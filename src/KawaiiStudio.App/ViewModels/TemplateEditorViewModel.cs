using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class TemplateEditorViewModel : ScreenViewModelBase
{
    private static readonly string[] DefaultTemplateTypes =
    [
        "2x6_4slots",
        "4x6_2slots",
        "4x6_4slots",
        "4x6_6slots"
    ];

    private readonly NavigationService _navigation;
    private readonly TemplateStorageService _storage;
    private readonly TemplateCatalogService _catalog;
    private readonly FrameCatalogService _frameCatalog;
    private readonly FrameOverrideService _frameOverrides;
    private TemplateEditorSlotViewModel? _qrSlot;
    private string? _selectedTemplateType;
    private FrameItem? _selectedFrame;
    private string _statusText = string.Empty;
    private double _templateWidth;
    private double _templateHeight;
    private TemplateDefinition? _baseTemplate;

    public TemplateEditorViewModel(
        NavigationService navigation,
        TemplateStorageService storage,
        TemplateCatalogService catalog,
        FrameCatalogService frameCatalog,
        FrameOverrideService frameOverrides,
        ThemeCatalogService themeCatalog)
        : base(themeCatalog, "template_editor")
    {
        _navigation = navigation;
        _storage = storage;
        _catalog = catalog;
        _frameCatalog = frameCatalog;
        _frameOverrides = frameOverrides;

        SaveCommand = new RelayCommand(SaveTemplate);
        SaveFrameOverrideCommand = new RelayCommand(SaveFrameOverride);
        ClearFrameOverrideCommand = new RelayCommand(ClearFrameOverride);
        ResetCommand = new RelayCommand(ResetTemplate);
        BackCommand = new RelayCommand(() => _navigation.Navigate("staff"));

        LoadTemplateTypes();
    }

    public ObservableCollection<string> TemplateTypes { get; } = new();
    public ObservableCollection<TemplateEditorSlotViewModel> Slots { get; } = new();
    public ObservableCollection<TemplateEditorSlotViewModel> QrSlots { get; } = new();
    public ObservableCollection<FrameItem> Frames { get; } = new();

    public ICommand SaveCommand { get; }
    public ICommand SaveFrameOverrideCommand { get; }
    public ICommand ClearFrameOverrideCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand BackCommand { get; }

    public string? SelectedTemplateType
    {
        get => _selectedTemplateType;
        set
        {
            if (_selectedTemplateType == value)
            {
                return;
            }

            _selectedTemplateType = value;
            OnPropertyChanged();
            LoadTemplate();
        }
    }

    public FrameItem? SelectedFrame
    {
        get => _selectedFrame;
        set
        {
            _selectedFrame = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FramePreviewPath));
            ApplyFrameOverrideIfAvailable();
        }
    }

    public string? FramePreviewPath => SelectedFrame?.FilePath;

    public TemplateEditorSlotViewModel? QrSlot
    {
        get => _qrSlot;
        private set
        {
            _qrSlot = value;
            QrSlots.Clear();
            if (value is not null)
            {
                QrSlots.Add(value);
            }
            OnPropertyChanged();
        }
    }

    public double TemplateWidth
    {
        get => _templateWidth;
        private set
        {
            _templateWidth = value;
            OnPropertyChanged();
        }
    }

    public double TemplateHeight
    {
        get => _templateHeight;
        private set
        {
            _templateHeight = value;
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
        LoadTemplateTypes();
        if (SelectedTemplateType is null && TemplateTypes.Count > 0)
        {
            SelectedTemplateType = TemplateTypes[0];
        }
    }

    public void MoveSlot(TemplateEditorSlotViewModel slot, double deltaX, double deltaY)
    {
        slot.Move(deltaX, deltaY, TemplateWidth, TemplateHeight);
    }

    public void ResizeSlot(TemplateEditorSlotViewModel slot, double deltaX, double deltaY)
    {
        slot.Resize(deltaX, deltaY, TemplateWidth, TemplateHeight);
    }

    private void LoadTemplateTypes()
    {
        var templates = _storage.LoadAll();
        TemplateTypes.Clear();

        foreach (var type in DefaultTemplateTypes)
        {
            TemplateTypes.Add(type);
        }

        foreach (var key in templates.Keys)
        {
            if (!TemplateTypes.Any(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)))
            {
                TemplateTypes.Add(key);
            }
        }
    }

    private void LoadTemplate()
    {
        if (string.IsNullOrWhiteSpace(SelectedTemplateType))
        {
            return;
        }

        var templates = _storage.LoadAll();
        TemplateDefinition? template = null;
        if (templates.TryGetValue(SelectedTemplateType, out var loaded))
        {
            template = loaded;
        }

        if (template is null)
        {
            template = CreateDefaultTemplate(SelectedTemplateType);
        }

        _baseTemplate = template;
        ApplyTemplateDefinition(template);

        LoadFrames();
        StatusText = $"Loaded template {SelectedTemplateType}";
    }

    private void LoadFrames()
    {
        Frames.Clear();
        if (string.IsNullOrWhiteSpace(SelectedTemplateType))
        {
            return;
        }

        var categories = _frameCatalog.Load();
        foreach (var category in categories.Where(category => category.TemplateType.Equals(SelectedTemplateType, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var frame in category.Frames)
            {
                Frames.Add(frame);
            }
        }

        SelectedFrame = Frames.FirstOrDefault();
    }

    private void SaveTemplate()
    {
        if (string.IsNullOrWhiteSpace(SelectedTemplateType))
        {
            StatusText = "Select a template before saving.";
            return;
        }

        var slots = Slots
            .OrderBy(slot => slot.Index)
            .Select(slot => new TemplateSlot(
                (int)Math.Round(slot.X),
                (int)Math.Round(slot.Y),
                (int)Math.Round(slot.Width),
                (int)Math.Round(slot.Height)))
            .ToList();

        var qr = QrSlot is null
            ? null
            : new TemplateSlot(
                (int)Math.Round(QrSlot.X),
                (int)Math.Round(QrSlot.Y),
                (int)Math.Round(QrSlot.Width),
                (int)Math.Round(QrSlot.Height));

        var template = new TemplateDefinition(
            SelectedTemplateType,
            new TemplateCanvas((int)Math.Round(TemplateWidth), (int)Math.Round(TemplateHeight)),
            slots,
            qr);

        _baseTemplate = template;
        _storage.SaveTemplate(template);
        _catalog.Reload();
        StatusText = $"Saved template {SelectedTemplateType}";
    }

    private void SaveFrameOverride()
    {
        if (SelectedFrame is null)
        {
            StatusText = "Select a frame before saving overrides.";
            return;
        }

        var slots = Slots
            .OrderBy(slot => slot.Index)
            .Select(slot => new TemplateSlot(
                (int)Math.Round(slot.X),
                (int)Math.Round(slot.Y),
                (int)Math.Round(slot.Width),
                (int)Math.Round(slot.Height)))
            .ToList();

        var qr = QrSlot is null
            ? null
            : new TemplateSlot(
                (int)Math.Round(QrSlot.X),
                (int)Math.Round(QrSlot.Y),
                (int)Math.Round(QrSlot.Width),
                (int)Math.Round(QrSlot.Height));

        var definition = new FrameOverrideDefinition(slots, qr);
        _frameOverrides.Save(SelectedFrame.FilePath, definition);
        StatusText = $"Saved override for {SelectedFrame.Name}";
    }

    private void ClearFrameOverride()
    {
        if (SelectedFrame is null)
        {
            StatusText = "Select a frame before clearing overrides.";
            return;
        }

        if (_frameOverrides.Delete(SelectedFrame.FilePath))
        {
            StatusText = $"Cleared override for {SelectedFrame.Name}";
        }
        else
        {
            StatusText = $"No override to clear for {SelectedFrame.Name}";
        }

        if (_baseTemplate is not null)
        {
            ApplyTemplateDefinition(_baseTemplate);
        }
    }

    private void ResetTemplate()
    {
        if (string.IsNullOrWhiteSpace(SelectedTemplateType))
        {
            return;
        }

        var template = CreateDefaultTemplate(SelectedTemplateType);
        _baseTemplate = template;
        ApplyTemplateDefinition(template);

        StatusText = "Reset slots to defaults.";
    }

    private void ApplyFrameOverrideIfAvailable()
    {
        if (SelectedFrame is null)
        {
            return;
        }

        if (_frameOverrides.TryLoad(SelectedFrame.FilePath, out var definition) && definition is not null)
        {
            if (_baseTemplate is not null && definition.Slots.Count != _baseTemplate.Slots.Count)
            {
                StatusText = $"Override slot count mismatch for {SelectedFrame.Name}";
                ApplyTemplateDefinition(_baseTemplate);
                return;
            }

            ApplyOverrideDefinition(definition);
            StatusText = $"Loaded override for {SelectedFrame.Name}";
            return;
        }

        if (_baseTemplate is not null)
        {
            ApplyTemplateDefinition(_baseTemplate);
        }

        StatusText = $"No override found for {SelectedFrame.Name}";
    }

    private void ApplyTemplateDefinition(TemplateDefinition template)
    {
        TemplateWidth = template.Canvas.Width;
        TemplateHeight = template.Canvas.Height;

        Slots.Clear();
        var slotIndex = 1;
        foreach (var slot in template.Slots)
        {
            Slots.Add(new TemplateEditorSlotViewModel(slotIndex, false, slot.X, slot.Y, slot.Width, slot.Height));
            slotIndex++;
        }

        if (template.Qr is null)
        {
            var qrSize = Math.Min(TemplateWidth, TemplateHeight) * 0.12;
            var qrX = TemplateWidth - qrSize - 32;
            var qrY = 32;
            QrSlot = new TemplateEditorSlotViewModel(0, true, qrX, qrY, qrSize, qrSize);
        }
        else
        {
            QrSlot = new TemplateEditorSlotViewModel(0, true, template.Qr.X, template.Qr.Y, template.Qr.Width, template.Qr.Height);
        }
    }

    private void ApplyOverrideDefinition(FrameOverrideDefinition definition)
    {
        Slots.Clear();
        var slotIndex = 1;
        foreach (var slot in definition.Slots)
        {
            Slots.Add(new TemplateEditorSlotViewModel(slotIndex, false, slot.X, slot.Y, slot.Width, slot.Height));
            slotIndex++;
        }

        if (definition.Qr is not null)
        {
            QrSlot = new TemplateEditorSlotViewModel(0, true, definition.Qr.X, definition.Qr.Y, definition.Qr.Width, definition.Qr.Height);
        }
    }

    private static TemplateDefinition CreateDefaultTemplate(string templateType)
    {
        var canvas = templateType.StartsWith("2x6", StringComparison.OrdinalIgnoreCase)
            ? new TemplateCanvas(600, 1800)
            : new TemplateCanvas(1200, 1800);

        var slotCount = templateType switch
        {
            "2x6_4slots" => 4,
            "4x6_2slots" => 2,
            "4x6_4slots" => 4,
            "4x6_6slots" => 6,
            _ => 4
        };

        var slots = BuildDefaultSlots(canvas.Width, canvas.Height, slotCount);
        var qrSize = Math.Min(canvas.Width, canvas.Height) * 0.12;
        var qr = new TemplateSlot(canvas.Width - (int)qrSize - 32, 32, (int)qrSize, (int)qrSize);
        return new TemplateDefinition(templateType, canvas, slots, qr);
    }

    private static System.Collections.Generic.List<TemplateSlot> BuildDefaultSlots(int width, int height, int count)
    {
        var slots = new System.Collections.Generic.List<TemplateSlot>();
        if (count <= 0)
        {
            return slots;
        }

        var padding = 40;
        var columns = count switch
        {
            2 => 1,
            4 => 2,
            6 => 2,
            _ => 1
        };

        var rows = (int)Math.Ceiling(count / (double)columns);
        var slotWidth = (width - padding * (columns + 1)) / columns;
        var slotHeight = (height - padding * (rows + 1)) / rows;

        var index = 0;
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < columns; col++)
            {
                if (index >= count)
                {
                    return slots;
                }

                var x = padding + col * (slotWidth + padding);
                var y = padding + row * (slotHeight + padding);
                slots.Add(new TemplateSlot(x, y, slotWidth, slotHeight));
                index++;
            }
        }

        return slots;
    }
}
