using System;

namespace KawaiiStudio.App.ViewModels;

public sealed class TemplateEditorSlotViewModel : ViewModelBase
{
    private const double MinimumSize = 40;
    private double _x;
    private double _y;
    private double _width;
    private double _height;
    private double _aspectRatio;

    public TemplateEditorSlotViewModel(int index, bool isQr, double x, double y, double width, double height)
    {
        Index = index;
        IsQr = isQr;
        _x = x;
        _y = y;
        _width = width;
        _height = height;
        _aspectRatio = CalculateAspectRatio(width, height);
    }

    public int Index { get; }
    public bool IsQr { get; }
    public string Label => IsQr ? "QR" : $"Slot {Index}";

    public double X
    {
        get => _x;
        private set
        {
            _x = value;
            OnPropertyChanged();
        }
    }

    public double Y
    {
        get => _y;
        private set
        {
            _y = value;
            OnPropertyChanged();
        }
    }

    public double Width
    {
        get => _width;
        private set
        {
            _width = value;
            OnPropertyChanged();
        }
    }

    public double Height
    {
        get => _height;
        private set
        {
            _height = value;
            OnPropertyChanged();
        }
    }

    public void Move(double deltaX, double deltaY, double canvasWidth, double canvasHeight)
    {
        var nextX = X + deltaX;
        var nextY = Y + deltaY;
        nextX = Clamp(nextX, 0, canvasWidth - Width);
        nextY = Clamp(nextY, 0, canvasHeight - Height);
        X = nextX;
        Y = nextY;
    }

    public void Resize(double deltaX, double deltaY, double canvasWidth, double canvasHeight)
    {
        var ratio = _aspectRatio <= 0 ? 1 : _aspectRatio;
        var maxWidth = Math.Max(MinimumSize, canvasWidth - X);
        var maxHeight = Math.Max(MinimumSize, canvasHeight - Y);

        var useWidth = Math.Abs(deltaX) >= Math.Abs(deltaY);
        if (useWidth)
        {
            var nextWidth = Clamp(Width + deltaX, MinimumSize, maxWidth);
            var nextHeight = nextWidth / ratio;
            if (nextHeight > maxHeight)
            {
                nextHeight = maxHeight;
                nextWidth = nextHeight * ratio;
            }

            Width = Math.Max(MinimumSize, nextWidth);
            Height = Math.Max(MinimumSize, nextHeight);
            return;
        }

        var heightCandidate = Clamp(Height + deltaY, MinimumSize, maxHeight);
        var widthCandidate = heightCandidate * ratio;
        if (widthCandidate > maxWidth)
        {
            widthCandidate = maxWidth;
            heightCandidate = widthCandidate / ratio;
        }

        Width = Math.Max(MinimumSize, widthCandidate);
        Height = Math.Max(MinimumSize, heightCandidate);
    }

    public void SetRect(double x, double y, double width, double height, double canvasWidth, double canvasHeight)
    {
        var nextWidth = Math.Max(MinimumSize, width);
        var nextHeight = Math.Max(MinimumSize, height);
        nextWidth = Math.Min(nextWidth, canvasWidth);
        nextHeight = Math.Min(nextHeight, canvasHeight);
        var nextX = Clamp(x, 0, canvasWidth - nextWidth);
        var nextY = Clamp(y, 0, canvasHeight - nextHeight);
        X = nextX;
        Y = nextY;
        Width = nextWidth;
        Height = nextHeight;
        _aspectRatio = CalculateAspectRatio(nextWidth, nextHeight);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static double CalculateAspectRatio(double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return 1;
        }

        return width / height;
    }
}
