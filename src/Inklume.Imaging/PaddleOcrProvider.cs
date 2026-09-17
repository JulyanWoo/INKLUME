using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Domain.TextRegions;
using Inklume.Imaging.Worker;

namespace Inklume.Imaging;

public sealed class PaddleOcrProvider(OcrWorkerClient workerClient) : IOcrProvider
{
    public async Task<OcrPageResult> AnalyzePageAsync(string imagePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        OcrPageResultDto dto = await workerClient.OcrPageAsync(imagePath, cancellationToken: cancellationToken);

        var detectedRegions = new List<OcrDetectedRegion>();
        foreach (OcrDetectedRegionDto regionDto in dto.Regions)
        {
            if (regionDto.Points.Count < 3)
            {
                continue;
            }

            ImagePoint[] rawPoints = [.. regionDto.Points.Select(p => new ImagePoint(p.X, p.Y))];
            ImagePoint[] canonicalPoints = NormalizePolygonPoints(rawPoints);

            try
            {
                var geometry = new PolygonTextRegionGeometry(canonicalPoints);
                detectedRegions.Add(new OcrDetectedRegion(
                    geometry,
                    regionDto.Text,
                    regionDto.RecognitionConfidence,
                    regionDto.DetectionConfidence));
            }
            catch (ArgumentException)
            {
                // If a detection produced degenerate or non-repairable geometry, skip to prevent crashing whole page
            }
        }

        var engineMetadata = new OcrEngineMetadata(
            dto.EngineName,
            dto.EngineVersion,
            dto.DetectionModel,
            dto.RecognitionModel,
            dto.ModelProfile);

        return new OcrPageResult(detectedRegions, engineMetadata);
    }

    public async Task<OcrRegionResult> RecognizeRegionAsync(
        string imagePath,
        TextRegionGeometry geometry,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(geometry);

        OcrRegionResultDto dto = await workerClient.OcrRegionAsync(imagePath, geometry.Points, cancellationToken);

        var engineMetadata = new OcrEngineMetadata(
            dto.EngineName,
            dto.EngineVersion,
            dto.DetectionModel,
            dto.RecognitionModel,
            dto.ModelProfile);

        return new OcrRegionResult(dto.Text, dto.RecognitionConfidence, engineMetadata);
    }

    public static ImagePoint[] NormalizePolygonPoints(IReadOnlyList<ImagePoint> points)
    {
        if (points.Count <= 3)
        {
            return [.. points];
        }

        // Compute centroid
        double centroidX = points.Average(p => p.X);
        double centroidY = points.Average(p => p.Y);

        // Sort radially around centroid (clockwise / counter-clockwise) to eliminate self-intersections (e.g. bow-tie quads)
        return [.. points.OrderBy(p => Math.Atan2(p.Y - centroidY, p.X - centroidX))];
    }
}
