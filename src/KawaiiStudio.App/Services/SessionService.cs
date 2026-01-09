using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class SessionService
{
    public SessionState Current { get; } = new();

    public void StartNewSession()
    {
        Current.Reset();
    }
}
