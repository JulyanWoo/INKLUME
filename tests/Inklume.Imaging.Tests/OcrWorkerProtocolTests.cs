using System.Text.Json;
using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Domain.TextRegions;
using Inklume.Imaging.Worker;
using Xunit;

namespace Inklume.Imaging.Tests;

public sealed class OcrWorkerProtocolTests
{
    [Fact]
    public async Task HealthAsync_ShouldCorrelateRequestAndParseResult()
    {
        var transport = new FakeWorkerTransport();
        await using var client = new OcrWorkerClient(transport);

        transport.OnSendLine = line =>
        {
            var request = JsonSerializer.Deserialize<OcrRequest>(line)!;
            Assert.Equal("health", request.Method);
            Assert.Equal(OcrProtocolConstants.CurrentProtocolVersion, request.ProtocolVersion);

            var healthDto = new OcrHealthResultDto
            {
                PythonVersion = "3.11.9",
                PaddleOcrVersion = "3.7.0",
                PaddlePaddleVersion = "3.3.1",
                Device = "cpu",
                DetectorModel = "PP-OCRv5_mobile_det",
                RecognitionModel = "korean_PP-OCRv5_mobile_rec",
                ModelProfile = "Korean + English",
                ModelsAvailable = true
            };

            var response = new OcrResponse
            {
                ProtocolVersion = OcrProtocolConstants.CurrentProtocolVersion,
                RequestId = request.RequestId,
                Success = true,
                Result = JsonSerializer.SerializeToElement(healthDto)
            };

            transport.EnqueueResponse(JsonSerializer.Serialize(response));
        };

        OcrHealthResultDto health = await client.HealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal("3.11.9", health.PythonVersion);
        Assert.Equal("3.7.0", health.PaddleOcrVersion);
        Assert.Equal("3.3.1", health.PaddlePaddleVersion);
        Assert.True(health.ModelsAvailable);
    }

    [Fact]
    public async Task OcrPageAsync_ShouldSendPayloadAndParseDetections()
    {
        var transport = new FakeWorkerTransport();
        await using var client = new OcrWorkerClient(transport);

        transport.OnSendLine = line =>
        {
            var request = JsonSerializer.Deserialize<OcrRequest>(line)!;
            Assert.Equal("ocr_page", request.Method);

            var pageDto = new OcrPageResultDto
            {
                Regions =
                [
                    new OcrDetectedRegionDto
                    {
                        Text = "만화 텍스트",
                        RecognitionConfidence = 0.94,
                        DetectionConfidence = 0.96,
                        Points =
                        [
                            new ImagePointDto(10, 10),
                            new ImagePointDto(100, 10),
                            new ImagePointDto(100, 40),
                            new ImagePointDto(10, 40)
                        ]
                    }
                ],
                EngineName = "PaddleOCR",
                EngineVersion = "3.7.0",
                DetectionModel = "PP-OCRv5_mobile_det",
                RecognitionModel = "korean_PP-OCRv5_mobile_rec",
                ModelProfile = "Korean + English"
            };

            var response = new OcrResponse
            {
                ProtocolVersion = OcrProtocolConstants.CurrentProtocolVersion,
                RequestId = request.RequestId,
                Success = true,
                Result = JsonSerializer.SerializeToElement(pageDto)
            };

            transport.EnqueueResponse(JsonSerializer.Serialize(response));
        };

        var provider = new PaddleOcrProvider(client);
        var result = await provider.AnalyzePageAsync(@"C:\test\page.png", TestContext.Current.CancellationToken);

        Assert.Equal("PaddleOCR", result.EngineInfo.EngineName);
        Assert.Equal("korean_PP-OCRv5_mobile_rec", result.EngineInfo.RecognitionModel);

        OcrDetectedRegion region = Assert.Single(result.Regions);
        Assert.Equal("만화 텍스트", region.Text);
        Assert.Equal(0.94, region.RecognitionConfidence);
        Assert.Equal(0.96, region.DetectionConfidence);
        Assert.Equal(4, region.Geometry.Points.Count);
    }

    [Fact]
    public async Task ExecuteCommand_ShouldThrowOnProtocolVersionMismatch()
    {
        var transport = new FakeWorkerTransport();
        await using var client = new OcrWorkerClient(transport);

        transport.OnSendLine = line =>
        {
            var request = JsonSerializer.Deserialize<OcrRequest>(line)!;
            var response = new OcrResponse
            {
                ProtocolVersion = 999, // Mismatched version
                RequestId = request.RequestId,
                Success = true,
                Result = JsonSerializer.SerializeToElement(new object())
            };
            transport.EnqueueResponse(JsonSerializer.Serialize(response));
        };

        ProjectOperationException ex = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            client.HealthAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.IncompatibleVersion, ex.Code);
    }

    [Fact]
    public async Task ExecuteCommand_ShouldThrowOnMalformedJson()
    {
        var transport = new FakeWorkerTransport();
        await using var client = new OcrWorkerClient(transport);

        transport.OnSendLine = _ =>
        {
            transport.EnqueueResponse("This is not JSON!");
        };

        ProjectOperationException ex = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            client.HealthAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.OcrInvalidResult, ex.Code);
    }

    [Fact]
    public async Task ExecuteCommand_ShouldThrowOnWorkerReportedError()
    {
        var transport = new FakeWorkerTransport();
        await using var client = new OcrWorkerClient(transport);

        transport.OnSendLine = line =>
        {
            var request = JsonSerializer.Deserialize<OcrRequest>(line)!;
            var response = new OcrResponse
            {
                ProtocolVersion = OcrProtocolConstants.CurrentProtocolVersion,
                RequestId = request.RequestId,
                Success = false,
                Error = new OcrErrorPayload { Code = "ModelNotFound", Message = "Recognition model not found." }
            };
            transport.EnqueueResponse(JsonSerializer.Serialize(response));
        };

        ProjectOperationException ex = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            client.HealthAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ProjectErrorCode.OcrProviderFailure, ex.Code);
        Assert.Contains("ModelNotFound", ex.Message);
    }

    [Fact]
    public void NormalizePolygonPoints_ShouldRepairSelfIntersectingBowtieQuad()
    {
        // Hourglass / bowtie ordering: (0,0) -> (10,10) -> (10,0) -> (0,10)
        ImagePoint[] bowtie =
        [
            new ImagePoint(0, 0),
            new ImagePoint(10, 10),
            new ImagePoint(10, 0),
            new ImagePoint(0, 10)
        ];

        ImagePoint[] normalized = PaddleOcrProvider.NormalizePolygonPoints(bowtie);

        // A PolygonTextRegionGeometry must be valid and non-self-intersecting
        var geometry = new PolygonTextRegionGeometry(normalized);
        Assert.Equal(4, geometry.Points.Count);
        Assert.Equal(10, geometry.Bounds.Width);
        Assert.Equal(10, geometry.Bounds.Height);
    }

    private sealed class FakeWorkerTransport : IOcrWorkerTransport
    {
        private readonly Queue<string> _responses = new();

        public bool IsRunning { get; private set; } = true;

        public Action<string>? OnSendLine { get; set; }

        public void EnqueueResponse(string line) => _responses.Enqueue(line);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task SendLineAsync(string jsonLine, CancellationToken cancellationToken)
        {
            OnSendLine?.Invoke(jsonLine);
            return Task.CompletedTask;
        }

        public Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (_responses.Count > 0)
            {
                return Task.FromResult<string?>(_responses.Dequeue());
            }

            return Task.FromResult<string?>(null);
        }

        public string GetRecentStderr() => "Fake stderr diagnostic";

        public Task StopAsync(CancellationToken cancellationToken)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }
}
