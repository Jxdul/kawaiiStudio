using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class DiagnosticsViewModel : ViewModelBase
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2.5);
    private readonly SettingsService _settings;
    private readonly SessionService _session;
    private readonly CameraService _camera;
    private readonly CashAcceptorService _cashAcceptor;
    private readonly ICardPaymentProvider _cardPayment;
    private readonly AppPaths _appPaths;
    private readonly DispatcherTimer _pollTimer;
    private PerformanceCounter? _cpuCounter;
    private bool _metricsInitialized;
    private DateTime _startTime;
    private double _cpuUsage;
    private double _memoryUsage;
    private double _diskUsage;
    private string _memoryText = "N/A";
    private string _diskText = "N/A";
    private bool _networkConnected;
    private bool _cameraConnected;
    private bool _cashAcceptorConnected;
    private bool _cardReaderConnected;
    private string _printerStatus = "Unknown";
    private string _currentSessionId = "None";
    private string _uptime = "0:00:00";
    private string _softwareVersion = "Unknown";
    private string _boothId = "Unknown";
    private bool _testMode;

    public DiagnosticsViewModel(
        SettingsService settings,
        SessionService session,
        CameraService camera,
        CashAcceptorService cashAcceptor,
        ICardPaymentProvider cardPayment,
        AppPaths appPaths)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _cashAcceptor = cashAcceptor ?? throw new ArgumentNullException(nameof(cashAcceptor));
        _cardPayment = cardPayment ?? throw new ArgumentNullException(nameof(cardPayment));
        _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));

        _startTime = Process.GetCurrentProcess().StartTime;
        _pollTimer = new DispatcherTimer
        {
            Interval = PollInterval
        };
        _pollTimer.Tick += OnPollTimerTick;

        RefreshCommand = new RelayCommand(Refresh);
        InitializeMetrics();
        Refresh();
    }

    public ICommand RefreshCommand { get; }

    public double CpuUsage
    {
        get => _cpuUsage;
        private set
        {
            if (Math.Abs(_cpuUsage - value) < 0.1)
            {
                return;
            }

            _cpuUsage = value;
            OnPropertyChanged();
        }
    }

    public double MemoryUsage
    {
        get => _memoryUsage;
        private set
        {
            if (Math.Abs(_memoryUsage - value) < 0.1)
            {
                return;
            }

            _memoryUsage = value;
            OnPropertyChanged();
        }
    }

    public double DiskUsage
    {
        get => _diskUsage;
        private set
        {
            if (Math.Abs(_diskUsage - value) < 0.1)
            {
                return;
            }

            _diskUsage = value;
            OnPropertyChanged();
        }
    }

    public string MemoryText
    {
        get => _memoryText;
        private set
        {
            if (string.Equals(_memoryText, value, StringComparison.Ordinal))
            {
                return;
            }

            _memoryText = value;
            OnPropertyChanged();
        }
    }

    public string DiskText
    {
        get => _diskText;
        private set
        {
            if (string.Equals(_diskText, value, StringComparison.Ordinal))
            {
                return;
            }

            _diskText = value;
            OnPropertyChanged();
        }
    }

    public bool NetworkConnected
    {
        get => _networkConnected;
        private set
        {
            if (_networkConnected == value)
            {
                return;
            }

            _networkConnected = value;
            OnPropertyChanged();
        }
    }

    public bool CameraConnected
    {
        get => _cameraConnected;
        private set
        {
            if (_cameraConnected == value)
            {
                return;
            }

            _cameraConnected = value;
            OnPropertyChanged();
        }
    }

    public bool CashAcceptorConnected
    {
        get => _cashAcceptorConnected;
        private set
        {
            if (_cashAcceptorConnected == value)
            {
                return;
            }

            _cashAcceptorConnected = value;
            OnPropertyChanged();
        }
    }

    public bool CardReaderConnected
    {
        get => _cardReaderConnected;
        private set
        {
            if (_cardReaderConnected == value)
            {
                return;
            }

            _cardReaderConnected = value;
            OnPropertyChanged();
        }
    }

    public string PrinterStatus
    {
        get => _printerStatus;
        private set
        {
            if (string.Equals(_printerStatus, value, StringComparison.Ordinal))
            {
                return;
            }

            _printerStatus = value;
            OnPropertyChanged();
        }
    }

    public string CurrentSessionId
    {
        get => _currentSessionId;
        private set
        {
            if (string.Equals(_currentSessionId, value, StringComparison.Ordinal))
            {
                return;
            }

            _currentSessionId = value;
            OnPropertyChanged();
        }
    }

    public string Uptime
    {
        get => _uptime;
        private set
        {
            if (string.Equals(_uptime, value, StringComparison.Ordinal))
            {
                return;
            }

            _uptime = value;
            OnPropertyChanged();
        }
    }

    public string SoftwareVersion
    {
        get => _softwareVersion;
        private set
        {
            if (string.Equals(_softwareVersion, value, StringComparison.Ordinal))
            {
                return;
            }

            _softwareVersion = value;
            OnPropertyChanged();
        }
    }

    public string BoothId
    {
        get => _boothId;
        private set
        {
            if (string.Equals(_boothId, value, StringComparison.Ordinal))
            {
                return;
            }

            _boothId = value;
            OnPropertyChanged();
        }
    }

    public bool TestMode
    {
        get => _testMode;
        private set
        {
            if (_testMode == value)
            {
                return;
            }

            _testMode = value;
            OnPropertyChanged();
        }
    }

    public void StartPolling()
    {
        if (!_pollTimer.IsEnabled)
        {
            _pollTimer.Start();
        }
    }

    public void StopPolling()
    {
        if (_pollTimer.IsEnabled)
        {
            _pollTimer.Stop();
        }
    }

    private void InitializeMetrics()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();
            _metricsInitialized = true;
        }
        catch
        {
            _metricsInitialized = false;
        }
    }

    private void OnPollTimerTick(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        RefreshSystemMetrics();
        RefreshHardwareStatus();
        RefreshApplicationInfo();
    }

    private void RefreshSystemMetrics()
    {
        if (_metricsInitialized && _cpuCounter != null)
        {
            try
            {
                CpuUsage = Math.Round(_cpuCounter.NextValue(), 1);
            }
            catch
            {
                CpuUsage = 0;
            }
        }

        try
        {
            var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64;
            long totalPhysicalMemory = 0;

            try
            {
                using var memCounter = new PerformanceCounter("Memory", "Available Bytes");
                var availableBytes = (long)memCounter.NextValue();

                using var totalCounter = new PerformanceCounter("Memory", "Committed Bytes");
                var committedBytes = (long)totalCounter.NextValue();

                if (availableBytes > 0 && committedBytes > 0)
                {
                    totalPhysicalMemory = availableBytes + committedBytes;
                }
            }
            catch
            {
                totalPhysicalMemory = 8L * 1024 * 1024 * 1024;
            }

            if (totalPhysicalMemory > 0 && workingSet > 0)
            {
                MemoryUsage = Math.Round((double)workingSet / totalPhysicalMemory * 100, 1);
                var usedMB = workingSet / (1024.0 * 1024.0);
                var totalMB = totalPhysicalMemory / (1024.0 * 1024.0);
                MemoryText = $"{usedMB:F1} MB / {totalMB:F1} MB";
            }
            else
            {
                MemoryText = "N/A";
            }
        }
        catch
        {
            MemoryUsage = 0;
            MemoryText = "N/A";
        }

        try
        {
            var sessionsRoot = _appPaths.SessionsRoot;
            var drivePath = Path.GetPathRoot(sessionsRoot) ?? "C:\\";
            var drive = new DriveInfo(drivePath);
            if (drive.IsReady && drive.TotalSize > 0)
            {
                var freeSpace = drive.AvailableFreeSpace;
                var totalSpace = drive.TotalSize;
                DiskUsage = Math.Round((double)(totalSpace - freeSpace) / totalSpace * 100, 1);
                var usedGB = (totalSpace - freeSpace) / (1024.0 * 1024.0 * 1024.0);
                var totalGB = totalSpace / (1024.0 * 1024.0 * 1024.0);
                DiskText = $"{usedGB:F1} GB / {totalGB:F1} GB";
            }
            else
            {
                DiskText = "N/A";
            }
        }
        catch
        {
            DiskUsage = 0;
            DiskText = "N/A";
        }

        try
        {
            NetworkConnected = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
        }
        catch
        {
            NetworkConnected = false;
        }
    }

    private void RefreshHardwareStatus()
    {
        CameraConnected = _camera.IsConnected;
        CashAcceptorConnected = _cashAcceptor.IsConnected;
        CardReaderConnected = _cardPayment.IsConnected;

        try
        {
            var printerName2x6 = _settings.PrinterName2x6;
            var printerName4x6 = _settings.PrinterName4x6;
            var status2x6 = CheckPrinterStatus(printerName2x6);
            var status4x6 = CheckPrinterStatus(printerName4x6);

            if (status2x6 == "OK" && status4x6 == "OK")
            {
                PrinterStatus = "OK (2x6, 4x6)";
            }
            else if (status2x6 == "OK")
            {
                PrinterStatus = $"OK (2x6), {status4x6} (4x6)";
            }
            else if (status4x6 == "OK")
            {
                PrinterStatus = $"{status2x6} (2x6), OK (4x6)";
            }
            else
            {
                PrinterStatus = $"{status2x6} (2x6), {status4x6} (4x6)";
            }
        }
        catch
        {
            PrinterStatus = "Error";
        }
    }

    private static string CheckPrinterStatus(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return "Not configured";
        }

        try
        {
            using var queue = new System.Printing.PrintQueue(new System.Printing.PrintServer(), printerName);
            queue.Refresh();
            var status = queue.QueueStatus;

            var errorFlags = System.Printing.PrintQueueStatus.Offline
                | System.Printing.PrintQueueStatus.Error
                | System.Printing.PrintQueueStatus.NotAvailable;

            if ((status & errorFlags) != System.Printing.PrintQueueStatus.None)
            {
                if ((status & System.Printing.PrintQueueStatus.Offline) != System.Printing.PrintQueueStatus.None)
                {
                    return "Offline";
                }

                if ((status & System.Printing.PrintQueueStatus.Error) != System.Printing.PrintQueueStatus.None)
                {
                    return "Error";
                }

                if ((status & System.Printing.PrintQueueStatus.NotAvailable) != System.Printing.PrintQueueStatus.None)
                {
                    return "Not available";
                }
            }

            return "OK";
        }
        catch
        {
            return "Not found";
        }
    }

    private void RefreshApplicationInfo()
    {
        CurrentSessionId = string.IsNullOrWhiteSpace(_session.Current.SessionId) ? "None" : _session.Current.SessionId;

        try
        {
            var uptime = DateTime.Now - _startTime;
            Uptime = $"{(int)uptime.TotalHours}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
        }
        catch
        {
            Uptime = "N/A";
        }

        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            SoftwareVersion = version?.ToString() ?? "Unknown";
        }
        catch
        {
            SoftwareVersion = "Unknown";
        }

        BoothId = string.IsNullOrWhiteSpace(_settings.BoothId) ? "Not configured" : _settings.BoothId;
        TestMode = _settings.TestMode;
    }
}
