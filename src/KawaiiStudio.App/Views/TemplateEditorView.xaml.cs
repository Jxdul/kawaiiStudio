using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using KawaiiStudio.App.ViewModels;

namespace KawaiiStudio.App.Views;

public partial class TemplateEditorView : UserControl
{
    public TemplateEditorView()
    {
        InitializeComponent();
    }

    private void OnSlotDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.DataContext is not TemplateEditorSlotViewModel slot)
        {
            return;
        }

        if (DataContext is not TemplateEditorViewModel viewModel)
        {
            return;
        }

        viewModel.MoveSlot(slot, e.HorizontalChange, e.VerticalChange);
    }

    private void OnSlotResizeDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.DataContext is not TemplateEditorSlotViewModel slot)
        {
            return;
        }

        if (DataContext is not TemplateEditorViewModel viewModel)
        {
            return;
        }

        viewModel.ResizeSlot(slot, e.HorizontalChange, e.VerticalChange);
    }

    private void OnQrDragDelta(object sender, DragDeltaEventArgs e)
    {
        OnSlotDragDelta(sender, e);
    }

    private void OnQrResizeDragDelta(object sender, DragDeltaEventArgs e)
    {
        OnSlotResizeDragDelta(sender, e);
    }
}
