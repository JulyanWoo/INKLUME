using System.Text.Json;
using Inklume.Application.Projects;
using Inklume.Domain.TextRegions;

namespace Inklume.Imaging.Worker;

public sealed class OcrWorkerClient(IOcrWorkerTransport transport) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _isDisposed;

    public bool IsRunning => transport.IsRunning;

    public async Task<OcrHealthResultDto> HealthAsync(CancellationToken cancellationToken = default)
    {
        JsonElement result = await ExecuteCommandAsync(
            OcrProtocolConstants.MethodHealth, null, cancellationToken);
        return JsonSerializer.Deserialize<OcrHealthResultDto>(result.GetRawText(), JsonOptions)
            ?? throw new ProjectOperationException(
                ProjectErrorCode.OcrInvalidResult, "OCR worker returned empty health metadata.");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteCommandAsync(OcrProtocolConstants.MethodInitialize, null, cancellationToken);
    }

    public async Task<OcrPageResultDto> OcrPageAsync(
        string imagePath,
        int? tileThreshold = null,
        int? tileHeight = null,
        int? tileOverlap = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        var payload = new OcrPagePayload
        {
            ImagePath = imagePath,
            TileThreshold = tileThreshold,
            TileHeight = tileHeight,
            TileOverlap = tileOverlap
        };

        JsonElement result = await ExecuteCommandAsync(
            OcrProtocolConstants.MethodOcrPage, payload, cancellationToken);

        return JsonSerializer.Deserialize<OcrPageResultDto>(result.GetRawText(), JsonOptions)
            ?? throw new ProjectOperationException(
                ProjectErrorCode.OcrInvalidResult, "OCR worker returned empty page results.");
    }

    public async Task<OcrRegionResultDto> OcrRegionAsync(
        string imagePath,
        IReadOnlyList<ImagePoint> points,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(points);

        var payload = new OcrRegionPayload
        {
            ImagePath = imagePath,
            Points = points.Select(pt => new ImagePointDto(pt.X, pt.Y)).ToList()
        };

        JsonElement result = await ExecuteCommandAsync(
            OcrProtocolConstants.MethodOcrRegion, payload, cancellationToken);

        return JsonSerializer.Deserialize<OcrRegionResultDto>(result.GetRawText(), JsonOptions)
            ?? throw new ProjectOperationException(
                ProjectErrorCode.OcrInvalidResult, "OCR worker returned empty region results.");
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (!transport.IsRunning)
        {
            return;
        }

        try
        {
            await ExecuteCommandAsync(OcrProtocolConstants.MethodShutdown, null, cancellationToken);
        }
        catch
        {
            // Ignore shutdown errors; transport stop handles remaining cleanup
        }
        finally
        {
            await transport.StopAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            await ShutdownAsync(CancellationToken.None);
        }
        catch
        {
            // Ignore disposal errors
        }

        await transport.DisposeAsync();
        _semaphore.Dispose();
    }

    private async Task<JsonElement> ExecuteCommandAsync(
        string method,
        object? payload,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!transport.IsRunning)
            {
                await transport.StartAsync(cancellationToken);
            }

            string requestId = Guid.NewGuid().ToString("N");
            var request = new OcrRequest
            {
                ProtocolVersion = OcrProtocolConstants.CurrentProtocolVersion,
                RequestId = requestId,
                Method = method,
                Payload = payload
            };

            string requestLine = JsonSerializer.Serialize(request, JsonOptions);
            await transport.SendLineAsync(requestLine, cancellationToken);

            string? responseLine = await transport.ReadLineAsync(cancellationToken);
            if (responseLine is null)
            {
                string stderr = transport.GetRecentStderr();
                throw new ProjectOperationException(
                    ProjectErrorCode.OcrProviderFailure,
                    $"The OCR worker process terminated unexpectedly while processing method '{method}'. Stderr: {stderr}");
            }

            OcrResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<OcrResponse>(responseLine, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new ProjectOperationException(
                    ProjectErrorCode.OcrInvalidResult,
                    $"Received malformed JSON from OCR worker: {ex.Message}");
            }

            if (response is null || response.RequestId != requestId)
            {
                throw new ProjectOperationException(
                    ProjectErrorCode.OcrInvalidResult,
                    "OCR worker response request correlation identifier mismatch.");
            }

            if (response.ProtocolVersion != OcrProtocolConstants.CurrentProtocolVersion)
            {
                throw new ProjectOperationException(
                    ProjectErrorCode.IncompatibleVersion,
                    $"OCR worker protocol version mismatch: expected {OcrProtocolConstants.CurrentProtocolVersion}, received {response.ProtocolVersion}.");
            }

            if (!response.Success)
            {
                string code = response.Error?.Code ?? "WorkerError";
                string message = response.Error?.Message ?? "OCR worker reported an unhandled failure.";
                throw new ProjectOperationException(
                    ProjectErrorCode.OcrProviderFailure,
                    $"[{code}] {message}");
            }

            return response.Result ?? default;
        }
        catch (OperationCanceledException)
        {
            // Per requirement 53: cancel terminates worker process to ensure partial work is aborted cleanly
            await transport.StopAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
