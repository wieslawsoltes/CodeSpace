using Microsoft.UI.Xaml.Controls;

namespace CodeSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private int _modalDepth;
    public object InteractionDiagnostics => new
    {
        quickPickOpen = _quickPick.IsOpen,
        quickPickReady = _quickPick.IsInputFocused,
        modalOpen = _modalDepth != 0
    };

    private async Task<ContentDialogResult> ShowWorkbenchDialogAsync(ContentDialog dialog)
    {
        _modalDepth++;
        try { return await dialog.ShowAsync(); }
        finally
        {
            // Restore only after the modal has finished closing, never to a collapsed
            // palette input or over a newer picker opened by an asynchronous command.
            _modalDepth--;
            if (!_disposed && _modalDepth == 0 && !_quickPick.IsOpen)
                _activeEditor?.FocusEditor();
        }
    }
}
