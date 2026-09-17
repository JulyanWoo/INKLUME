using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inklume.Imaging.Worker;

public static class OcrProtocolConstants
{
    public const int CurrentProtocolVersion = 1;

    public const string MethodHealth = "health";
    public const string MethodInitialize = "initialize";
    public const string MethodOcrPage = "ocr_page";
    public const string MethodOcrRegion = "ocr_region";
    public const string MethodShutdown = "shutdown";
}

public sealed class OcrRequest
{
    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; set; } = OcrProtocolConstants.CurrentProtocolVersion;

    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public object? Payload { get; set; }
}

public sealed class OcrResponse
{
    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; set; }

    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    public OcrErrorPayload? Error { get; set; }
}

public sealed class OcrErrorPayload
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public sealed class OcrHealthResultDto
{
    [JsonPropertyName("pythonVersion")]
    public string PythonVersion { get; set; } = string.Empty;

    [JsonPropertyName("paddleOcrVersion")]
    public string PaddleOcrVersion { get; set; } = string.Empty;

    [JsonPropertyName("paddlePaddleVersion")]
    public string PaddlePaddleVersion { get; set; } = string.Empty;

    [JsonPropertyName("device")]
    public string Device { get; set; } = "cpu";

    [JsonPropertyName("detectorModel")]
    public string DetectorModel { get; set; } = string.Empty;

    [JsonPropertyName("recognitionModel")]
    public string RecognitionModel { get; set; } = string.Empty;

    [JsonPropertyName("modelProfile")]
    public string ModelProfile { get; set; } = string.Empty;

    [JsonPropertyName("modelsAvailable")]
    public bool ModelsAvailable { get; set; }
}

public sealed class OcrPagePayload
{
    [JsonPropertyName("imagePath")]
    public string ImagePath { get; set; } = string.Empty;

    [JsonPropertyName("tileThreshold")]
    public int? TileThreshold { get; set; }

    [JsonPropertyName("tileHeight")]
    public int? TileHeight { get; set; }

    [JsonPropertyName("tileOverlap")]
    public int? TileOverlap { get; set; }
}

public sealed class OcrRegionPayload
{
    [JsonPropertyName("imagePath")]
    public string ImagePath { get; set; } = string.Empty;

    [JsonPropertyName("points")]
    public List<ImagePointDto> Points { get; set; } = [];
}

public sealed class ImagePointDto
{
    public ImagePointDto()
    {
    }

    public ImagePointDto(double x, double y)
    {
        X = x;
        Y = y;
    }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public sealed class OcrDetectedRegionDto
{
    [JsonPropertyName("points")]
    public List<ImagePointDto> Points { get; set; } = [];

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("recognitionConfidence")]
    public double RecognitionConfidence { get; set; }

    [JsonPropertyName("detectionConfidence")]
    public double? DetectionConfidence { get; set; }
}

public sealed class OcrPageResultDto
{
    [JsonPropertyName("regions")]
    public List<OcrDetectedRegionDto> Regions { get; set; } = [];

    [JsonPropertyName("engineName")]
    public string EngineName { get; set; } = "PaddleOCR";

    [JsonPropertyName("engineVersion")]
    public string EngineVersion { get; set; } = "3.7.0";

    [JsonPropertyName("detectionModel")]
    public string DetectionModel { get; set; } = "PP-OCRv5_mobile_det";

    [JsonPropertyName("recognitionModel")]
    public string RecognitionModel { get; set; } = "korean_PP-OCRv5_mobile_rec";

    [JsonPropertyName("modelProfile")]
    public string ModelProfile { get; set; } = "Korean + English";
}

public sealed class OcrRegionResultDto
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("recognitionConfidence")]
    public double RecognitionConfidence { get; set; }

    [JsonPropertyName("engineName")]
    public string EngineName { get; set; } = "PaddleOCR";

    [JsonPropertyName("engineVersion")]
    public string EngineVersion { get; set; } = "3.7.0";

    [JsonPropertyName("detectionModel")]
    public string DetectionModel { get; set; } = "PP-OCRv5_mobile_det";

    [JsonPropertyName("recognitionModel")]
    public string RecognitionModel { get; set; } = "korean_PP-OCRv5_mobile_rec";

    [JsonPropertyName("modelProfile")]
    public string ModelProfile { get; set; } = "Korean + English";
}
