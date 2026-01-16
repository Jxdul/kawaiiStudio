using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class LayoutViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly string _framesRoot;
    private readonly DispatcherTimer _frameRotationTimer;
    private readonly Random _random = new();
    private readonly Dictionary<LayoutOption, List<string>> _layoutFrames = new();
    private readonly Dictionary<LayoutOption, int> _layoutIndices = new();
    private readonly Dictionary<LayoutOption, string> _currentFrames = new();

    public LayoutViewModel(NavigationService navigation, SessionService session, ThemeCatalogService themeCatalog, AppPaths appPaths)
        : base(themeCatalog, "layout")
    {
        _navigation = navigation;
        _session = session;
        _framesRoot = appPaths.FramesRoot;

        Options = new List<LayoutOption>
        {
            new(LayoutStyle.TwoSlots, "Style A (2 shots)", 2, "4x6_2slots"),
            new(LayoutStyle.FourSlots, "Style B (4 shots)", 4, "4x6_4slots"),
            new(LayoutStyle.SixSlots, "Style C (6 shots)", 6, "4x6_6slots")
        };

        SelectLayoutCommand = new RelayCommand<LayoutOption>(SelectLayout);
        BackCommand = new RelayCommand(() => _navigation.Navigate("quantity"));

        LoadSampleFrames();

        _frameRotationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.0)
        };
        _frameRotationTimer.Tick += OnFrameRotationTimerTick;
    }

    public IReadOnlyList<LayoutOption> Options { get; }
    public ObservableCollection<LayoutOption> VisibleOptions { get; } = new();

    public ICommand SelectLayoutCommand { get; }
    public ICommand BackCommand { get; }

    public string? GetCurrentFrame(LayoutOption option)
    {
        return _currentFrames.TryGetValue(option, out var frame) ? frame : null;
    }

    public string? TwoSlotsFrame => GetCurrentFrame(Options.FirstOrDefault(o => o.Slots == 2));
    public string? FourSlotsFrame => GetCurrentFrame(Options.FirstOrDefault(o => o.Slots == 4));
    public string? SixSlotsFrame => GetCurrentFrame(Options.FirstOrDefault(o => o.Slots == 6));

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        StopFrameRotation();
        LoadSampleFrames();
        UpdateVisibleOptions();
        StartFrameRotation();
    }

    private void UpdateVisibleOptions()
    {
        VisibleOptions.Clear();
        foreach (var option in Options)
        {
            VisibleOptions.Add(option);
        }
    }

    private void LoadSampleFrames()
    {
        _layoutFrames.Clear();
        _layoutIndices.Clear();
        _currentFrames.Clear();

        var fourBySixRoot = Path.Combine(_framesRoot, "4x6");
        if (!Directory.Exists(fourBySixRoot))
        {
            return;
        }

        foreach (var option in Options)
        {
            var slotFolder = $"{option.Slots}slots";
            var slotRoot = Path.Combine(fourBySixRoot, slotFolder);
            if (!Directory.Exists(slotRoot))
            {
                continue;
            }

            var frames = new List<string>();
            foreach (var categoryDir in Directory.EnumerateDirectories(slotRoot))
            {
                var categoryFrames = Directory.EnumerateFiles(categoryDir, "*.png")
                    .OrderBy(f => Path.GetFileName(f))
                    .ToList();
                frames.AddRange(categoryFrames);
            }

            frames = frames.Distinct().ToList();
            _layoutFrames[option] = frames;

            if (frames.Count > 0)
            {
                var initialIndex = _random.Next(frames.Count);
                _layoutIndices[option] = initialIndex;
                _currentFrames[option] = frames[initialIndex];
            }
            else
            {
                _layoutIndices[option] = 0;
                _currentFrames[option] = string.Empty;
            }
        }

        OnPropertyChanged(nameof(Options));
        UpdateVisibleOptions();
    }

    private void StartFrameRotation()
    {
        var hasFrames = _layoutFrames.Values.Any(frames => frames.Count > 1);
        if (hasFrames)
        {
            _frameRotationTimer.Start();
        }
    }

    private void StopFrameRotation()
    {
        _frameRotationTimer.Stop();
    }

    private void OnFrameRotationTimerTick(object? sender, EventArgs e)
    {
        // Rotate all layout frames together at the same time
        foreach (var option in Options)
        {
            if (!_layoutFrames.TryGetValue(option, out var frames) || frames.Count <= 1)
            {
                continue;
            }

            var currentIndex = _layoutIndices[option];
            int newIndex;
            if (frames.Count == 1)
            {
                newIndex = 0;
            }
            else
            {
                do
                {
                    newIndex = _random.Next(frames.Count);
                } while (newIndex == currentIndex && frames.Count > 1);
            }

            _layoutIndices[option] = newIndex;
            _currentFrames[option] = frames[newIndex];
        }

        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(TwoSlotsFrame));
        OnPropertyChanged(nameof(FourSlotsFrame));
        OnPropertyChanged(nameof(SixSlotsFrame));
    }

    private void SelectLayout(LayoutOption option)
    {
        _session.Current.SetLayout(option.Style);
        KawaiiStudio.App.App.Log($"LAYOUT_SELECTED value={option.TemplateType}");
        _navigation.Navigate("category");
    }
}
