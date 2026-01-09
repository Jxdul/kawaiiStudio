using System;
using System.Collections.Generic;
using KawaiiStudio.App.ViewModels;

namespace KawaiiStudio.App.Services;

public sealed class NavigationService
{
    private readonly Dictionary<string, ViewModelBase> _viewModels = new(StringComparer.OrdinalIgnoreCase);

    public event Action<ViewModelBase>? Navigated;

    public void Register(string key, ViewModelBase viewModel)
    {
        _viewModels[key] = viewModel;
    }

    public void Navigate(string key)
    {
        if (_viewModels.TryGetValue(key, out var viewModel))
        {
            if (viewModel is INavigationAware navigationAware)
            {
                navigationAware.OnNavigatedTo();
            }

            Navigated?.Invoke(viewModel);
        }
    }
}
