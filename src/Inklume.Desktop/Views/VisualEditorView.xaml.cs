using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Inklume.Application.Projects;
using Inklume.Desktop.ViewModels;
using Inklume.Desktop.VisualEditor;
using Inklume.Domain.TextRegions;

namespace Inklume.Desktop.Views;

public partial class VisualEditorView : UserControl
{
    private readonly List<ImagePoint> _polygonPoints = [];
    private VisualEditorViewModel? _viewModel;
    private ImagePoint? _rectangleStart;
    private ImagePoint? _transientPointer;
    private Point _lastPanPoint;
    private bool _isPanning;
    private bool _isSpacePressed;
    private bool _overlayRefreshPending;
    private bool _editorUsedAltModifier;

    public VisualEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as VisualEditorViewModel);
        ConfigureViewport(fitIfUninitialized: true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
        => AttachViewModel(null);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachViewModel(e.NewValue as VisualEditorViewModel);
        ConfigureViewport(fitIfUninitialized: true);
    }

    private void AttachViewModel(VisualEditorViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.TextRegions.CollectionChanged -= OnRegionsChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.TextRegions.CollectionChanged += OnRegionsChanged;
        }

        CancelTransientVisuals();
        RefreshEditorVisuals();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VisualEditorViewModel.SelectedPage)
            or nameof(VisualEditorViewModel.Preview)
            or nameof(VisualEditorViewModel.IsGenericPreview))
        {
            CancelTransientVisuals();
            ConfigureViewport(fitIfUninitialized: true);
        }

        if (e.PropertyName is nameof(VisualEditorViewModel.ZoomScale)
            or nameof(VisualEditorViewModel.ViewportOffsetX)
            or nameof(VisualEditorViewModel.ViewportOffsetY))
        {
            ApplyViewportTransform();
        }

        if (e.PropertyName is nameof(VisualEditorViewModel.SelectedRegion)
            or nameof(VisualEditorViewModel.SelectedTool))
        {
            if (e.PropertyName == nameof(VisualEditorViewModel.SelectedTool))
            {
                CancelTransientVisuals();
            }

            RedrawOverlays();
        }
    }

    private void OnRegionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => QueueOverlayRefresh();

    private void QueueOverlayRefresh()
    {
        if (_overlayRefreshPending)
        {
            return;
        }

        _overlayRefreshPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _overlayRefreshPending = false;
            RedrawOverlays();
        });
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
        => ConfigureViewport(fitIfUninitialized: false);

    private void ConfigureViewport(bool fitIfUninitialized)
    {
        if (_viewModel is null || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0)
        {
            return;
        }

        _viewModel.ConfigureViewport(Viewport.ActualWidth, Viewport.ActualHeight, fitIfUninitialized);
        RefreshEditorVisuals();
    }

    private void RefreshEditorVisuals()
    {
        ApplyViewportTransform();
        RedrawOverlays();
    }

    private void ApplyViewportTransform()
    {
        if (_viewModel is null)
        {
            WorldTransform.Matrix = Matrix.Identity;
            return;
        }

        WorldTransform.Matrix = new Matrix(
            _viewModel.ZoomScale,
            0,
            0,
            _viewModel.ZoomScale,
            _viewModel.ViewportOffsetX,
            _viewModel.ViewportOffsetY);
        UpdateBitmapScalingMode(_viewModel.ZoomScale);
        RedrawOverlays();
    }

    private void UpdateBitmapScalingMode(double zoomScale)
    {
        if (PageImage is null)
        {
            return;
        }

        BitmapScalingMode targetMode = zoomScale > 2.0
            ? BitmapScalingMode.NearestNeighbor
            : BitmapScalingMode.Linear;

        if (RenderOptions.GetBitmapScalingMode(PageImage) != targetMode)
        {
            RenderOptions.SetBitmapScalingMode(PageImage, targetMode);
        }
    }

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null || (!_viewModel.HasPage && !_viewModel.IsGenericPreview))
        {
            return;
        }

        Viewport.Focus();
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            _editorUsedAltModifier = true;
        }
        Point viewportPoint = e.GetPosition(Viewport);
        if (e.ChangedButton == MouseButton.Middle
            || (e.ChangedButton == MouseButton.Left && _isSpacePressed))
        {
            _isPanning = true;
            _lastPanPoint = viewportPoint;
            Viewport.Cursor = Cursors.SizeAll;
            Mouse.Capture(Viewport);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left || !_viewModel.IsEditingEnabled)
        {
            return;
        }

        ImagePoint imagePoint = _viewModel.ViewportToImage(new ViewportPoint(viewportPoint.X, viewportPoint.Y));
        switch (_viewModel.SelectedTool)
        {
            case VisualEditorTool.Select:
                _viewModel.SelectRegion(FindRegion(e.OriginalSource as DependencyObject));
                break;
            case VisualEditorTool.RectangleRegion when IsInsideImage(imagePoint):
                _rectangleStart = imagePoint;
                _transientPointer = imagePoint;
                Mouse.Capture(Viewport);
                RedrawOverlays();
                break;
            case VisualEditorTool.PolygonRegion when IsInsideImage(imagePoint):
                if (e.ClickCount >= 2)
                {
                    _ = FinishPolygonAsync();
                }
                else
                {
                    _polygonPoints.Add(imagePoint);
                    _transientPointer = imagePoint;
                    RedrawOverlays();
                }

                break;
        }

        e.Handled = true;
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        Point viewportPoint = e.GetPosition(Viewport);
        if (_isPanning)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            {
                _editorUsedAltModifier = true;
            }
            _viewModel.PanBy(viewportPoint.X - _lastPanPoint.X, viewportPoint.Y - _lastPanPoint.Y);
            _lastPanPoint = viewportPoint;
            e.Handled = true;
            return;
        }

        if (_rectangleStart is not null || _polygonPoints.Count > 0)
        {
            _transientPointer = ClampToImage(
                _viewModel.ViewportToImage(new ViewportPoint(viewportPoint.X, viewportPoint.Y)));
            RedrawOverlays();
        }
    }

    private async void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (_isPanning && e.ChangedButton is MouseButton.Middle or MouseButton.Left)
        {
            _isPanning = false;
            Viewport.Cursor = null;
            Mouse.Capture(null);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left || _rectangleStart is not { } start)
        {
            return;
        }

        Point viewportPoint = e.GetPosition(Viewport);
        ImagePoint end = ClampToImage(
            _viewModel.ViewportToImage(new ViewportPoint(viewportPoint.X, viewportPoint.Y)));
        _rectangleStart = null;
        _transientPointer = null;
        Mouse.Capture(null);
        try
        {
            await _viewModel.CreateRectangleAsync(start, end);
        }
        catch (Exception exception) when (exception is ArgumentException or ProjectOperationException)
        {
            _viewModel.ReportInteractionError(exception);
        }

        RedrawOverlays();
        e.Handled = true;
    }

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_viewModel is null || (!_viewModel.HasPage && !_viewModel.IsGenericPreview))
        {
            return;
        }

        if (!Viewport.IsKeyboardFocused)
        {
            Viewport.Focus();
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        bool hasShift = (modifiers & ModifierKeys.Shift) != 0;
        bool hasControl = (modifiers & ModifierKeys.Control) != 0;
        bool hasAlt = (modifiers & ModifierKeys.Alt) != 0;

        if (hasAlt)
        {
            _editorUsedAltModifier = true;
        }

        if (hasShift)
        {
            // Shift + MouseWheel and Ctrl + Shift + MouseWheel: horizontal pan
            // Shift takes precedence over zoom so Ctrl+Shift+Wheel is an unambiguous horizontal pan
            _viewModel.PanBy(e.Delta / 3d, 0);
        }
        else if (hasControl)
        {
            // Ctrl + MouseWheel: zoom toward cursor
            Point cursor = e.GetPosition(Viewport);
            _viewModel.ZoomAt(new ViewportPoint(cursor.X, cursor.Y), e.Delta);
        }
        else
        {
            // Plain MouseWheel: vertical pan / scroll
            _viewModel.PanBy(0, e.Delta / 3d);
        }

        e.Handled = true;
    }

    private async void OnViewportKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null || e.OriginalSource is TextBox)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            _isSpacePressed = true;
            if (!_isPanning)
            {
                Viewport.Cursor = Cursors.Hand;
            }

            e.Handled = true;
        }
        else if (e.Key == Key.System || e.SystemKey is Key.LeftAlt or Key.RightAlt)
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
            {
                _editorUsedAltModifier = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            CancelTransientVisuals();
            _viewModel.CancelTransientOperation();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _polygonPoints.Count > 0)
        {
            await FinishPolygonAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _viewModel.DeleteSelectedRegionCommand.CanExecute(null))
        {
            try
            {
                await _viewModel.DeleteSelectedRegionCommand.ExecuteAsync(null);
            }
            catch (Exception exception) when (exception is ProjectOperationException or InvalidOperationException)
            {
                _viewModel.ReportInteractionError(exception);
            }

            e.Handled = true;
        }
    }

    private void OnViewportKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.System || e.SystemKey is Key.LeftAlt or Key.RightAlt)
        {
            if (_editorUsedAltModifier)
            {
                _editorUsedAltModifier = false;
                e.Handled = true;
                return;
            }
        }

        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift)
        {
            if (!Viewport.IsKeyboardFocused)
            {
                Viewport.Focus();
            }

            return;
        }

        if (e.Key != Key.Space)
        {
            return;
        }

        _isSpacePressed = false;
        if (!_isPanning)
        {
            Viewport.Cursor = null;
        }

        e.Handled = true;
    }

    private async Task FinishPolygonAsync()
    {
        if (_viewModel is null)
        {
            return;
        }

        ImagePoint[] points = [.. _polygonPoints];
        CancelTransientVisuals();
        try
        {
            await _viewModel.CreatePolygonAsync(points);
        }
        catch (Exception exception) when (exception is ArgumentException or ProjectOperationException)
        {
            _viewModel.ReportInteractionError(exception);
        }

        RedrawOverlays();
    }

    private void RedrawOverlays()
    {
        if (OverlayCanvas is null)
        {
            return;
        }

        OverlayCanvas.Children.Clear();
        if (_viewModel is null)
        {
            return;
        }

        double zoom = Math.Max(_viewModel.ZoomScale, 0.01);
        foreach (TextRegion region in _viewModel.TextRegions)
        {
            bool isSelected = ReferenceEquals(region, _viewModel.SelectedRegion);
            Shape shape = CreateRegionShape(region, isSelected, zoom);
            OverlayCanvas.Children.Add(shape);
            AddReadingOrderLabel(region, isSelected, zoom);
        }

        DrawTransientGeometry(zoom);
    }

    private Shape CreateRegionShape(TextRegion region, bool isSelected, double zoom)
    {
        Shape shape;
        if (region.Geometry is RectangleTextRegionGeometry rectangle)
        {
            var rectangleShape = new Rectangle
            {
                Width = rectangle.Width,
                Height = rectangle.Height
            };
            Canvas.SetLeft(rectangleShape, rectangle.X);
            Canvas.SetTop(rectangleShape, rectangle.Y);
            shape = rectangleShape;
        }
        else
        {
            shape = new Polygon
            {
                Points = new PointCollection(region.Geometry.Points.Select(point => new Point(point.X, point.Y)))
            };
        }

        shape.Tag = region;
        shape.StrokeThickness = (isSelected ? 2 : 1.25) / zoom;
        shape.SetResourceReference(
            Shape.StrokeProperty,
            isSelected ? "OverlaySelectedStrokeBrush" : "OverlayStrokeBrush");
        shape.SetResourceReference(
            Shape.FillProperty,
            isSelected ? "OverlaySelectedFillBrush" : "OverlayFillBrush");
        return shape;
    }

    private void AddReadingOrderLabel(TextRegion region, bool isSelected, double zoom)
    {
        var label = new Border
        {
            Tag = region,
            CornerRadius = new CornerRadius(2 / zoom),
            Padding = new Thickness(4 / zoom, 1 / zoom, 4 / zoom, 1 / zoom),
            Child = new TextBlock
            {
                Text = region.ReadingOrder.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 10 / zoom,
                FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal
            }
        };
        label.SetResourceReference(BackgroundProperty, "OverlayLabelBackgroundBrush");
        ((TextBlock)label.Child).SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        Canvas.SetLeft(label, region.Geometry.Bounds.X);
        Canvas.SetTop(label, region.Geometry.Bounds.Y);
        OverlayCanvas.Children.Add(label);
    }

    private void DrawTransientGeometry(double zoom)
    {
        if (_rectangleStart is { } start && _transientPointer is { } end)
        {
            var preview = new Rectangle
            {
                Width = Math.Abs(end.X - start.X),
                Height = Math.Abs(end.Y - start.Y),
                StrokeThickness = 1.5 / zoom,
                StrokeDashArray = new DoubleCollection { 4 / zoom, 3 / zoom }
            };
            preview.SetResourceReference(Shape.StrokeProperty, "OverlaySelectedStrokeBrush");
            preview.SetResourceReference(Shape.FillProperty, "OverlaySelectedFillBrush");
            Canvas.SetLeft(preview, Math.Min(start.X, end.X));
            Canvas.SetTop(preview, Math.Min(start.Y, end.Y));
            OverlayCanvas.Children.Add(preview);
        }

        if (_polygonPoints.Count > 0)
        {
            IEnumerable<ImagePoint> points = _transientPointer is { } pointer
                ? _polygonPoints.Append(pointer)
                : _polygonPoints;
            var preview = new Polyline
            {
                Points = new PointCollection(points.Select(point => new Point(point.X, point.Y))),
                StrokeThickness = 1.5 / zoom,
                StrokeDashArray = new DoubleCollection { 4 / zoom, 3 / zoom }
            };
            preview.SetResourceReference(Shape.StrokeProperty, "OverlaySelectedStrokeBrush");
            OverlayCanvas.Children.Add(preview);
        }
    }

    private TextRegion? FindRegion(DependencyObject? element)
    {
        while (element is not null && !ReferenceEquals(element, Viewport))
        {
            if (element is FrameworkElement { Tag: TextRegion region })
            {
                return region;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private bool IsInsideImage(ImagePoint point)
        => _viewModel is not null
            && point.X >= 0 && point.Y >= 0
            && point.X <= _viewModel.ImageWidth
            && point.Y <= _viewModel.ImageHeight;

    private ImagePoint ClampToImage(ImagePoint point)
        => _viewModel is null
            ? point
            : new ImagePoint(
                Math.Clamp(point.X, 0, _viewModel.ImageWidth),
                Math.Clamp(point.Y, 0, _viewModel.ImageHeight));

    private void CancelTransientVisuals()
    {
        _rectangleStart = null;
        _transientPointer = null;
        _polygonPoints.Clear();
        _isPanning = false;
        Mouse.Capture(null);
        if (Viewport is not null)
        {
            Viewport.Cursor = _isSpacePressed ? Cursors.Hand : null;
        }

        RedrawOverlays();
    }
}
