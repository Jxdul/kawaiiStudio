using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class SizeViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly SettingsService _settings;
    private readonly string _framesRoot;
    private readonly DispatcherTimer _frameRotationTimer;
    private readonly Random _random = new();
    private string _staffPin = string.Empty;
    private string _pendingStaffPin = string.Empty;
    private string _staffPinError = string.Empty;
    private bool _isStaffPinPromptVisible;
    private List<string> _twoBySixFrames = new();
    private List<string> _fourBySixFrames = new();
    private int _currentTwoBySixIndex = 0;
    private int _currentFourBySixIndex = 0;
    private string _currentTwoBySixFrame = string.Empty;
    private string _currentFourBySixFrame = string.Empty;
    private double _twoBySixOpacity = 1.0;
    private double _fourBySixOpacity = 1.0;

    public SizeViewModel(
        NavigationService navigation,
        SessionService session,
        ThemeCatalogService themeCatalog,
        SettingsService settings,
        AppPaths appPaths)
        : base(themeCatalog, "size")
    {
        _navigation = navigation;
        _session = session;
        _settings = settings;
        _framesRoot = appPaths.FramesRoot;

        SelectSizeCommand = new RelayCommand<PrintSize>(SelectSize);
        BackCommand = new RelayCommand(() => _navigation.Navigate("home"));
        StaffCommand = new RelayCommand(RequestStaffAccess);
        StaffPinInputCommand = new RelayCommand<string>(StaffPinInput);
        StaffPinBackspaceCommand = new RelayCommand(StaffPinBackspace);
        StaffPinClearCommand = new RelayCommand(StaffPinClear);
        StaffPinConfirmCommand = new RelayCommand(StaffPinConfirm);
        StaffPinCancelCommand = new RelayCommand(StaffPinCancel);

        LoadSampleFrames();

        _frameRotationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.0) // Rotate every second
        };
        _frameRotationTimer.Tick += OnFrameRotationTimerTick;
    }

    public string CurrentTwoBySixFrame
    {
        get => _currentTwoBySixFrame;
        private set
        {
            _currentTwoBySixFrame = value;
            OnPropertyChanged();
        }
    }

    public string CurrentFourBySixFrame
    {
        get => _currentFourBySixFrame;
        private set
        {
            _currentFourBySixFrame = value;
            OnPropertyChanged();
        }
    }

    public double TwoBySixOpacity
    {
        get => _twoBySixOpacity;
        set
        {
            _twoBySixOpacity = value;
            OnPropertyChanged();
        }
    }

    public double FourBySixOpacity
    {
        get => _fourBySixOpacity;
        set
        {
            _fourBySixOpacity = value;
            OnPropertyChanged();
        }
    }

    public ICommand SelectSizeCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand StaffCommand { get; }
    public ICommand StaffPinInputCommand { get; }
    public ICommand StaffPinBackspaceCommand { get; }
    public ICommand StaffPinClearCommand { get; }
    public ICommand StaffPinConfirmCommand { get; }
    public ICommand StaffPinCancelCommand { get; }

    public bool IsStaffPinPromptVisible
    {
        get => _isStaffPinPromptVisible;
        private set
        {
            _isStaffPinPromptVisible = value;
            OnPropertyChanged();
        }
    }

    public string StaffPinDisplay => new string('*', _pendingStaffPin.Length);

    public string StaffPinError
    {
        get => _staffPinError;
        private set
        {
            _staffPinError = value;
            OnPropertyChanged();
        }
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        ClearStaffPinPrompt();
        StopFrameRotation(); // Stop any existing timer
        LoadSampleFrames();
        StartFrameRotation();
    }

    private void LoadSampleFrames()
    {
        _twoBySixFrames.Clear();
        _fourBySixFrames.Clear();

        // Load 2x6 frames
        var twoBySixRoot = Path.Combine(_framesRoot, "2x6");
        if (Directory.Exists(twoBySixRoot))
        {
            foreach (var categoryDir in Directory.EnumerateDirectories(twoBySixRoot))
            {
                var frames = Directory.EnumerateFiles(categoryDir, "*.png")
                    .OrderBy(f => Path.GetFileName(f))
                    .ToList();
                _twoBySixFrames.AddRange(frames);
            }
        }

        // Load 4x6 frames (from any slot folder)
        var fourBySixRoot = Path.Combine(_framesRoot, "4x6");
        if (Directory.Exists(fourBySixRoot))
        {
            foreach (var slotDir in Directory.EnumerateDirectories(fourBySixRoot))
            {
                foreach (var categoryDir in Directory.EnumerateDirectories(slotDir))
                {
                    var frames = Directory.EnumerateFiles(categoryDir, "*.png")
                        .OrderBy(f => Path.GetFileName(f))
                        .ToList();
                    _fourBySixFrames.AddRange(frames);
                }
            }
        }

        // Remove duplicates
        _twoBySixFrames = _twoBySixFrames.Distinct().ToList();
        _fourBySixFrames = _fourBySixFrames.Distinct().ToList();

        // Set initial frames
        if (_twoBySixFrames.Count > 0)
        {
            _currentTwoBySixIndex = _random.Next(_twoBySixFrames.Count);
            CurrentTwoBySixFrame = _twoBySixFrames[_currentTwoBySixIndex];
        }

        if (_fourBySixFrames.Count > 0)
        {
            _currentFourBySixIndex = _random.Next(_fourBySixFrames.Count);
            CurrentFourBySixFrame = _fourBySixFrames[_currentFourBySixIndex];
        }
    }

    private void StartFrameRotation()
    {
        if (_twoBySixFrames.Count > 1 || _fourBySixFrames.Count > 1)
        {
            ResetTimer();
            _frameRotationTimer.Start();
        }
    }

    private void StopFrameRotation()
    {
        _frameRotationTimer.Stop();
    }

    private void OnFrameRotationTimerTick(object? sender, EventArgs e)
    {
        // Rotate both frames together at the same time
        if (_twoBySixFrames.Count > 1)
        {
            RotateFrame(true);
        }

        if (_fourBySixFrames.Count > 1)
        {
            RotateFrame(false);
        }

        ResetTimer();
    }

    private void RotateFrame(bool isTwoBySix)
    {
        var frames = isTwoBySix ? _twoBySixFrames : _fourBySixFrames;
        var currentIndex = isTwoBySix ? _currentTwoBySixIndex : _currentFourBySixIndex;

        if (frames.Count == 0)
        {
            return;
        }

        // Pick a random different frame
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

        if (isTwoBySix)
        {
            _currentTwoBySixIndex = newIndex;
            CurrentTwoBySixFrame = frames[newIndex];
            OnPropertyChanged(nameof(CurrentTwoBySixFrame)); // Trigger fade animation
        }
        else
        {
            _currentFourBySixIndex = newIndex;
            CurrentFourBySixFrame = frames[newIndex];
            OnPropertyChanged(nameof(CurrentFourBySixFrame)); // Trigger fade animation
        }
    }

    private void ResetTimer()
    {
        _frameRotationTimer.Interval = TimeSpan.FromSeconds(1.0);
    }

    private void SelectSize(PrintSize size)
    {
        _session.Current.SetSize(size);
        KawaiiStudio.App.App.Log($"SIZE_SELECTED value={FormatSize(size)}");
        _navigation.Navigate("quantity");
    }

    private static string FormatSize(PrintSize size)
    {
        return size == PrintSize.TwoBySix ? "2x6" : "4x6";
    }

    private void RequestStaffAccess()
    {
        _settings.Reload();
        var configuredPin = _settings.StaffPin;
        if (string.IsNullOrWhiteSpace(configuredPin))
        {
            ClearStaffPinPrompt();
            _navigation.Navigate("staff");
            return;
        }

        _staffPin = configuredPin.Trim();
        SetPendingStaffPin(string.Empty);
        StaffPinError = string.Empty;
        IsStaffPinPromptVisible = true;
    }

    private void StaffPinInput(string input)
    {
        if (!IsStaffPinPromptVisible || string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var digit = input.Trim();
        if (digit.Length != 1 || digit[0] < '0' || digit[0] > '9')
        {
            return;
        }

        SetPendingStaffPin(_pendingStaffPin + digit);
        StaffPinError = string.Empty;
    }

    private void StaffPinBackspace()
    {
        if (!IsStaffPinPromptVisible || _pendingStaffPin.Length == 0)
        {
            return;
        }

        SetPendingStaffPin(_pendingStaffPin[..^1]);
        StaffPinError = string.Empty;
    }

    private void StaffPinClear()
    {
        if (!IsStaffPinPromptVisible)
        {
            return;
        }

        SetPendingStaffPin(string.Empty);
        StaffPinError = string.Empty;
    }

    private void StaffPinConfirm()
    {
        if (!IsStaffPinPromptVisible)
        {
            return;
        }

        if (string.Equals(_pendingStaffPin, _staffPin, StringComparison.Ordinal))
        {
            ClearStaffPinPrompt();
            _navigation.Navigate("staff");
            return;
        }

        StaffPinError = "Incorrect PIN";
        SetPendingStaffPin(string.Empty);
    }

    private void StaffPinCancel()
    {
        if (!IsStaffPinPromptVisible)
        {
            return;
        }

        ClearStaffPinPrompt();
    }

    private void ClearStaffPinPrompt()
    {
        _staffPin = string.Empty;
        SetPendingStaffPin(string.Empty);
        StaffPinError = string.Empty;
        IsStaffPinPromptVisible = false;
    }

    private void SetPendingStaffPin(string value)
    {
        _pendingStaffPin = value ?? string.Empty;
        OnPropertyChanged(nameof(StaffPinDisplay));
    }
}
