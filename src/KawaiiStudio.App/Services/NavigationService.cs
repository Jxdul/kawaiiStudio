using System;
using System.Collections.Generic;
using KawaiiStudio.App.ViewModels;

namespace KawaiiStudio.App.Services;

public sealed class NavigationService
{
    private static readonly HashSet<string> PrePaymentKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "startup",
        "home",
        "size",
        "quantity",
        "layout",
        "category",
        "frame",
        "payment",
        "library"
    };

    private readonly Dictionary<string, ViewModelBase> _viewModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly SessionService _session;

    public event Action<ViewModelBase>? Navigated;

    public NavigationService(SessionService session)
    {
        _session = session;
    }

    public string? CurrentKey { get; private set; }

    public void Register(string key, ViewModelBase viewModel)
    {
        _viewModels[key] = viewModel;
    }

    public void Navigate(string key)
    {
        if (!CanNavigate(key))
        {
            App.Log($"NAVIGATION_BLOCKED target={key}");
            return;
        }

        if (_viewModels.TryGetValue(key, out var viewModel))
        {
            if (viewModel is INavigationAware navigationAware)
            {
                navigationAware.OnNavigatedTo();
            }

            CurrentKey = key;
            Navigated?.Invoke(viewModel);
        }
    }

    private bool CanNavigate(string key)
    {
        if (!_session.Current.IsPaid || _session.Current.EndTime is not null)
        {
            return true;
        }

        if (string.Equals(key, "staff", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !PrePaymentKeys.Contains(key);
    }
}
