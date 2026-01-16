using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class FrameViewModel : ScreenViewModelBase
{
    private const int FramesPerPage = 3;
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private string _categoryName = "";
    private int _currentPageIndex = 0;

    public FrameViewModel(NavigationService navigation, SessionService session, ThemeCatalogService themeCatalog)
        : base(themeCatalog, "frame")
    {
        _navigation = navigation;
        _session = session;

        SelectFrameCommand = new RelayCommand<FrameItem>(SelectFrame);
        BackCommand = new RelayCommand(() => _navigation.Navigate("category"));
        NextPageCommand = new RelayCommand(NextPage, () => HasFrames);
        PreviousPageCommand = new RelayCommand(PreviousPage, () => HasFrames);
    }

    public ObservableCollection<FrameItem> Frames { get; } = new();
    public ObservableCollection<FrameItem> VisibleFrames { get; } = new();

    public string CategoryName
    {
        get => _categoryName;
        private set
        {
            _categoryName = value;
            OnPropertyChanged();
        }
    }

    public ICommand SelectFrameCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }

    public bool HasFrames => Frames.Count > 0;
    // For infinite carousel, arrows are always enabled when there are frames
    public bool HasNextPage => HasFrames;
    public bool HasPreviousPage => HasFrames;

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        _currentPageIndex = 0;
        LoadFrames();
    }

    private void LoadFrames()
    {
        Frames.Clear();
        var category = _session.Current.Category;
        CategoryName = category?.Name ?? "Frames";

        if (category is null)
        {
            OnPropertyChanged(nameof(HasFrames));
            UpdateVisibleFrames();
            return;
        }

        foreach (var frame in category.Frames)
        {
            Frames.Add(frame);
        }

        OnPropertyChanged(nameof(HasFrames));
        UpdateVisibleFrames();
    }

    private void UpdateVisibleFrames()
    {
        VisibleFrames.Clear();
        
        if (Frames.Count == 0)
        {
            OnPropertyChanged(nameof(HasNextPage));
            OnPropertyChanged(nameof(HasPreviousPage));
            ((RelayCommand)NextPageCommand).RaiseCanExecuteChanged();
            ((RelayCommand)PreviousPageCommand).RaiseCanExecuteChanged();
            return;
        }

        // Always show 3 frames, wrapping around if needed
        var startIndex = _currentPageIndex * FramesPerPage;
        
        for (var i = 0; i < FramesPerPage; i++)
        {
            var index = (startIndex + i) % Frames.Count;
            VisibleFrames.Add(Frames[index]);
        }

        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(HasPreviousPage));
        ((RelayCommand)NextPageCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviousPageCommand).RaiseCanExecuteChanged();
    }

    private void NextPage()
    {
        if (!HasFrames) return;
        
        // Increment page index infinitely - modulo in UpdateVisibleFrames handles visual wrapping
        _currentPageIndex++;
        UpdateVisibleFrames();
    }

    private void PreviousPage()
    {
        if (!HasFrames) return;
        
        _currentPageIndex--;
        
        // Wrap around if we go negative to create infinite scrolling effect
        // Calculate how many unique pages exist, then wrap to a large positive equivalent
        if (_currentPageIndex < 0 && Frames.Count > 0)
        {
            // Every Frames.Count frames (or equivalent pages), the visual state repeats
            // Use a large number to simulate infinite scrolling
            var largeOffset = 1000000;
            _currentPageIndex = largeOffset + _currentPageIndex;
        }
        
        UpdateVisibleFrames();
    }

    private void SelectFrame(FrameItem frame)
    {
        _session.Current.SetFrame(frame);
        _navigation.Navigate("payment");
    }
}
