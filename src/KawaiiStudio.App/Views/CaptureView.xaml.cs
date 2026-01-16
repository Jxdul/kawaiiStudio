using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KawaiiStudio.App.Views;

public partial class CaptureView : UserControl
{
    private readonly Random _random = new Random();
    private string? _lastCountdownText;
    private ViewModels.CaptureViewModel? _viewModel;

    public CaptureView()
    {
        InitializeComponent();
        this.Loaded += OnLoaded;
        this.DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeToViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UnsubscribeFromViewModel();
        SubscribeToViewModel();
    }

    private void SubscribeToViewModel()
    {
        if (DataContext is ViewModels.CaptureViewModel viewModel)
        {
            _viewModel = viewModel;
            if (viewModel is INotifyPropertyChanged notifyPropertyChanged)
            {
                notifyPropertyChanged.PropertyChanged += OnViewModelPropertyChanged;
            }
        }
    }

    private void UnsubscribeFromViewModel()
    {
        if (_viewModel is INotifyPropertyChanged notifyPropertyChanged)
        {
            notifyPropertyChanged.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "CountdownText" && _viewModel != null && _viewModel.IsCountdownActive)
        {
            var currentText = _viewModel.CountdownText;
            if (currentText != _lastCountdownText && !string.IsNullOrEmpty(currentText))
            {
                _lastCountdownText = currentText;
                Dispatcher.BeginInvoke(new Action(() => AnimateCountdown()), DispatcherPriority.Render);
            }
        }
        else if (e.PropertyName == "IsCountdownActive" && _viewModel != null && !_viewModel.IsCountdownActive)
        {
            _lastCountdownText = null;
        }
    }

    private void AnimateCountdown()
    {
        // Reset transforms
        CountdownTranslate.X = 0;
        CountdownTranslate.Y = 0;
        CountdownRotate.Angle = 0;
        CountdownTextBlock.Opacity = 0;

        // Generate random values for confetti effect
        var randomX = (_random.NextDouble() - 0.5) * 100; // -50 to +50
        var randomRotation = (_random.NextDouble() - 0.5) * 720; // -360 to +360
        var fallDistance = 100 + _random.NextDouble() * 100; // 100 to 200
        var fallDuration = TimeSpan.FromSeconds(0.6 + _random.NextDouble() * 0.3); // 0.6 to 0.9 seconds

        // Create animation timeline
        var storyboard = new Storyboard();

        // Fade in and move up
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3));
        Storyboard.SetTarget(fadeIn, CountdownTextBlock);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(fadeIn);

        var moveUpY = new DoubleAnimation(0, -50, TimeSpan.FromSeconds(0.3));
        moveUpY.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        Storyboard.SetTarget(moveUpY, CountdownTranslate);
        Storyboard.SetTargetProperty(moveUpY, new PropertyPath(TranslateTransform.YProperty));
        storyboard.Children.Add(moveUpY);

        // Hold at top briefly
        var pauseTime = TimeSpan.FromSeconds(0.1);

        // Confetti fall: move down with gravity, horizontal drift, rotation
        var moveDownY = new DoubleAnimation(-50, fallDistance, fallDuration);
        moveDownY.BeginTime = TimeSpan.FromSeconds(0.4);
        moveDownY.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        Storyboard.SetTarget(moveDownY, CountdownTranslate);
        Storyboard.SetTargetProperty(moveDownY, new PropertyPath(TranslateTransform.YProperty));
        storyboard.Children.Add(moveDownY);

        var moveX = new DoubleAnimation(0, randomX, fallDuration);
        moveX.BeginTime = TimeSpan.FromSeconds(0.4);
        moveX.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
        Storyboard.SetTarget(moveX, CountdownTranslate);
        Storyboard.SetTargetProperty(moveX, new PropertyPath(TranslateTransform.XProperty));
        storyboard.Children.Add(moveX);

        var rotate = new DoubleAnimation(0, randomRotation, fallDuration);
        rotate.BeginTime = TimeSpan.FromSeconds(0.4);
        rotate.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        Storyboard.SetTarget(rotate, CountdownRotate);
        Storyboard.SetTargetProperty(rotate, new PropertyPath(RotateTransform.AngleProperty));
        storyboard.Children.Add(rotate);

        var fadeOut = new DoubleAnimation(1, 0, fallDuration);
        fadeOut.BeginTime = TimeSpan.FromSeconds(0.4);
        fadeOut.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        Storyboard.SetTarget(fadeOut, CountdownTextBlock);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(fadeOut);

        storyboard.Begin();
    }
}
