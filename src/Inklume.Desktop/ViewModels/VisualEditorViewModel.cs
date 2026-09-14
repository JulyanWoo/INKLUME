using System.Collections.ObjectModel;
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
    private ViewportTransform? _viewportTransform;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPage))]
    [NotifyPropertyChangedFor(nameof(ImageWidth))]
    [NotifyPropertyChangedFor(nameof(ImageHeight))]
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

    public VisualEditorViewModel(
        ProjectWorkspace workspace,
        ChapterService chapterService,
        TextRegionService textRegionService,
        IPagePreviewLoader previewLoader)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(chapterService);
        ArgumentNullException.ThrowIfNull(textRegionService);
        ArgumentNullException.ThrowIfNull(previewLoader);
        _workspace = workspace;
        _chapterService = chapterService;
        _textRegionService = textRegionService;
        _previewLoader = previewLoader;
        SelectToolCommand = new RelayCommand(() => SelectedTool = VisualEditorTool.Select);
        RectangleToolCommand = new RelayCommand(() => SelectedTool = VisualEditorTool.RectangleRegion);
        PolygonToolCommand = new RelayCommand(() => SelectedTool = VisualEditorTool.PolygonRegion);
        ZoomInCommand = new RelayCommand(ZoomIn, () => ZoomScale < ViewportTransform.MaximumZoom);
        ZoomOutCommand = new RelayCommand(ZoomOut, () => ZoomScale > ViewportTransform.MinimumInteractiveZoom);
        ResetZoomCommand = new RelayCommand(ResetZoom, () => _viewportTransform is not null);
        FitToViewCommand = new RelayCommand(FitToView, () => _viewportTransform is not null);
        DeleteSelectedRegionCommand = new AsyncRelayCommand(
            DeleteSelectedRegionAsync, () => SelectedRegion is not null);
    }

    public ObservableCollection<TextRegion> TextRegions { get; } = [];

    public IReadOnlyList<TextRegionRole> Roles { get; } = Enum.GetValues<TextRegionRole>();

    public IReadOnlyList<TextContainerType> ContainerTypes { get; } = Enum.GetValues<TextContainerType>();

    public bool HasPage => SelectedPage is not null;

    public bool HasPreview => Preview is not null;

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

    public double ImageWidth => SelectedPage?.Page.PixelWidth ?? 0;

    public double ImageHeight => SelectedPage?.Page.PixelHeight ?? 0;

    public int DecodedPixelWidth => Preview?.DecodedPixelWidth ?? 0;

    public int DecodedPixelHeight => Preview?.DecodedPixelHeight ?? 0;

    public double ZoomScale => _viewportTransform?.Zoom ?? 1;

    public string ZoomPercentDisplay => $"{Math.Round(ZoomScale * 100):0}%";

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

        SelectedPage = resolved;
        Preview = preview;
        TextRegions.Clear();
        foreach (TextRegion region in regions)
        {
            TextRegions.Add(region);
        }

        _viewportTransform = null;
        NotifyViewportChanged();
        StatusMessage = $"Page {resolved.Page.Number} loaded.";
        return resolved;
    }

    public void Clear()
    {
        ClearTransientState();
        SelectedPage = null;
        Preview = null;
        TextRegions.Clear();
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        _viewportTransform = null;
        NotifyViewportChanged();
    }

    public void ConfigureViewport(double width, double height, bool fitIfUninitialized)
    {
        if (!HasPage || width <= 0 || height <= 0)
        {
            return;
        }

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
        if (_viewportTransform is null)
        {
            return;
        }

        double factor = wheelDelta > 0 ? 1.1 : 1 / 1.1;
        _viewportTransform = _viewportTransform.ZoomAt(cursor, _viewportTransform.Zoom * factor);
        NotifyViewportChanged();
    }

    public void PanBy(double horizontalDelta, double verticalDelta)
    {
        if (_viewportTransform is null)
        {
            return;
        }

        _viewportTransform = _viewportTransform.PanBy(horizontalDelta, verticalDelta);
        NotifyViewportChanged();
    }

    public ImagePoint ViewportToImage(ViewportPoint point)
        => _viewportTransform?.ViewportToImage(point)
            ?? throw new InvalidOperationException("The visual editor viewport has not been initialized.");

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
        if (region is not null && !TextRegions.Contains(region))
        {
            throw new ArgumentException("The region does not belong to the selected page.", nameof(region));
        }

        SelectedRegion = region;
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
        => DeleteSelectedRegionCommand.NotifyCanExecuteChanged();

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
        TextRegions.Remove(region);
        SelectedRegion = null;
        StatusMessage = $"Region {region.ReadingOrder} deleted.";
    }

    private void ZoomIn()
    {
        if (_viewportTransform is null)
        {
            return;
        }

        var center = new ViewportPoint(
            _viewportTransform.ViewportWidth / 2,
            _viewportTransform.ViewportHeight / 2);
        _viewportTransform = _viewportTransform.ZoomAt(center, _viewportTransform.Zoom + 0.1);
        NotifyViewportChanged();
    }

    private void ZoomOut()
    {
        if (_viewportTransform is null)
        {
            return;
        }

        var center = new ViewportPoint(
            _viewportTransform.ViewportWidth / 2,
            _viewportTransform.ViewportHeight / 2);
        _viewportTransform = _viewportTransform.ZoomAt(center, _viewportTransform.Zoom - 0.1);
        NotifyViewportChanged();
    }

    private void ResetZoom()
    {
        if (_viewportTransform is null)
        {
            return;
        }

        _viewportTransform = _viewportTransform.Reset();
        NotifyViewportChanged();
    }

    private void FitToView()
    {
        if (_viewportTransform is null)
        {
            return;
        }

        _viewportTransform = ViewportTransform.Fit(
            ImageWidth,
            ImageHeight,
            _viewportTransform.ViewportWidth,
            _viewportTransform.ViewportHeight);
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
}
