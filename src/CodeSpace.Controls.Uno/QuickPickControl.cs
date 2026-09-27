using CodeSpace.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CodeSpace.Controls.Uno;

public sealed record QuickPickItem(string Label, string Detail, string Shortcut, Action Execute);
public sealed class QuickPickControl : Grid
{
    private readonly TextBox _input;
    private readonly StackPanel _results;
    private readonly TextBlock _hint;
    private readonly DispatcherTimer _focusTimer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private QuickPickItem[] _items = [];
    private QuickPickItem[] _filtered = [];
    private int _selected, _focusAttempts;
    private Action<string>? _submit;
    public event EventHandler? Dismissed;
    public QuickPickControl()
    {
        Visibility = Visibility.Collapsed; Background = WorkbenchColors.Brush("#30000000");
        var panel = new StackPanel();
        _input = new TextBox { Margin = new Thickness(6), Padding = new Thickness(8, 6, 8, 6), Background = WorkbenchColors.Brush("#313131"), Foreground = WorkbenchColors.Foreground, BorderBrush = WorkbenchColors.Accent, BorderThickness = new Thickness(1), FontSize = 13, CornerRadius = new CornerRadius(0) };
        AutomationProperties.SetName(_input, "Command palette input"); panel.Children.Add(_input);
        _results = new StackPanel(); panel.Children.Add(new ScrollViewer { Content = _results, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _hint = WorkbenchColors.Text("↑↓ to navigate    Enter to select    Esc to dismiss", 11, "#9d9d9d"); _hint.Margin = new Thickness(12, 8, 12, 8); panel.Children.Add(_hint);
        var card = new Border { Child = panel, Width = 640, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 8, 16, 0), Background = WorkbenchColors.Brush("#252526"), BorderBrush = WorkbenchColors.Brush("#454545"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
        Children.Add(card); SizeChanged += (_, _) => card.Width = Math.Max(200, Math.Min(640, ActualWidth - 32));
        _input.TextChanged += (_, _) => { _selected = 0; Refresh(); };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) { Hide(); e.Handled = true; }
            else if (e.Key == VirtualKey.Down) { _selected = Math.Min(_filtered.Length - 1, _selected + 1); DrawRows(); e.Handled = true; }
            else if (e.Key == VirtualKey.Up) { _selected = Math.Max(0, _selected - 1); DrawRows(); e.Handled = true; }
            else if (e.Key == VirtualKey.Enter) { var submit = _submit; var text = _input.Text; if (submit is not null) { Hide(); submit(text); } else Choose(_selected); e.Handled = true; }
        };
        _focusTimer.Tick += (_, _) =>
        {
            // A collapsed control can report a stale location before its first layout.
            // Focus only after layout so Uno can place its native text-input bridge correctly.
            if (Visibility != Visibility.Visible || ++_focusAttempts > 20) { _focusTimer.Stop(); return; }
            if (_input.ActualWidth > 10 && _input.ActualHeight > 10 && _input.Focus(FocusState.Programmatic))
            { _input.Select(_input.Text.Length, 0); _focusTimer.Stop(); }
        };
        Unloaded += (_, _) => _focusTimer.Stop();
        PointerPressed += (_, e) => { if (ReferenceEquals(e.OriginalSource, this)) Hide(); };
    }
    public void Show(IEnumerable<QuickPickItem> items, string placeholder = "Type a command", string initialText = "")
    {
        _focusTimer.Stop(); _submit = null; _items = items.ToArray(); _input.PlaceholderText = placeholder; _input.Text = initialText;
        Visibility = Visibility.Visible; _selected = 0; Refresh(); _focusAttempts = 0; _focusTimer.Start();
    }
    public void Prompt(string placeholder, Action<string> submit, string initialText = "")
    {
        Show([], placeholder, initialText); _submit = submit; _hint.Text = "Enter to confirm    Esc to cancel";
    }
    public void Hide() { _focusTimer.Stop(); Visibility = Visibility.Collapsed; _submit = null; Dismissed?.Invoke(this, EventArgs.Empty); }
    private void Refresh()
    {
        _filtered = _items.Select(i => (Item: i, Score: CommandRegistry.FuzzyScore(i.Label, _input.Text))).Where(x => x.Score >= 0).OrderByDescending(x => x.Score).Take(60).Select(x => x.Item).ToArray();
        _hint.Text = $"{_filtered.Length} results    ↑↓ to navigate    Enter to select    Esc to dismiss"; DrawRows();
    }
    private void DrawRows()
    {
        _results.Children.Clear();
        for (var i = 0; i < _filtered.Length; i++)
        {
            var index = i; var item = _filtered[i]; var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = WorkbenchColors.Text(item.Label); row.Children.Add(label);
            var shortcut = WorkbenchColors.Text(item.Shortcut, 11, "#aaaaaa"); shortcut.Margin = new Thickness(15, 0, 0, 0); row.Children.Add(shortcut); SetColumn(shortcut, 1);
            var button = new WorkbenchButton(item.Label, () => Choose(index), item.Detail) { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 5, 12, 5), Background = WorkbenchColors.Brush(i == _selected ? "#04395e" : "#00000000") };
            _results.Children.Add(button);
        }
    }
    private void Choose(int index)
    {
        if (index < 0 || index >= _filtered.Length) return; var action = _filtered[index].Execute; Hide(); action();
    }
}
