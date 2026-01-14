using System.Threading;
using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class PrintingViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly PrinterService _printer;
    private string? _printPreviewPath;
    private string _statusText = "Preparing print...";
    private bool _printStarted;

    public PrintingViewModel(
        NavigationService navigation,
        SessionService session,
        PrinterService printer,
        ThemeCatalogService themeCatalog)
        : base(themeCatalog, "printing")
    {
        _navigation = navigation;
        _session = session;
        _printer = printer;
        ContinueCommand = new RelayCommand(() => _navigation.Navigate("thank_you"));
    }

    public ICommand ContinueCommand { get; }

    public string? PrintPreviewPath
    {
        get => _printPreviewPath;
        private set
        {
            _printPreviewPath = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        KawaiiStudio.App.App.Log("PRINTING_START");
        PrintPreviewPath = _session.Current.FinalImagePath;
        StartPrint();
    }

    private async void StartPrint()
    {
        if (_printStarted)
        {
            return;
        }

        _printStarted = true;
        try
        {
            StatusText = "Sending to printer...";
            var result = await _printer.PrintAsync(_session.Current, CancellationToken.None);
            if (result.ok)
            {
                _session.SetPrintJob(result.jobId ?? string.Empty, "sent");
                StatusText = "Printing...";
                KawaiiStudio.App.App.Log($"PRINT_SENT job={result.jobId ?? "unknown"}");
            }
            else
            {
                _session.SetPrintJob(string.Empty, "error");
                var reason = string.IsNullOrWhiteSpace(result.error) ? "print_failed" : result.error;
                StatusText = "Print failed. Call staff.";
                KawaiiStudio.App.App.Log($"PRINT_FAILED reason={reason}");
            }
        }
        finally
        {
            _printStarted = false;
        }
    }
}
