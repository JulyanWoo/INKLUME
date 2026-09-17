using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inklume.Application.Projects;
using Inklume.Application.TextRegions;
using Inklume.Domain.Projects;
using Inklume.Domain.TextRegions;

namespace Inklume.Desktop.ViewModels;

public sealed partial class RegionReviewViewModel : ObservableObject
{
    private readonly VisualEditorViewModel _editor;
    private readonly TextRegionReviewService _reviewService;
    private readonly TextRegionService _textRegionService;
    private readonly OcrService? _ocrService;

    [ObservableProperty]
    private string _reviewedTextDraft = string.Empty;

    [ObservableProperty]
    private bool _isReviewTextDirty;

    [ObservableProperty]
    private string _rawOcrText = "No OCR result";

    [ObservableProperty]
    private bool _hasOcr;

    [ObservableProperty]
    private string _recognitionConfidenceDisplay = "-";

    [ObservableProperty]
    private string _detectionConfidenceDisplay = "-";

    [ObservableProperty]
    private string _engineDisplay = "-";

    [ObservableProperty]
    private string _modelDisplay = "-";

    [ObservableProperty]
    private string _profileDisplay = "-";

    public RegionReviewViewModel(
        VisualEditorViewModel editor,
        TextRegionReviewService reviewService,
        TextRegionService textRegionService,
        OcrService? ocrService = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _reviewService = reviewService ?? throw new ArgumentNullException(nameof(reviewService));
        _textRegionService = textRegionService ?? throw new ArgumentNullException(nameof(textRegionService));
        _ocrService = ocrService;

        SaveReviewedTextCommand = new AsyncRelayCommand(SaveReviewedTextAsync, () => HasSelectedRegion);
        ResetReviewedTextCommand = new AsyncRelayCommand(ResetReviewedTextAsync, () => HasSelectedRegion && CanResetReviewedText);
        MarkReviewedCommand = new AsyncRelayCommand(MarkReviewedAsync, () => HasSelectedRegion && !IsReviewed);
        MarkPendingCommand = new AsyncRelayCommand(MarkPendingAsync, () => HasSelectedRegion && IsReviewed);
        ToggleReviewStatusCommand = new AsyncRelayCommand(ToggleReviewStatusAsync, () => HasSelectedRegion);
        MoveUpCommand = new AsyncRelayCommand(MoveUpAsync, () => CanMoveUp);
        MoveDownCommand = new AsyncRelayCommand(MoveDownAsync, () => CanMoveDown);
        SelectPreviousRegionCommand = new AsyncRelayCommand(SelectPreviousRegionAsync, () => CanSelectPrevious);
        SelectNextRegionCommand = new AsyncRelayCommand(SelectNextRegionAsync, () => CanSelectNext);
        RecognizeRegionCommand = new AsyncRelayCommand(RecognizeRegionAsync, () => CanRecognizeRegion);
    }

    public IAsyncRelayCommand SelectPreviousCommand => SelectPreviousRegionCommand;

    public IAsyncRelayCommand SelectNextCommand => SelectNextRegionCommand;

    public TextRegion? SelectedRegion => _editor.SelectedRegion;

    public bool HasSelectedRegion => SelectedRegion is not null;

    public IReadOnlyList<TextRegionRole> Roles { get; } = Enum.GetValues<TextRegionRole>();

    public IReadOnlyList<TextContainerType> ContainerTypes { get; } = Enum.GetValues<TextContainerType>();

    public TextRegionReviewStatus ReviewStatus => SelectedRegion?.ReviewStatus ?? TextRegionReviewStatus.Pending;

    public bool IsReviewed => ReviewStatus == TextRegionReviewStatus.Reviewed;

    public bool IsPending => ReviewStatus == TextRegionReviewStatus.Pending;

    public string ReviewStatusDisplay => IsReviewed ? "Reviewed" : "Pending";

    public bool CanResetReviewedText => (SelectedRegion?.ReviewedText is not null) || (HasSelectedRegion && IsReviewTextDirty);

    public string OriginDisplay => SelectedRegion is null
        ? "-"
        : SelectedRegion.Origin == TextRegionOrigin.OcrDetection ? "OCR Detection" : "Manual";

    public int ReadingOrder => SelectedRegion?.ReadingOrder ?? 0;

    public string BoundsDisplay
    {
        get
        {
            ImageBounds? bounds = SelectedRegion?.Geometry.Bounds;
            return bounds is null
                ? string.Empty
                : $"{bounds.Value.X:0.##}, {bounds.Value.Y:0.##}  {bounds.Value.Width:0.##} x {bounds.Value.Height:0.##}";
        }
    }

    public string EffectiveSourceTextDisplay
    {
        get
        {
            if (SelectedRegion is null)
            {
                return string.Empty;
            }

            string? rawOcr = HasOcr ? RawOcrText : null;
            return RegionSourceTextDto.ResolveEffectiveSourceText(SelectedRegion.ReviewedText, rawOcr) ?? "(None)";
        }
    }

    public string PageReviewProgressDisplay
    {
        get
        {
            int total = _editor.TextRegions.Count;
            if (total == 0)
            {
                return "No regions";
            }

            int reviewed = _editor.TextRegions.Count(r => r.ReviewStatus == TextRegionReviewStatus.Reviewed);
            return $"Reviewed {reviewed} / {total}";
        }
    }

    public bool CanMoveUp
    {
        get
        {
            if (SelectedRegion is null || _editor.TextRegions.Count <= 1)
            {
                return false;
            }

            int index = GetSortedIndex(SelectedRegion);
            return index > 0;
        }
    }

    public bool CanMoveDown
    {
        get
        {
            if (SelectedRegion is null || _editor.TextRegions.Count <= 1)
            {
                return false;
            }

            int index = GetSortedIndex(SelectedRegion);
            return index >= 0 && index < _editor.TextRegions.Count - 1;
        }
    }

    public bool CanSelectPrevious => CanMoveUp;

    public bool CanSelectNext => CanMoveDown;

    public bool CanRecognizeRegion
        => _editor.HasPage && SelectedRegion is not null && !_editor.IsOcrRunning && _ocrService is not null;

    public IAsyncRelayCommand SaveReviewedTextCommand { get; }

    public IAsyncRelayCommand ResetReviewedTextCommand { get; }

    public IAsyncRelayCommand MarkReviewedCommand { get; }

    public IAsyncRelayCommand MarkPendingCommand { get; }

    public IAsyncRelayCommand ToggleReviewStatusCommand { get; }

    public IAsyncRelayCommand MoveUpCommand { get; }

    public IAsyncRelayCommand MoveDownCommand { get; }

    public IAsyncRelayCommand SelectPreviousRegionCommand { get; }

    public IAsyncRelayCommand SelectNextRegionCommand { get; }

    public IAsyncRelayCommand RecognizeRegionCommand { get; }

    partial void OnReviewedTextDraftChanged(string value)
    {
        string persisted = SelectedRegion?.ReviewedText ?? string.Empty;
        IsReviewTextDirty = SelectedRegion is not null && value != persisted;
        OnPropertyChanged(nameof(CanResetReviewedText));
        SaveReviewedTextCommand.NotifyCanExecuteChanged();
        ResetReviewedTextCommand.NotifyCanExecuteChanged();
    }

    public void OnSelectedRegionChanged(TextRegion? region)
    {
        ReviewedTextDraft = region?.ReviewedText ?? string.Empty;
        IsReviewTextDirty = false;

        UpdateOcrDisplay(region);
        NotifyReviewStateChanged();
    }

    public void OnRegionsChanged()
    {
        NotifyReviewStateChanged();
    }

    public void NotifyOcrChanged()
    {
        UpdateOcrDisplay(SelectedRegion);
        NotifyReviewStateChanged();
    }

    public async Task<bool> CommitDraftIfDirtyAsync(CancellationToken cancellationToken = default)
    {
        if (!IsReviewTextDirty || SelectedRegion is null || _editor.SelectedPage is null)
        {
            return true;
        }

        try
        {
            await SaveReviewedTextAsync();
            return true;
        }
        catch (Exception exception)
        {
            _editor.ReportInteractionError(exception);
            return false;
        }
    }

    public async Task SaveReviewedTextAsync()
    {
        if (SelectedRegion is null || _editor.SelectedPage is null)
        {
            return;
        }

        try
        {
            TextRegion region = SelectedRegion;
            TextRegion updated = await _reviewService.SaveReviewedTextAsync(
                _editor.Workspace,
                _editor.SelectedPage.Page.Id,
                region.Id,
                ReviewedTextDraft,
                CancellationToken.None);

            ReplaceRegionInCollection(region, updated);
            _editor.SelectedRegion = updated;
            IsReviewTextDirty = false;
            _editor.StatusMessage = $"Reviewed text saved for region {updated.ReadingOrder}.";
            NotifyReviewStateChanged();
        }
        catch (ProjectOperationException exception)
        {
            _editor.ErrorMessage = exception.Message;
            _editor.StatusMessage = "Failed to save reviewed text.";
        }
        catch (Exception)
        {
            _editor.ErrorMessage = "An unexpected error occurred while saving reviewed text.";
            _editor.StatusMessage = "Failed to save reviewed text.";
        }
    }

    public async Task ResetReviewedTextAsync()
    {
        if (SelectedRegion is null || _editor.SelectedPage is null)
        {
            return;
        }

        if (SelectedRegion.ReviewedText is null)
        {
            ReviewedTextDraft = string.Empty;
            IsReviewTextDirty = false;
            NotifyReviewStateChanged();
            return;
        }

        try
        {
            TextRegion region = SelectedRegion;
            TextRegion updated = await _reviewService.ResetReviewedTextAsync(
                _editor.Workspace,
                _editor.SelectedPage.Page.Id,
                region.Id,
                CancellationToken.None);

            ReplaceRegionInCollection(region, updated);
            _editor.SelectedRegion = updated;
            ReviewedTextDraft = string.Empty;
            IsReviewTextDirty = false;
            _editor.StatusMessage = $"Reset reviewed text for region {updated.ReadingOrder}.";
            NotifyReviewStateChanged();
        }
        catch (ProjectOperationException exception)
        {
            _editor.ErrorMessage = exception.Message;
            _editor.StatusMessage = "Failed to reset reviewed text.";
        }
        catch (Exception)
        {
            _editor.ErrorMessage = "An unexpected error occurred while resetting reviewed text.";
            _editor.StatusMessage = "Failed to reset reviewed text.";
        }
    }

    public async Task MarkReviewedAsync()
    {
        if (SelectedRegion is null || _editor.SelectedPage is null)
        {
            return;
        }

        // Persist draft before marking reviewed
        if (IsReviewTextDirty)
        {
            bool committed = await CommitDraftIfDirtyAsync();
            if (!committed)
            {
                return;
            }
        }

        try
        {
            TextRegion region = SelectedRegion;
            TextRegion updated = await _reviewService.SetReviewStatusAsync(
                _editor.Workspace,
                _editor.SelectedPage.Page.Id,
                region.Id,
                TextRegionReviewStatus.Reviewed,
                CancellationToken.None);

            ReplaceRegionInCollection(region, updated);
            _editor.SelectedRegion = updated;
            _editor.StatusMessage = $"Region {updated.ReadingOrder} marked as Reviewed.";
            NotifyReviewStateChanged();
        }
        catch (ProjectOperationException exception)
        {
            _editor.ErrorMessage = exception.Message;
            _editor.StatusMessage = "Failed to mark region as Reviewed.";
        }
        catch (Exception)
        {
            _editor.ErrorMessage = "An unexpected error occurred while updating review status.";
            _editor.StatusMessage = "Failed to update review status.";
        }
    }

    public async Task MarkPendingAsync()
    {
        if (SelectedRegion is null || _editor.SelectedPage is null)
        {
            return;
        }

        try
        {
            TextRegion region = SelectedRegion;
            TextRegion updated = await _reviewService.SetReviewStatusAsync(
                _editor.Workspace,
                _editor.SelectedPage.Page.Id,
                region.Id,
                TextRegionReviewStatus.Pending,
                CancellationToken.None);

            ReplaceRegionInCollection(region, updated);
            _editor.SelectedRegion = updated;
            _editor.StatusMessage = $"Region {updated.ReadingOrder} marked as Pending.";
            NotifyReviewStateChanged();
        }
        catch (ProjectOperationException exception)
        {
            _editor.ErrorMessage = exception.Message;
            _editor.StatusMessage = "Failed to mark region as Pending.";
        }
        catch (Exception)
        {
            _editor.ErrorMessage = "An unexpected error occurred while updating review status.";
            _editor.StatusMessage = "Failed to update review status.";
        }
    }

    public Task ToggleReviewStatusAsync()
        => IsReviewed ? MarkPendingAsync() : MarkReviewedAsync();

    public async Task UpdateClassificationAsync(TextRegionRole role, TextContainerType containerType)
    {
        if (SelectedRegion is null || _editor.SelectedPage is null)
        {
            return;
        }

        try
        {
            TextRegion region = SelectedRegion;
            TextRegion updated = await _reviewService.UpdateClassificationAsync(
                _editor.Workspace,
                _editor.SelectedPage.Page.Id,
                region.Id,
                role,
                containerType,
                CancellationToken.None);

            ReplaceRegionInCollection(region, updated);
            _editor.SelectedRegion = updated;
            NotifyReviewStateChanged();
        }
        catch (ProjectOperationException exception)
        {
            _editor.ErrorMessage = exception.Message;
        }
        catch (Exception)
        {
            _editor.ErrorMessage = "An unexpected error occurred while updating classification.";
        }
    }

    public async Task MoveUpAsync()
    {
        if (SelectedRegion is null || _editor.SelectedPage is null || !CanMoveUp)
        {
            return;
        }

        if (IsReviewTextDirty)
        {
            bool committed = await CommitDraftIfDirtyAsync();
            if (!committed)
            {
                return;
            }
        }

        try
        {
            Guid currentRegionId = SelectedRegion.Id;
            IReadOnlyList<TextRegion> reordered = await _reviewService.MoveRegionUpAsync(
                _editor.Workspace,
                _editor.SelectedPage.Page.Id,
                currentRegionId,
                CancellationToken.None);

            UpdateEditorRegionsCollection(reordered, currentRegionId);
            _editor.StatusMessage = "Reading order updated.";
        }
        catch (ProjectOperationException exception)
        {
            _editor.ErrorMessage = exception.Message;
            _editor.StatusMessage = "Failed to reorder regions.";
        }
        catch (Exception)
        {
            _editor.ErrorMessage = "An unexpected error occurred while reordering regions.";
            _editor.StatusMessage = "Failed to reorder regions.";
        }
    }

    public async Task MoveDownAsync()
    {
        if (SelectedRegion is null || _editor.SelectedPage is null || !CanMoveDown)
        {
            return;
        }

        if (IsReviewTextDirty)
        {
            bool committed = await CommitDraftIfDirtyAsync();
            if (!committed)
            {
                return;
            }
        }

        try
        {
            Guid currentRegionId = SelectedRegion.Id;
            IReadOnlyList<TextRegion> reordered = await _reviewService.MoveRegionDownAsync(
                _editor.Workspace,
                _editor.SelectedPage.Page.Id,
                currentRegionId,
                CancellationToken.None);

            UpdateEditorRegionsCollection(reordered, currentRegionId);
            _editor.StatusMessage = "Reading order updated.";
        }
        catch (ProjectOperationException exception)
        {
            _editor.ErrorMessage = exception.Message;
            _editor.StatusMessage = "Failed to reorder regions.";
        }
        catch (Exception)
        {
            _editor.ErrorMessage = "An unexpected error occurred while reordering regions.";
            _editor.StatusMessage = "Failed to reorder regions.";
        }
    }

    public async Task SelectPreviousRegionAsync()
    {
        if (SelectedRegion is null || !CanSelectPrevious)
        {
            return;
        }

        if (IsReviewTextDirty)
        {
            bool committed = await CommitDraftIfDirtyAsync();
            if (!committed)
            {
                return;
            }
        }

        var sorted = _editor.TextRegions.OrderBy(r => r.ReadingOrder).ToList();
        int index = sorted.FindIndex(r => r.Id == SelectedRegion.Id);
        if (index > 0)
        {
            _editor.SelectRegion(sorted[index - 1]);
        }
    }

    public async Task SelectNextRegionAsync()
    {
        if (SelectedRegion is null || !CanSelectNext)
        {
            return;
        }

        if (IsReviewTextDirty)
        {
            bool committed = await CommitDraftIfDirtyAsync();
            if (!committed)
            {
                return;
            }
        }

        var sorted = _editor.TextRegions.OrderBy(r => r.ReadingOrder).ToList();
        int index = sorted.FindIndex(r => r.Id == SelectedRegion.Id);
        if (index >= 0 && index < sorted.Count - 1)
        {
            _editor.SelectRegion(sorted[index + 1]);
        }
    }

    public async Task RecognizeRegionAsync()
    {
        if (SelectedRegion is null || _editor.SelectedPage is null || _ocrService is null)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _editor.IsOcrRunning = true;
        _editor.OcrProgressMessage = "Recognizing region...";
        _editor.ErrorMessage = string.Empty;
        NotifyCanExecute();

        try
        {
            TextRegion region = SelectedRegion;
            OcrRecognition recognition = await _ocrService.RecognizeRegionAsync(
                _editor.Workspace,
                _editor.SelectedPage.Page.Id,
                region.Id,
                cancellation.Token);

            _editor.SetRegionRecognition(region.Id, recognition);
            UpdateOcrDisplay(region);
            _editor.StatusMessage = $"Region {region.ReadingOrder} recognized.";
            NotifyReviewStateChanged();
        }
        catch (OperationCanceledException)
        {
            _editor.StatusMessage = "Region OCR cancelled.";
        }
        catch (ProjectOperationException exception)
        {
            _editor.ErrorMessage = exception.Message;
            _editor.StatusMessage = "Region OCR failed.";
        }
        catch (Exception)
        {
            _editor.ErrorMessage = "An unexpected error occurred during region OCR.";
            _editor.StatusMessage = "Region OCR failed.";
        }
        finally
        {
            _editor.IsOcrRunning = false;
            _editor.OcrProgressMessage = string.Empty;
            NotifyCanExecute();
        }
    }

    private void UpdateOcrDisplay(TextRegion? region)
    {
        if (region is null)
        {
            HasOcr = false;
            RawOcrText = "No OCR result";
            RecognitionConfidenceDisplay = "-";
            DetectionConfidenceDisplay = "-";
            EngineDisplay = "-";
            ModelDisplay = "-";
            ProfileDisplay = "-";
            return;
        }

        if (_editor.TryGetRecognition(region.Id, out OcrRecognition? recognition) && recognition is not null)
        {
            HasOcr = true;
            RawOcrText = recognition.Text;
            RecognitionConfidenceDisplay = $"{recognition.RecognitionConfidence:P1} ({recognition.RecognitionConfidence:0.###})";
            DetectionConfidenceDisplay = recognition.DetectionConfidence.HasValue
                ? $"{recognition.DetectionConfidence.Value:P1} ({recognition.DetectionConfidence.Value:0.###})"
                : "N/A";
            EngineDisplay = $"{recognition.EngineName} {recognition.EngineVersion}".Trim();
            ModelDisplay = recognition.RecognitionModel;
            ProfileDisplay = recognition.ModelProfile;
        }
        else
        {
            HasOcr = false;
            RawOcrText = "No OCR result";
            RecognitionConfidenceDisplay = "-";
            DetectionConfidenceDisplay = "-";
            EngineDisplay = "-";
            ModelDisplay = "-";
            ProfileDisplay = "-";
        }
    }

    private void ReplaceRegionInCollection(TextRegion original, TextRegion updated)
    {
        int index = _editor.TextRegions.IndexOf(original);
        if (index >= 0)
        {
            _editor.TextRegions[index] = updated;
        }
    }

    private void UpdateEditorRegionsCollection(IReadOnlyList<TextRegion> reordered, Guid currentRegionId)
    {
        _editor.TextRegions.Clear();
        foreach (TextRegion region in reordered)
        {
            _editor.TextRegions.Add(region);
        }

        TextRegion? current = reordered.FirstOrDefault(r => r.Id == currentRegionId);
        _editor.SelectedRegion = current;
    }

    private int GetSortedIndex(TextRegion region)
    {
        var sorted = _editor.TextRegions.OrderBy(r => r.ReadingOrder).ToList();
        return sorted.FindIndex(r => r.Id == region.Id);
    }

    private void NotifyReviewStateChanged()
    {
        OnPropertyChanged(nameof(SelectedRegion));
        OnPropertyChanged(nameof(HasSelectedRegion));
        OnPropertyChanged(nameof(ReviewStatus));
        OnPropertyChanged(nameof(IsReviewed));
        OnPropertyChanged(nameof(ReviewStatusDisplay));
        OnPropertyChanged(nameof(CanResetReviewedText));
        OnPropertyChanged(nameof(OriginDisplay));
        OnPropertyChanged(nameof(ReadingOrder));
        OnPropertyChanged(nameof(BoundsDisplay));
        OnPropertyChanged(nameof(EffectiveSourceTextDisplay));
        OnPropertyChanged(nameof(PageReviewProgressDisplay));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(CanSelectPrevious));
        OnPropertyChanged(nameof(CanSelectNext));
        OnPropertyChanged(nameof(CanRecognizeRegion));

        NotifyCanExecute();
    }

    private void NotifyCanExecute()
    {
        SaveReviewedTextCommand.NotifyCanExecuteChanged();
        ResetReviewedTextCommand.NotifyCanExecuteChanged();
        MarkReviewedCommand.NotifyCanExecuteChanged();
        MarkPendingCommand.NotifyCanExecuteChanged();
        ToggleReviewStatusCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        SelectPreviousRegionCommand.NotifyCanExecuteChanged();
        SelectNextRegionCommand.NotifyCanExecuteChanged();
        RecognizeRegionCommand.NotifyCanExecuteChanged();
    }
}
