using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class FrameCompositionService
{
    private readonly TemplateCatalogService _templates;
    private readonly QrCodeService _qrCodes;
    private readonly FrameOverrideService _frameOverrides;
    private const int PrintCanvasWidth = 1200;
    private const int PrintCanvasHeight = 1800;

    public FrameCompositionService(
        TemplateCatalogService templates,
        QrCodeService qrCodes,
        FrameOverrideService frameOverrides)
    {
        _templates = templates;
        _qrCodes = qrCodes;
        _frameOverrides = frameOverrides;
    }

    public BitmapSource? RenderComposite(SessionState session, out string? error)
    {
        return RenderComposite(session, includeQr: true, out error);
    }

    public BitmapSource? RenderComposite(SessionState session, bool includeQr, out string? error)
    {
        error = null;
        if (session is null)
        {
            error = "Session not available.";
            return null;
        }

        var templateType = session.TemplateType;
        if (string.IsNullOrWhiteSpace(templateType))
        {
            error = "Template type not set.";
            return null;
        }

        var template = _templates.GetTemplate(templateType);
        if (template is null)
        {
            error = $"Template not found: {templateType}";
            KawaiiStudio.App.App.Log($"RENDER_COMPOSITE_ERROR: {error}");
            return null;
        }

        if (template.Canvas.Width <= 0 || template.Canvas.Height <= 0)
        {
            error = $"Template canvas invalid: {templateType}";
            KawaiiStudio.App.App.Log($"RENDER_COMPOSITE_ERROR: {error}");
            return null;
        }

        var width = template.Canvas.Width;
        var height = template.Canvas.Height;

        KawaiiStudio.App.App.Log($"RENDER_COMPOSITE: template={templateType} slots={template.Slots.Count} canvas={width}x{height}");
        var slots = ResolveSlots(session, template);
        KawaiiStudio.App.App.Log($"RENDER_COMPOSITE: resolved slots={slots.Count}");
        var qrSlot = ResolveQrSlot(session, template);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));

            // Draw slots if available
            if (slots.Count > 0)
            {
                for (var i = 0; i < slots.Count; i++)
                {
                    var slotIndex = i + 1;
                    var slot = slots[i];
                    var rect = new Rect(slot.X, slot.Y, slot.Width, slot.Height);
                    DrawSlot(context, session, slotIndex, rect);
                }
            }

            // Always draw frame overlay if frame is set, even if slots aren't configured
            DrawFrameOverlay(context, session, width, height);
            if (includeQr)
            {
                DrawQrOverlay(context, session, qrSlot);
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public BitmapSource? RenderPrintComposite(SessionState session, out string? error)
    {
        return RenderPrintComposite(session, includeQr: true, out error);
    }

    public BitmapSource? RenderPrintComposite(SessionState session, bool includeQr, out string? error)
    {
        error = null;
        var composite = RenderComposite(session, includeQr, out error);
        if (composite is null)
        {
            return null;
        }

        var templateType = session.TemplateType ?? string.Empty;
        if (templateType.StartsWith("2x6", StringComparison.OrdinalIgnoreCase))
        {
            return RenderDuplicatedTwoBySix(composite);
        }

        return RenderScaledComposite(composite, PrintCanvasWidth, PrintCanvasHeight);
    }

    public bool TrySavePrintComposite(SessionState session, string outputPath, out string? error)
    {
        return TrySavePrintComposite(session, outputPath, includeQr: true, out error);
    }

    public bool TrySavePrintComposite(SessionState session, string outputPath, bool includeQr, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            error = "Output path missing.";
            return false;
        }

        var image = RenderPrintComposite(session, includeQr, out error);
        if (image is null)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
        return true;
    }

    public bool TrySaveComposite(SessionState session, string outputPath, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            error = "Output path missing.";
            return false;
        }

        var image = RenderComposite(session, out error);
        if (image is null)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
        return true;
    }

    private static void DrawSlot(DrawingContext context, SessionState session, int slotIndex, Rect rect)
    {
        if (TryGetSlotPhotoPath(session, slotIndex, out var path) && File.Exists(path))
        {
            var image = LoadBitmap(path);
            if (image is not null)
            {
                var brush = new ImageBrush(image)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
                context.DrawRectangle(brush, null, rect);
                return;
            }
        }

        var placeholder = new SolidColorBrush(Color.FromRgb(24, 24, 24));
        var border = new Pen(new SolidColorBrush(Color.FromRgb(64, 64, 64)), 2);
        context.DrawRectangle(placeholder, border, rect);
    }

    private void DrawFrameOverlay(DrawingContext context, SessionState session, int width, int height)
    {
        var framePath = session.Frame?.FilePath;
        if (string.IsNullOrWhiteSpace(framePath))
        {
            KawaiiStudio.App.App.Log($"DRAW_FRAME_OVERLAY: framePath is null or empty, Frame={session.Frame?.Name ?? "null"}");
            return;
        }

        if (!File.Exists(framePath))
        {
            KawaiiStudio.App.App.Log($"DRAW_FRAME_OVERLAY: file does not exist: {framePath}");
            return;
        }

        var overlay = LoadBitmap(framePath);
        if (overlay is not null)
        {
            KawaiiStudio.App.App.Log($"DRAW_FRAME_OVERLAY: drawing frame {session.Frame?.Name} at {width}x{height}, image size={overlay.PixelWidth}x{overlay.PixelHeight}");
            context.DrawImage(overlay, new Rect(0, 0, width, height));
        }
        else
        {
            KawaiiStudio.App.App.Log($"DRAW_FRAME_OVERLAY: failed to load bitmap from {framePath}");
        }
    }

    private void DrawQrOverlay(DrawingContext context, SessionState session, TemplateSlot? qrSlot)
    {
        if (qrSlot is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(session.QrUrl))
        {
            return;
        }

        var payload = ResolveQrPayload(session);
        var qrImage = _qrCodes.Render(payload, 12);
        if (qrImage is null)
        {
            return;
        }

        var qrRect = new Rect(qrSlot.X, qrSlot.Y, qrSlot.Width, qrSlot.Height);
        context.DrawImage(qrImage, qrRect);
    }

    private static string ResolveQrPayload(SessionState session)
    {
        if (!string.IsNullOrWhiteSpace(session.QrUrl))
        {
            return session.QrUrl;
        }

        var sessionId = string.IsNullOrWhiteSpace(session.SessionId) ? "session" : session.SessionId;
        return $"kawaiistudio://{sessionId}";
    }

    private static bool TryGetSlotPhotoPath(SessionState session, int slotIndex, out string path)
    {
        path = string.Empty;
        if (!session.SelectedMapping.TryGetValue(slotIndex, out var photoIndex))
        {
            return false;
        }

        if (photoIndex < 0 || photoIndex >= session.CapturedPhotos.Count)
        {
            return false;
        }

        path = session.CapturedPhotos[photoIndex];
        return !string.IsNullOrWhiteSpace(path);
    }

    private IReadOnlyList<TemplateSlot> ResolveSlots(SessionState session, TemplateDefinition template)
    {
        // Default: Always use template slots from templates.json
        // Frame overrides (.layout.json) are optional - only use if they exist, are valid, and match slot count
        var framePath = session.Frame?.FilePath;
        
        if (string.IsNullOrWhiteSpace(framePath))
        {
            // No frame selected - use template slots from templates.json
            KawaiiStudio.App.App.Log($"RESOLVE_SLOTS: no frame, using template slots={template.Slots.Count} for template={template.Key}");
            return template.Slots;
        }

        // Try to load frame override (.layout.json)
        if (_frameOverrides.TryLoad(framePath, out var overrideDefinition)
            && overrideDefinition is not null
            && overrideDefinition.Slots.Count > 0)
        {
            // Check if override slot count matches template slot count
            if (overrideDefinition.Slots.Count == template.Slots.Count)
            {
                // Override exists and matches - use it
                KawaiiStudio.App.App.Log($"RESOLVE_SLOTS: using frame override from {framePath}, slots={overrideDefinition.Slots.Count}");
                return overrideDefinition.Slots;
            }
            else
            {
                // Override exists but slot count doesn't match - fall back to template
                KawaiiStudio.App.App.Log($"RESOLVE_SLOTS: override slot count mismatch (override={overrideDefinition.Slots.Count}, template={template.Slots.Count}), using template slots from templates.json");
            }
        }
        else
        {
            // No valid override file - use template slots from templates.json
            KawaiiStudio.App.App.Log($"RESOLVE_SLOTS: no .layout.json override found for {framePath}, using template slots={template.Slots.Count} from templates.json");
        }

        // Always fall back to template slots from templates.json
        return template.Slots;
    }

    private TemplateSlot? ResolveQrSlot(SessionState session, TemplateDefinition template)
    {
        var framePath = session.Frame?.FilePath;
        if (!string.IsNullOrWhiteSpace(framePath)
            && _frameOverrides.TryLoad(framePath, out var overrideDefinition)
            && overrideDefinition?.Qr is not null)
        {
            return overrideDefinition.Qr;
        }

        return template.Qr;
    }

    private static BitmapSource? LoadBitmap(string path)
    {
        return ImageCache.GetOrLoad(path);
    }

    private static BitmapSource RenderDuplicatedTwoBySix(BitmapSource source)
    {
        var targetWidth = PrintCanvasWidth;
        var targetHeight = PrintCanvasHeight;
        var halfWidth = targetWidth / 2.0;
        var scaleX = halfWidth / source.PixelWidth;
        var scaleY = targetHeight / source.PixelHeight;
        var scale = Math.Min(scaleX, scaleY);
        var drawWidth = source.PixelWidth * scale;
        var drawHeight = source.PixelHeight * scale;
        var yOffset = (targetHeight - drawHeight) / 2.0;
        var xOffset = (halfWidth - drawWidth) / 2.0;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, targetWidth, targetHeight));
            context.DrawImage(source, new Rect(xOffset, yOffset, drawWidth, drawHeight));
            context.DrawImage(source, new Rect(halfWidth + xOffset, yOffset, drawWidth, drawHeight));
        }

        var bitmap = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource RenderScaledComposite(BitmapSource source, int targetWidth, int targetHeight)
    {
        if (source.PixelWidth == targetWidth && source.PixelHeight == targetHeight)
        {
            return source;
        }

        var scaleX = targetWidth / (double)source.PixelWidth;
        var scaleY = targetHeight / (double)source.PixelHeight;
        var scale = Math.Min(scaleX, scaleY);
        var drawWidth = source.PixelWidth * scale;
        var drawHeight = source.PixelHeight * scale;
        var xOffset = (targetWidth - drawWidth) / 2.0;
        var yOffset = (targetHeight - drawHeight) / 2.0;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, targetWidth, targetHeight));
            context.DrawImage(source, new Rect(xOffset, yOffset, drawWidth, drawHeight));
        }

        var bitmap = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
