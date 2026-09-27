using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CodeSpace.Controls.Uno;

/// <summary>
/// Native text/IME input bridge for a separately stored and rendered document.
/// The document processes editing keys before TextBox consumes its own shortcuts.
/// Unhandled character input continues through the platform text-input path.
/// </summary>
public sealed class EditorInputBridge : TextBox
{
    /// <summary>
    /// Synchronous key processor. Set Handled before returning for document commands.
    /// Asynchronous clipboard or dialog work must be started without deferring Handled.
    /// </summary>
    public Action<KeyRoutedEventArgs>? KeyProcessor { get; set; }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (!e.Handled) KeyProcessor?.Invoke(e);
        if (!e.Handled) base.OnKeyDown(e);
    }
}
