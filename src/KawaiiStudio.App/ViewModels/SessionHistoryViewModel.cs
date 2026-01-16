using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class SessionHistoryViewModel : ViewModelBase
{
    private readonly AppPaths _appPaths;
    private readonly PrinterService _printerService;
    private readonly QrCodeService _qrCodeService;
    private readonly SettingsService _settings;
    private SessionHistoryItem? _selectedSession;
    private string _statusMessage = string.Empty;
    private bool _isLoading;
    private BitmapSource? _qrCodeImage;
    private string? _qrCodeUrl;
    private bool _isQrDialogVisible;

    public SessionHistoryViewModel(
        AppPaths appPaths,
        PrinterService printerService,
        QrCodeService qrCodeService,
        SettingsService settings)
    {
        _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
        _printerService = printerService ?? throw new ArgumentNullException(nameof(printerService));
        _qrCodeService = qrCodeService ?? throw new ArgumentNullException(nameof(qrCodeService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        Sessions = new ObservableCollection<SessionHistoryItem>();
        RefreshCommand = new RelayCommand(Refresh);
        ReprintCommand = new RelayCommand<SessionHistoryItem>(Reprint, item => item != null && item.HasFinalImage);
        ViewQrCommand = new RelayCommand<SessionHistoryItem>(ViewQr, item => item != null && item.HasQrUrl);
        CloseQrDialogCommand = new RelayCommand(() => IsQrDialogVisible = false);

        Refresh();
    }

    public ObservableCollection<SessionHistoryItem> Sessions { get; }

    public ICommand RefreshCommand { get; }
    public ICommand ReprintCommand { get; }
    public ICommand ViewQrCommand { get; }
    public ICommand CloseQrDialogCommand { get; }

    public SessionHistoryItem? SelectedSession
    {
        get => _selectedSession;
        set
        {
            _selectedSession = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (string.Equals(_statusMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public BitmapSource? QrCodeImage
    {
        get => _qrCodeImage;
        private set
        {
            _qrCodeImage = value;
            OnPropertyChanged();
        }
    }

    public string? QrCodeUrl
    {
        get => _qrCodeUrl;
        private set
        {
            _qrCodeUrl = value;
            OnPropertyChanged();
        }
    }

    public bool IsQrDialogVisible
    {
        get => _isQrDialogVisible;
        set
        {
            if (_isQrDialogVisible == value)
            {
                return;
            }

            _isQrDialogVisible = value;
            OnPropertyChanged();
        }
    }

    private void Refresh()
    {
        IsLoading = true;
        StatusMessage = "Loading sessions...";
        Sessions.Clear();

        Task.Run(() =>
        {
            try
            {
                var sessionsRoot = _appPaths.SessionsRoot;
                if (!Directory.Exists(sessionsRoot))
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = "No sessions folder found";
                        IsLoading = false;
                    });
                    return;
                }

                var sessions = new List<SessionHistoryItem>();

                foreach (var dateFolder in Directory.GetDirectories(sessionsRoot))
                {
                    var folderName = Path.GetFileName(dateFolder);
                    if (string.IsNullOrWhiteSpace(folderName) || folderName.Length != 8)
                    {
                        continue;
                    }

                    if (!DateTime.TryParseExact(folderName, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date))
                    {
                        continue;
                    }

                    var logFilePath = Path.Combine(dateFolder, "session.log");
                    if (!File.Exists(logFilePath))
                    {
                        continue;
                    }

                    var sessionFolders = Directory.GetDirectories(dateFolder, "session_*");
                    foreach (var sessionFolder in sessionFolders)
                    {
                        var sessionItem = ParseSession(sessionFolder, date, logFilePath);
                        if (sessionItem != null)
                        {
                            sessions.Add(sessionItem);
                        }
                    }
                }

                var sortedSessions = sessions.OrderByDescending(s => s.SessionDate).ToList();

                App.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var session in sortedSessions)
                    {
                        Sessions.Add(session);
                    }

                    StatusMessage = $"Loaded {Sessions.Count} sessions";
                    IsLoading = false;
                });
            }
            catch (Exception ex)
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"Error: {ex.Message}";
                    IsLoading = false;
                });
            }
        });
    }

    private static SessionHistoryItem? ParseSession(string sessionFolder, DateTime date, string logFilePath)
    {
        var sessionName = Path.GetFileName(sessionFolder);
        if (string.IsNullOrWhiteSpace(sessionName) || !sessionName.StartsWith("session_", StringComparison.Ordinal))
        {
            return null;
        }

        var sessionId = sessionName;
        var finalImagePath = Path.Combine(sessionFolder, $"{sessionName}_final.png");
        var hasFinalImage = File.Exists(finalImagePath);

        if (!hasFinalImage)
        {
            finalImagePath = null;
        }

        var item = new SessionHistoryItem
        {
            SessionId = sessionId,
            SessionFolder = sessionFolder,
            FinalImagePath = finalImagePath,
            HasFinalImage = hasFinalImage,
            SessionDate = date
        };

        try
        {
            var logLines = File.ReadAllLines(logFilePath);
            var sessionStartPattern = new Regex(@"SESSION_START id=(\S+)");
            var sizePattern = new Regex(@"SIZE_SELECTED value=(\S+)");
            var quantityPattern = new Regex(@"QUANTITY_SELECTED value=(\d+)");
            var paymentPattern = new Regex(@"PAYMENT_COMPLETED total=([\d.]+)");
            var uploadOkPattern = new Regex(@"UPLOAD_OK");
            var qrUrlPattern = new Regex(@"QR_URL=(.+)");

            var sessionStartFound = false;
            var sessionStartTime = date;

            foreach (var line in logLines)
            {
                if (!sessionStartFound)
                {
                    var match = sessionStartPattern.Match(line);
                    if (match.Success && match.Groups[1].Value == sessionId)
                    {
                        sessionStartFound = true;
                        if (TryParseLogTimestamp(line, out var timestamp))
                        {
                            sessionStartTime = timestamp;
                            item.SessionDate = timestamp;
                        }
                        continue;
                    }
                }

                if (!sessionStartFound)
                {
                    continue;
                }

                var sizeMatch = sizePattern.Match(line);
                if (sizeMatch.Success)
                {
                    var sizeValue = sizeMatch.Groups[1].Value;
                    item.Size = sizeValue == "2x6" ? PrintSize.TwoBySix : PrintSize.FourBySix;
                    continue;
                }

                var quantityMatch = quantityPattern.Match(line);
                if (quantityMatch.Success)
                {
                    if (int.TryParse(quantityMatch.Groups[1].Value, out var quantity))
                    {
                        item.Quantity = quantity;
                    }
                    continue;
                }

                var paymentMatch = paymentPattern.Match(line);
                if (paymentMatch.Success)
                {
                    if (decimal.TryParse(paymentMatch.Groups[1].Value, out var amount))
                    {
                        item.PaymentAmount = amount;
                        item.PaymentMethod = line.Contains("CARD_PAYMENT") || line.Contains("CARD_PAYMENT_APPROVED") ? "card" : "cash";
                    }
                    continue;
                }

                var qrUrlMatch = qrUrlPattern.Match(line);
                if (qrUrlMatch.Success)
                {
                    item.QrUrl = qrUrlMatch.Groups[1].Value.Trim();
                    item.HasQrUrl = true;
                    continue;
                }

                if (uploadOkPattern.IsMatch(line))
                {
                    item.HasQrUrl = true;
                    if (string.IsNullOrWhiteSpace(item.QrUrl))
                    {
                        item.QrUrl = "Generated (URL not logged)";
                    }
                    continue;
                }

                if (line.Contains("SESSION_END") && line.Contains(sessionId))
                {
                    break;
                }
            }
        }
        catch
        {
        }

        return item;
    }

    private static bool TryParseLogTimestamp(string line, out DateTime timestamp)
    {
        timestamp = DateTime.MinValue;
        if (line.Length < 19)
        {
            return false;
        }

        var datePart = line.Substring(0, 10);
        var timePart = line.Substring(11, 8);

        if (DateTime.TryParse($"{datePart} {timePart}", out timestamp))
        {
            return true;
        }

        return false;
    }

    private async void Reprint(SessionHistoryItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.FinalImagePath) || !File.Exists(item.FinalImagePath))
        {
            StatusMessage = "Cannot reprint: Final image not found";
            return;
        }

        if (item.Size == null)
        {
            StatusMessage = "Cannot reprint: Session size unknown";
            return;
        }

        StatusMessage = "Printing...";
        try
        {
            var sessionState = new SessionState();
            sessionState.SetFinalImagePath(item.FinalImagePath);
            if (item.Size.HasValue)
            {
                sessionState.SetSize(item.Size.Value);
            }

            if (item.Quantity.HasValue)
            {
                sessionState.SetQuantity(item.Quantity.Value);
            }

            var result = await _printerService.PrintAsync(sessionState, CancellationToken.None);
            if (result.ok)
            {
                StatusMessage = "Print sent successfully";
                App.Log($"REPRINT_OK session={item.SessionId}");
            }
            else
            {
                StatusMessage = $"Print failed: {result.error ?? "Unknown error"}";
                App.Log($"REPRINT_FAILED session={item.SessionId} error={result.error}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Print error: {ex.Message}";
            App.Log($"REPRINT_ERROR session={item.SessionId} error={ex.Message}");
        }
    }

    private void ViewQr(SessionHistoryItem? item)
    {
        if (item == null || !item.HasQrUrl)
        {
            StatusMessage = "QR code not available for this session";
            return;
        }

        if (string.IsNullOrWhiteSpace(item.QrUrl) || item.QrUrl == "Generated (URL not logged)")
        {
            StatusMessage = "QR code was generated but URL is not available. Check the printed photo for the QR code.";
            return;
        }

        QrCodeUrl = item.QrUrl;
        var qrImage = _qrCodeService.Render(item.QrUrl, 20);
        if (qrImage == null)
        {
            StatusMessage = "Failed to generate QR code image";
            return;
        }

        QrCodeImage = qrImage;
        IsQrDialogVisible = true;
    }
}
