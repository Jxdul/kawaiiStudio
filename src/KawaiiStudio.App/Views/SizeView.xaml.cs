using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using KawaiiStudio.App.ViewModels;

namespace KawaiiStudio.App.Views;

public partial class SizeView : UserControl
{
    private string? _lastTwoBySixFrame;
    private string? _lastFourBySixFrame;

    public SizeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SizeViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is SizeViewModel newViewModel)
        {
            newViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _lastTwoBySixFrame = newViewModel.CurrentTwoBySixFrame;
            _lastFourBySixFrame = newViewModel.CurrentFourBySixFrame;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SizeViewModel viewModel)
        {
            return;
        }

        if (e.PropertyName == nameof(SizeViewModel.CurrentTwoBySixFrame))
        {
            if (_lastTwoBySixFrame != viewModel.CurrentTwoBySixFrame && !string.IsNullOrEmpty(viewModel.CurrentTwoBySixFrame))
            {
                FadeInImage(TwoBySixImage);
                _lastTwoBySixFrame = viewModel.CurrentTwoBySixFrame;
            }
        }
        else if (e.PropertyName == nameof(SizeViewModel.CurrentFourBySixFrame))
        {
            if (_lastFourBySixFrame != viewModel.CurrentFourBySixFrame && !string.IsNullOrEmpty(viewModel.CurrentFourBySixFrame))
            {
                FadeInImage(FourBySixImage);
                _lastFourBySixFrame = viewModel.CurrentFourBySixFrame;
            }
        }
    }

    private static void FadeInImage(UIElement element)
    {
        var storyboard = new Storyboard();
        var fadeAnimation = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromSeconds(0.8))
        };
        Storyboard.SetTarget(fadeAnimation, element);
        Storyboard.SetTargetProperty(fadeAnimation, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fadeAnimation);
        storyboard.Begin();
    }
}
