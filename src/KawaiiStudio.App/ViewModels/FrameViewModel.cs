using System.Collections.ObjectModel;
using System.Windows.Input;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class FrameViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private string _categoryName = "";

    public FrameViewModel(NavigationService navigation, SessionService session, ThemeCatalogService themeCatalog)
        : base(themeCatalog, "frame")
    {
        _navigation = navigation;
        _session = session;

        SelectFrameCommand = new RelayCommand<FrameItem>(SelectFrame);
        BackCommand = new RelayCommand(() => _navigation.Navigate("category"));
    }

    public ObservableCollection<FrameItem> Frames { get; } = new();

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

    public bool HasFrames => Frames.Count > 0;

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
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
            return;
        }

        foreach (var frame in category.Frames)
        {
            Frames.Add(frame);
        }

        OnPropertyChanged(nameof(HasFrames));
    }

    private void SelectFrame(FrameItem? frame)
    {
        if (frame is null)
        {
            return;
        }

        _session.Current.SetFrame(frame);
        _navigation.Navigate("payment");
    }
}
