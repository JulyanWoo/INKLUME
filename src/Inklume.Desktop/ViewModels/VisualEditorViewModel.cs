using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inklume.Application.Chapters;
using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Desktop.Services;
using Inklume.Desktop.VisualEditor;
using Inklume.Domain.TextRegions;

namespace Inklume.Desktop.ViewModels;

public sealed partial class VisualEditorViewModel : ObservableObject
{
    private const double MinimumRectangleExtent = 3;
    private readonly ProjectWorkspace _workspace;
    private readonly ChapterService _chapterService;
    private readonly TextRegionService _textRegionService;
    private readonly IPagePreviewLoader _previewLoader;
    private readonly OcrService? _ocrService;
    private Dictionary<Guid, OcrRecognition> _ocrRecognitions = [];
    private CancellationTokenSource? _ocrCancellation;
    private ViewportTransform? _viewportTransform;
    private int _genericImageWidth;
    private int _genericImageHeight;
    private double _viewportWidth;
    private double _viewportHeight;
    private string? _genericImageFilePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPage))]
    [NotifyPropertyChangedFor(nameof(IsEditingEnabled))]
    [NotifyPropertyChangedFor(nameof(IsGenericPreview))]
    [NotifyPropertyChangedFor(nameof(ImageWidth))]
    [NotifyPropertyChangedFor(nameof(ImageHeight))]
    [NotifyCanExecuteChangedFor(nameof(RunPageOcrCommand))]
    private PageWorkspace? _selectedPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImage))]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private PagePreview? _preview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRegion))]
    [NotifyPropertyChangedFor(nameof(SelectedRegionBoundsDisplay))]
    private TextRegion? _selectedRegion;

    [ObservableProperty]
    private VisualEditorTool _selectedTool;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunPageOcrCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelOcrCommand))]
    private bool _isOcrRunning;

    [ObservableProperty]
    private string _ocrProgressMessage = string.Empty;

    public VisualEditorViewModel(
        ProjectWorkspace workspace,
        ChapterService chapterService,
        TextRegionService textRegionService,
        IPagePreviewLoader previewLoader,
        OcrService? ocrService = null,
        TextRegionReviewService? reviewService = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(chapterService);
        ArgumentNullException.ThrowIfNull(textRegionService);
        ArgumentNullException.ThrowIfNull(previewLoader);
        _workspace = workspace;
        _chapterService = chapterService;
        _textRegionService = textRegionService;
        _previewLoader = previewLoader;
        _ocrService = ocrService;

        var actualReviewService = reviewService ?? new TextRegionReviewService(
            new Inklume.Infrastructure.TextRegions.SqliteTextRegionStore(),
            new Inklume.Infrastructure.TextRegions.SqliteOcrStore(),
            TimeProvider.System);
        Review = new RegionReviewViewModel(this, actualReviewService, textRegionService, ocrService);

        SelectToolCommand = new RelayCommand(() => SelectedTool = VisualEditorTool.Select);
        RectangleToolCommand = new RelayCommand(
            () => SelectedTool = VisualEditorTool.RectangleRegion,
            () => IsEditingEnabled);
        PolygonToolCommand = new RelayCommand(
            () => SelectedTool = VisualEditorTool.PolygonRegion,
            () => IsEditingEnabled);
        ZoomInCommand = new RelayCommand(ZoomIn, () => ZoomScale < ViewportTransform.MaximumZoom - 0.0001);
        ZoomOutCommand = new RelayCommand(ZoomOut, () => ZoomScale > ViewportTransform.MinimumInteractiveZoom + 0.0001);
        ResetZoomCommand = new RelayCommand(ResetZoom, () => _viewportTransform is not null);
        FitToViewCommand = new RelayCommand(FitToView, () => _viewportTransform is not null);
        DeleteSelectedRegionCommand = new AsyncRelayCommand(
            DeleteSelectedRegionAsync, () => IsEditingEnabled && SelectedRegion is not null);
        RunPageOcrCommand = new AsyncRelayCommand(RunPageOcrAsync, CanRunPageOcr);
        CancelOcrCommand = new RelayCommand(CancelOcr, () => IsOcrRunning);
    }

    public RegionReviewViewModel Review { get; }

    public ProjectWorkspace Workspace => _workspace;

    public string SelectedRegionOcrText => Review.RawOcrText;

    public string SelectedRegionOcrRecognitionConfidenceDisplay => Review.RecognitionConfidenceDisplay;

    public string SelectedRegionOcrDetectionConfidenceDisplay => Review.DetectionConfidenceDisplay;

    public string SelectedRegionOcrEngineDisplay => Review.EngineDisplay;

    public string SelectedRegionOcrModelDisplay => Review.ModelDisplay;

    public string SelectedRegionOcrProfileDisplay => Review.ProfileDisplay;

    public bool HasSelectedRegionOcr => Review.HasOcr;

    public string SelectedRegionOriginDisplay => Review.OriginDisplay;

    public IAsyncRelayCommand RecognizeSelectedRegionCommand => Review.RecognizeRegionCommand;

    public bool TryGetRecognition(Guid regionId, out OcrRecognition? recognition)
        => _ocrRecognitions.TryGetValue(regionId, out recognition);

    public void SetRegionRecognition(Guid regionId, OcrRecognition recognition)
    {
        _ocrRecognitions[regionId] = recognition;
        Review.NotifyOcrChanged();
    }

    public ObservableCollection<TextRegion> TextRegions { get; } = [];

    public IReadOnlyList<TextRegionRole> Roles { get; } = Enum.GetValues<TextRegionRole>();

    public IReadOnlyList<TextContainerType> ContainerTypes { get; } = Enum.GetValues<TextContainerType>();

    public bool HasPage => SelectedPage is not null;

    public bool HasPreview => Preview is not null;

    public bool IsEditingEnabled => SelectedPage is not null;

    public bool IsGenericPreview => _genericImageFilePath is not null && SelectedPage is null;

    public string? GenericImageFilePath => _genericImageFilePath;

    public bool HasSelectedRegion => SelectedRegion is not null;

    public string SelectedRegionBoundsDisplay
    {
        get
        {
            ImageBounds? bounds = SelectedRegion?.Geometry.Bounds;
            return bounds is null
                ? string.Empty
                : $"{bounds.Value.X:0.##}, {bounds.Value.Y:0.##}  {bounds.Value.Width:0.##} x {bounds.Value.Height:0.##}";
        }
    }

    public ImageSource? PreviewImage => Preview?.Image;

    public double ImageWidth => SelectedPage?.Page.PixelWidth ?? _genericImageWidth;

    public double ImageHeight => SelectedPage?.Page.PixelHeight ?? _genericImageHeight;

    public int DecodedPixelWidth => Preview?.DecodedPixelWidth ?? 0;

    public int DecodedPixelHeight => Preview?.DecodedPixelHeight ?? 0;

    public double ZoomScale => _viewportTransform?.Zoom ?? 1;

    public string ZoomPercentDisplay => $"{Math.Round(ZoomScale * 100, 1):0.#}%";

    public double ViewportOffsetX => _viewportTransform?.OffsetX ?? 0;

    public double ViewportOffsetY => _viewportTransform?.OffsetY ?? 0;

    public IRelayCommand SelectToolCommand { get; }

    public IRelayCommand RectangleToolCommand { get; }

    public IRelayCommand PolygonToolCommand { get; }

    public IRelayCommand ZoomInCommand { get; }

    public IRelayCommand ZoomOutCommand { get; }

    public IRelayCommand ResetZoomCommand { get; }

    public IRelayCommand FitToViewCommand { get; }

    public IAsyncRelayCommand DeleteSelectedRegionCommand { get; }

    public IAsyncRelayCommand RunPageOcrCommand { get; }

    public IRelayCommand CancelOcrCommand { get; }

    public async Task<PageWorkspace> LoadPageAsync(
        PageWorkspace page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ClearTransientState();
        ErrorMessage = string.Empty;
        StatusMessage = $"Loading page {page.Page.Number}...";
        PageWorkspace resolved = await _chapterService.EnsurePageDimensionsAsync(
            _workspace, page.Page.Id, cancellationToken);
        int rawPixelWidth = resolved.Page.PixelWidth.GetValueOrDefault();
        int rawPixelHeight = resolved.Page.PixelHeight.GetValueOrDefault();
        PagePreview preview = await _previewLoader.LoadAsync(
            resolved.FilePath, rawPixelWidth, rawPixelHeight, cancellationToken);
        IReadOnlyList<TextRegion> regions = await _textRegionService.GetForPageAsync(
            _workspace, resolved.Page.Id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        _genericImageFilePath = null;
        _genericImageWidth = 0;
        _genericImageHeight = 0;
        SelectedPage = resolved;
        Preview = preview;
        _viewportTransform = (_viewportWidth > 0 && _viewportHeight > 0 && rawPixelWidth > 0 && rawPixelHeight > 0)
            ? ViewportTransform.Fit(rawPixelWidth, rawPixelHeight, _viewportWidth, _viewportHeight)
            : null;
        TextRegions.Clear();
        foreach (TextRegion region in regions)
        {
            TextRegions.Add(region);
        }

        if (_ocrService is not null)
        {
            try
            {
                IReadOnlyDictionary<Guid, OcrRecognition> recognitions = await _ocrService.GetRecognitionsForPageAsync(
                    _workspace, resolved.Page.Id, cancellationToken);
                _ocrRecognitions = new Dictionary<Guid, OcrRecognition>(recognitions);
            }
            catch
            {
                _ocrRecognitions = [];
            }
        }
        else
        {
            _ocrRecognitions = [];
        }

        Review.NotifyOcrChanged();
        Review.OnRegionsChanged();
        OnPropertyChanged(nameof(GenericImageFilePath));
        OnPropertyChanged(nameof(IsGenericPreview));
        NotifyViewportChanged();
        StatusMessage = $"Page {resolved.Page.Number} loaded.";
        return resolved;
    }

    public async Task LoadGenericImageAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ClearTransientState();
        ErrorMessage = string.Empty;
        StatusMessage = $"Loading image {Path.GetFileName(filePath)}...";

        PagePreview preview = await _previewLoader.LoadImageAsync(filePath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        SelectedPage = null;
        _genericImageFilePath = filePath;
        _genericImageWidth = preview.RawPixelWidth;
        _genericImageHeight = preview.RawPixelHeight;
        Preview = preview;
        _viewportTransform = (_viewportWidth > 0 && _viewportHeight > 0 && preview.RawPixelWidth > 0 && preview.RawPixelHeight > 0)
            ? ViewportTransform.Fit(preview.RawPixelWidth, preview.RawPixelHeight, _viewportWidth, _viewportHeight)
            : null;
        TextRegions.Clear();
        _ocrRecognitions.Clear();
        Review.NotifyOcrChanged();
        Review.OnRegionsChanged();

        OnPropertyChanged(nameof(GenericImageFilePath));
        OnPropertyChanged(nameof(IsGenericPreview));
        OnPropertyChanged(nameof(IsEditingEnabled));
        OnPropertyChanged(nameof(ImageWidth));
        OnPropertyChanged(nameof(ImageHeight));
        RectangleToolCommand.NotifyCanExecuteChanged();
        PolygonToolCommand.NotifyCanExecuteChanged();
        DeleteSelectedRegionCommand.NotifyCanExecuteChanged();

        NotifyViewportChanged();
        StatusMessage = $"Image {Path.GetFileName(filePath)} loaded (read-only preview).";
    }

    public void Clear()
    {
        ClearTransientState();
        SelectedPage = null;
        _genericImageFilePath = null;
        _genericImageWidth = 0;
        _genericImageHeight = 0;
        Preview = null;
        TextRegions.Clear();
        _ocrRecognitions.Clear();
        Review.NotifyOcrChanged();
        Review.OnRegionsChanged();
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(GenericImageFilePath));
        OnPropertyChanged(nameof(IsGenericPreview));
        OnPropertyChanged(nameof(IsEditingEnabled));
        RectangleToolCommand.NotifyCanExecuteChanged();
        PolygonToolCommand.NotifyCanExecuteChanged();
        DeleteSelectedRegionCommand.NotifyCanExecuteChanged();
        _viewportTransform = null;
        NotifyViewportChanged();
    }

    public void ConfigureViewport(double width, double height, bool fitIfUninitialized)
    {
        if ((!HasPage && !IsGenericPreview) || width <= 0 || height <= 0)
        {
            return;
        }

        _viewportWidth = width;
        _viewportHeight = height;
        _viewportTransform = _viewportTransform is null
            ? ViewportTransform.Fit(ImageWidth, ImageHeight, width, height)
            : _viewportTransform.Resize(width, height);
        if (fitIfUninitialized && _viewportTransform is not null)
        {
            _viewportTransform = ViewportTransform.Fit(ImageWidth, ImageHeight, width, height);
        }

        NotifyViewportChanged();
    }

    public void ZoomAt(ViewportPoint cursor, double wheelDelta)
    {
        ViewportTransform? transform = EnsureViewportTransform();
        if (transform is null)
        {
            return;
        }

        double targetZoom = wheelDelta > 0
            ? ZoomLevels.GetNextZoomIn(transform.Zoom)
            : ZoomLevels.GetNextZoomOut(transform.Zoom);
        _viewportTransform = transform.ZoomAt(cursor, targetZoom);
        NotifyViewportChanged();
    }

    public void PanBy(double horizontalDelta, double verticalDelta)
    {
        ViewportTransform? transform = EnsureViewportTransform();
        if (transform is null)
        {
            return;
        }

        _viewportTransform = transform.PanBy(horizontalDelta, verticalDelta);
        NotifyViewportChanged();
    }

    public ImagePoint ViewportToImage(ViewportPoint point)
    {
        ViewportTransform? transform = EnsureViewportTransform();
        return transform?.ViewportToImage(point)
            ?? throw new InvalidOperationException("The visual editor viewport has not been initialized.");
    }

    public async Task<TextRegion?> CreateRectangleAsync(
        ImagePoint first,
        ImagePoint second,
        CancellationToken cancellationToken = default)
    {
        if (SelectedPage is null)
        {
            return null;
        }

        double x = Math.Min(first.X, second.X);
        double y = Math.Min(first.Y, second.Y);
        double width = Math.Abs(second.X - first.X);
        double height = Math.Abs(second.Y - first.Y);
        if (width < MinimumRectangleExtent || height < MinimumRectangleExtent)
        {
            return null;
        }

        var geometry = new RectangleTextRegionGeometry(x, y, width, height);
        return await CreateRegionAsync(geometry, cancellationToken);
    }

    public async Task<TextRegion?> CreatePolygonAsync(
        IReadOnlyList<ImagePoint> points,
        CancellationToken cancellationToken = default)
    {
        if (SelectedPage is null || points.Count < 3)
        {
            return null;
        }

        return await CreateRegionAsync(new PolygonTextRegionGeometry(points), cancellationToken);
    }

    public void SelectRegion(TextRegion? region)
    {
        if (region is not null)
        {
            TextRegion? matched = TextRegions.FirstOrDefault(r => r.Id == region.Id);
            if (matched is null)
            {
                throw new ArgumentException("The region does not belong to the selected page.", nameof(region));
            }

            region = matched;
        }

        if (Review.IsReviewTextDirty)
        {
            _ = Review.CommitDraftIfDirtyAsync();
        }

        SelectedRegion = region;
    }

    public async Task<bool> SelectRegionAsync(TextRegion? region)
    {
        if (region is not null)
        {
            TextRegion? matched = TextRegions.FirstOrDefault(r => r.Id == region.Id);
            if (matched is null)
            {
                throw new ArgumentException("The region does not belong to the selected page.", nameof(region));
            }

            region = matched;
        }

        if (Review.IsReviewTextDirty)
        {
            bool committed = await Review.CommitDraftIfDirtyAsync();
            if (!committed)
            {
                return false;
            }
        }

        SelectedRegion = region;
        return true;
    }

    public async Task UpdateSelectedClassificationAsync(
        TextRegionRole role,
        TextContainerType containerType,
        CancellationToken cancellationToken = default)
    {
        if (SelectedRegion is null)
        {
            return;
        }

        TextRegion original = SelectedRegion;
        TextRegion updated = await _textRegionService.UpdateClassificationAsync(
            _workspace, original, role, containerType, cancellationToken);
        int index = TextRegions.IndexOf(original);
        TextRegions[index] = updated;
        SelectedRegion = updated;
        StatusMessage = $"Region {updated.ReadingOrder} updated.";
    }

    public void CancelTransientOperation()
    {
        StatusMessage = HasPage ? "Drawing cancelled." : string.Empty;
    }

    public void ReportInteractionError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ErrorMessage = exception is ProjectOperationException projectError
            ? projectError.Message
            : "The visual editor operation could not be completed.";
        StatusMessage = "Visual editor operation failed.";
    }

    partial void OnSelectedRegionChanged(TextRegion? value)
    {
        DeleteSelectedRegionCommand.NotifyCanExecuteChanged();
        Review.OnSelectedRegionChanged(value);
        NotifyOcrForwardingPropertiesChanged();
    }

    private void NotifyOcrForwardingPropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedRegionOcrText));
        OnPropertyChanged(nameof(SelectedRegionOriginDisplay));
        OnPropertyChanged(nameof(SelectedRegionOcrRecognitionConfidenceDisplay));
        OnPropertyChanged(nameof(SelectedRegionOcrDetectionConfidenceDisplay));
        OnPropertyChanged(nameof(SelectedRegionOcrEngineDisplay));
        OnPropertyChanged(nameof(SelectedRegionOcrModelDisplay));
        OnPropertyChanged(nameof(SelectedRegionOcrProfileDisplay));
        OnPropertyChanged(nameof(HasSelectedRegionOcr));
        OnPropertyChanged(nameof(RecognizeSelectedRegionCommand));
    }

    private async Task<TextRegion> CreateRegionAsync(
        TextRegionGeometry geometry,
        CancellationToken cancellationToken)
    {
        PageWorkspace page = SelectedPage
            ?? throw new InvalidOperationException("A page must be selected before creating a text region.");
        TextRegion region = await _textRegionService.CreateAsync(
            _workspace,
            page.Page.Id,
            geometry,
            cancellationToken: cancellationToken);
        TextRegions.Add(region);
        SelectedRegion = region;
        StatusMessage = $"Region {region.ReadingOrder} created.";
        return region;
    }

    private async Task DeleteSelectedRegionAsync()
    {
        TextRegion? region = SelectedRegion;
        if (region is null)
        {
            return;
        }

        await _textRegionService.DeleteAsync(_workspace, region);
        _ocrRecognitions.Remove(region.Id);
        TextRegions.Remove(region);
        SelectedRegion = null;
        StatusMessage = $"Region {region.ReadingOrder} deleted.";
    }

    private ViewportTransform? EnsureViewportTransform()
    {
        if (_viewportTransform is not null)
        {
            return _viewportTransform;
        }

        if ((!HasPage && !IsGenericPreview) || ImageWidth <= 0 || ImageHeight <= 0)
        {
            return null;
        }

        double width = _viewportWidth > 0 ? _viewportWidth : Math.Max(ImageWidth, 800);
        double height = _viewportHeight > 0 ? _viewportHeight : Math.Max(ImageHeight, 600);
        _viewportTransform = ViewportTransform.Fit(ImageWidth, ImageHeight, width, height);
        NotifyViewportChanged();
        return _viewportTransform;
    }

    private void ZoomIn()
    {
        ViewportTransform? transform = EnsureViewportTransform();
        if (transform is null)
        {
            return;
        }

        var center = new ViewportPoint(
            transform.ViewportWidth / 2,
            transform.ViewportHeight / 2);
        double targetZoom = ZoomLevels.GetNextZoomIn(transform.Zoom);
        _viewportTransform = transform.ZoomAt(center, targetZoom);
        NotifyViewportChanged();
    }

    private void ZoomOut()
    {
        ViewportTransform? transform = EnsureViewportTransform();
        if (transform is null)
        {
            return;
        }

        var center = new ViewportPoint(
            transform.ViewportWidth / 2,
            transform.ViewportHeight / 2);
        double targetZoom = ZoomLevels.GetNextZoomOut(transform.Zoom);
        _viewportTransform = transform.ZoomAt(center, targetZoom);
        NotifyViewportChanged();
    }

    private void ResetZoom()
    {
        ViewportTransform? transform = EnsureViewportTransform();
        if (transform is null)
        {
            return;
        }

        _viewportTransform = transform.Reset();
        NotifyViewportChanged();
    }

    private void FitToView()
    {
        ViewportTransform? transform = EnsureViewportTransform();
        if (transform is null)
        {
            return;
        }

        _viewportTransform = ViewportTransform.Fit(
            ImageWidth,
            ImageHeight,
            transform.ViewportWidth,
            transform.ViewportHeight);
        NotifyViewportChanged();
    }

    private void ClearTransientState()
    {
        SelectedRegion = null;
        SelectedTool = VisualEditorTool.Select;
    }

    private void NotifyViewportChanged()
    {
        OnPropertyChanged(nameof(ZoomScale));
        OnPropertyChanged(nameof(ZoomPercentDisplay));
        OnPropertyChanged(nameof(ViewportOffsetX));
        OnPropertyChanged(nameof(ViewportOffsetY));
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
        ResetZoomCommand.NotifyCanExecuteChanged();
        FitToViewCommand.NotifyCanExecuteChanged();
    }

    private bool CanRunPageOcr()
        => HasPage && !IsGenericPreview && !IsOcrRunning && _ocrService is not null;

    private async Task RunPageOcrAsync()
    {
        if (SelectedPage is null || _ocrService is null)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _ocrCancellation = cancellation;
        IsOcrRunning = true;
        OcrProgressMessage = "Analyzing page with PaddleOCR...";
        ErrorMessage = string.Empty;
        NotifyOcrCommandsCanExecuteChanged();

        try
        {
            IReadOnlyList<TextRegion> updatedRegions = await _ocrService.AnalyzePageAsync(
                _workspace, SelectedPage.Page.Id, cancellation.Token);
            TextRegions.Clear();
            foreach (TextRegion region in updatedRegions)
            {
                TextRegions.Add(region);
            }

            IReadOnlyDictionary<Guid, OcrRecognition> recognitions = await _ocrService.GetRecognitionsForPageAsync(
                _workspace, SelectedPage.Page.Id, cancellation.Token);
            _ocrRecognitions = new Dictionary<Guid, OcrRecognition>(recognitions);
            SelectedRegion = null;
            StatusMessage = $"OCR completed: {updatedRegions.Count} region(s) detected.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusMessage = "OCR cancelled.";
        }
        catch (ProjectOperationException exception)
        {
            ErrorMessage = exception.Message;
            StatusMessage = "OCR failed.";
        }
        catch (Exception)
        {
            ErrorMessage = "An unexpected error occurred during OCR.";
            StatusMessage = "OCR failed.";
        }
        finally
        {
            _ocrCancellation = null;
            IsOcrRunning = false;
            OcrProgressMessage = string.Empty;
            NotifyOcrCommandsCanExecuteChanged();
        }
    }

    private void CancelOcr()
    {
        _ocrCancellation?.Cancel();
    }

    private void NotifyOcrCommandsCanExecuteChanged()
    {
        RunPageOcrCommand.NotifyCanExecuteChanged();
        Review.RecognizeRegionCommand.NotifyCanExecuteChanged();
        CancelOcrCommand.NotifyCanExecuteChanged();
    }
}
