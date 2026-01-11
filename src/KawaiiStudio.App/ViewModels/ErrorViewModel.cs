using System.Windows;
using System.Windows.Input;

namespace KawaiiStudio.App.ViewModels;

public sealed class ErrorViewModel : ViewModelBase
{
    private readonly RelayCommand _closeCommand;
    private string _title = "System Error";
    private string _message = "An error occurred.";

    public ErrorViewModel()
    {
        _closeCommand = new RelayCommand(CloseApp);
        CloseCommand = _closeCommand;
    }

    public ICommand CloseCommand { get; }

    public string Title
    {
        get => _title;
        private set
        {
            _title = value;
            OnPropertyChanged();
        }
    }

    public string Message
    {
        get => _message;
        private set
        {
            _message = value;
            OnPropertyChanged();
        }
    }

    public void SetError(string title, string message)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "System Error" : title.Trim();
        Message = string.IsNullOrWhiteSpace(message) ? "An error occurred." : message.Trim();
    }

    private void CloseApp()
    {
        KawaiiStudio.App.App.Log("ERROR_CLOSE_REQUESTED");
        Application.Current.Shutdown();
    }
}
