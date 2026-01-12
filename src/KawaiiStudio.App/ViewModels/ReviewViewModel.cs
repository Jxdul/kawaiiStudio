using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class ReviewViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly FrameCompositionService _composer;
    private readonly RelayCommand _continueCommand;
    private readonly RelayCommand<ReviewPhotoItem> _selectPhotoCommand;
    private string _statusText = "Select photos to fill the slots";
    private string _slotSummary = string.Empty;
    private ImageSource? _previewImage;
    private int _slotCount;
    private string? _previewError;

    public ReviewViewModel(
        NavigationService navigation,
        SessionService session,
        FrameCompositionService composer,
        ThemeCatalogService themeCatalog)
        : base(themeCatalog, "review")
    {
        _navigation = navigation;
        _session = session;
        _composer = composer;

        _continueCommand = new RelayCommand(() => _navigation.Navigate("finalize"), CanContinue);
        ContinueCommand = _continueCommand;
        _selectPhotoCommand = new RelayCommand<ReviewPhotoItem>(SelectPhoto);
        SelectPhotoCommand = _selectPhotoCommand;
        BackCommand = new RelayCommand(() => _navigation.Navigate("capture"));
    }

    public ObservableCollection<ReviewPhotoItem> Photos { get; } = new();

    public ICommand ContinueCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SelectPhotoCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public string SlotSummary
    {
        get => _slotSummary;
        private set
        {
            _slotSummary = value;
            OnPropertyChanged();
        }
    }

    public ImageSource? PreviewImage
    {
        get => _previewImage;
        private set
        {
            _previewImage = value;
            OnPropertyChanged();
        }
    }

    public bool HasPhotos => Photos.Count > 0;

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        KawaiiStudio.App.App.Log("REVIEW_START");
        LoadPhotos();
        UpdatePreview();
        UpdateStatus();
    }

    private void LoadPhotos()
    {
        Photos.Clear();
        var captured = _session.Current.CapturedPhotos;
        for (var i = 0; i < captured.Count; i++)
        {
            Photos.Add(new ReviewPhotoItem(i, captured[i]));
        }

        _slotCount = _session.Current.SlotCount ?? 0;
        SyncSelection();
        OnPropertyChanged(nameof(HasPhotos));
    }

    private void SyncSelection()
    {
        foreach (var item in Photos)
        {
            var slotIndex = _session.Current.SelectedMapping
                .Where(pair => pair.Value == item.Index)
                .Select(pair => pair.Key)
                .FirstOrDefault();
            item.SetSlotIndex(slotIndex);
        }
    }

    private void SelectPhoto(ReviewPhotoItem item)
    {
        if (item.IsSelected)
        {
            _session.Current.RemoveSelectedMapping(item.SlotIndex);
            item.SetSlotIndex(0);
            KawaiiStudio.App.App.Log($"REVIEW_PHOTO_UNSELECT index={item.Index}");
        }
        else
        {
            if (!TryGetNextSlot(out var nextSlot))
            {
                StatusText = "All slots filled. Tap a selected photo to change.";
                return;
            }

            _session.Current.SetSelectedMapping(nextSlot, item.Index);
            item.SetSlotIndex(nextSlot);
            KawaiiStudio.App.App.Log($"REVIEW_PHOTO_SELECT index={item.Index} slot={nextSlot}");
        }

        UpdatePreview();
        UpdateStatus();
    }

    private bool TryGetNextSlot(out int slotIndex)
    {
        slotIndex = 0;
        if (_slotCount <= 0)
        {
            return false;
        }

        for (var i = 1; i <= _slotCount; i++)
        {
            if (!_session.Current.SelectedMapping.ContainsKey(i))
            {
                slotIndex = i;
                return true;
            }
        }

        return false;
    }

    private void UpdatePreview()
    {
        var preview = _composer.RenderComposite(_session.Current, out var error);
        if (preview is null)
        {
            PreviewImage = null;
            _previewError = error ?? "Preview unavailable.";
            return;
        }

        _previewError = null;
        PreviewImage = preview;
    }

    private void UpdateStatus()
    {
        var selectedCount = _session.Current.SelectedMapping.Count;
        if (_slotCount <= 0)
        {
            StatusText = "Slots not configured.";
            SlotSummary = string.Empty;
            _continueCommand.RaiseCanExecuteChanged();
            return;
        }

        SlotSummary = $"Selected {selectedCount} / {_slotCount}";
        if (!string.IsNullOrWhiteSpace(_previewError))
        {
            StatusText = _previewError;
            _continueCommand.RaiseCanExecuteChanged();
            return;
        }

        if (selectedCount < _slotCount)
        {
            StatusText = "Select photos to fill the slots";
        }
        else
        {
            StatusText = "All slots filled";
        }

        _continueCommand.RaiseCanExecuteChanged();
    }

    private bool CanContinue()
    {
        return _slotCount > 0
            && _session.Current.SelectedMapping.Count >= _slotCount
            && string.IsNullOrWhiteSpace(_previewError);
    }

    public void AutoFillMissingSelections()
    {
        if (_slotCount <= 0 || Photos.Count == 0)
        {
            return;
        }

        var selected = _session.Current.SelectedMapping;
        var missingSlots = Enumerable.Range(1, _slotCount)
            .Where(slot => !selected.ContainsKey(slot))
            .ToList();

        if (missingSlots.Count == 0)
        {
            return;
        }

        var selectedPhotos = selected.Values.ToHashSet();
        var remainingPhotos = Photos
            .Select(photo => photo.Index)
            .Where(index => !selectedPhotos.Contains(index))
            .ToList();

        if (remainingPhotos.Count == 0)
        {
            return;
        }

        KawaiiStudio.App.App.Log($"REVIEW_TIMEOUT_AUTOFILL missing={missingSlots.Count}");
        var rng = Random.Shared;
        foreach (var slotIndex in missingSlots)
        {
            if (remainingPhotos.Count == 0)
            {
                break;
            }

            var pickIndex = rng.Next(remainingPhotos.Count);
            var photoIndex = remainingPhotos[pickIndex];
            remainingPhotos.RemoveAt(pickIndex);
            selectedPhotos.Add(photoIndex);
            _session.Current.SetSelectedMapping(slotIndex, photoIndex);
            KawaiiStudio.App.App.Log($"REVIEW_AUTOFILL slot={slotIndex} photo={photoIndex}");
        }

        SyncSelection();
        UpdatePreview();
        UpdateStatus();
    }
}
