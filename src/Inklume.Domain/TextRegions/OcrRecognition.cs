namespace Inklume.Domain.TextRegions;

public sealed record OcrRecognition
{
    public OcrRecognition(
        Guid id,
        Guid textRegionId,
        string text,
        double recognitionConfidence,
        double? detectionConfidence,
        string engineName,
        string engineVersion,
        string detectionModel,
        string recognitionModel,
        string modelProfile,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The OCR recognition identifier must not be empty.", nameof(id));
        }

        if (textRegionId == Guid.Empty)
        {
            throw new ArgumentException("The text region identifier must not be empty.", nameof(textRegionId));
        }

        ArgumentNullException.ThrowIfNull(text);

        if (recognitionConfidence is < 0.0 or > 1.0 || double.IsNaN(recognitionConfidence) || double.IsInfinity(recognitionConfidence))
        {
            throw new ArgumentOutOfRangeException(
                nameof(recognitionConfidence),
                recognitionConfidence,
                "Recognition confidence must be between 0.0 and 1.0.");
        }

        if (detectionConfidence.HasValue && (detectionConfidence.Value is < 0.0 or > 1.0 || double.IsNaN(detectionConfidence.Value) || double.IsInfinity(detectionConfidence.Value)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(detectionConfidence),
                detectionConfidence,
                "Detection confidence must be between 0.0 and 1.0 when provided.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(engineName);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(detectionModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(recognitionModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelProfile);

        if (updatedAt < createdAt)
        {
            throw new ArgumentException("The update timestamp must not precede creation.", nameof(updatedAt));
        }

        Id = id;
        TextRegionId = textRegionId;
        Text = text;
        RecognitionConfidence = recognitionConfidence;
        DetectionConfidence = detectionConfidence;
        EngineName = engineName.Trim();
        EngineVersion = engineVersion.Trim();
        DetectionModel = detectionModel.Trim();
        RecognitionModel = recognitionModel.Trim();
        ModelProfile = modelProfile.Trim();
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public Guid Id { get; }

    public Guid TextRegionId { get; }

    public string Text { get; }

    public double RecognitionConfidence { get; }

    public double? DetectionConfidence { get; }

    public string EngineName { get; }

    public string EngineVersion { get; }

    public string DetectionModel { get; }

    public string RecognitionModel { get; }

    public string ModelProfile { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }
}
