using CodeSpace.Docking;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace CodeSpace.Controls.Uno;

public sealed record TabMoveRequest(string Path, string SourceGroup, string TargetGroup, int Index);
public sealed class EditorTabs : Grid
{
    public event EventHandler<string>? Activated;
    public event EventHandler<string>? Closed;
    public event EventHandler<TabMoveRequest>? Moved;
    public event EventHandler? SplitRequested;
    public EditorTabs(TabGroup group, Func<string, bool> isDirty)
    {
        Height = 35; Background = WorkbenchColors.Sidebar;
        ColumnDefinitions.Add(new ColumnDefinition()); ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        Children.Add(new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled });
        for (var i = 0; i < group.Tabs.Length; i++)
        {
            var path = group.Tabs[i]; var index = i; var active = path == group.ActiveTab;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = System.IO.Path.GetExtension(path) switch { ".cs" => "#", ".json" => "{}", ".md" => "M", ".ts" => "TS", _ => "◇" };
            var name = new WorkbenchButton(icon + "  " + System.IO.Path.GetFileName(path), () => Activated?.Invoke(this, path), path) { Height = 32, Padding = new Thickness(12, 0, 8, 0), MinWidth = 94 };
            row.Children.Add(name);
            row.Children.Add(new WorkbenchButton(isDirty(path) ? "●" : "×", () => Closed?.Invoke(this, path), "Close " + path) { Width = 26, Height = 32, Padding = new Thickness(4, 0, 4, 0) });
            var tab = new Border { Child = row, Background = active ? WorkbenchColors.Background : WorkbenchColors.Sidebar, BorderBrush = active ? WorkbenchColors.Accent : WorkbenchColors.Border, BorderThickness = new Thickness(0, 1, 1, 0), CanDrag = true, AllowDrop = true };
            tab.DragStarting += (_, e) => { e.Data.SetText(group.Id + "\n" + path); e.AllowedOperations = DataPackageOperation.Move; };
            tab.DragOver += (_, e) => { e.AcceptedOperation = DataPackageOperation.Move; e.Handled = true; };
            tab.Drop += async (_, e) => { if (e.DataView.Contains(StandardDataFormats.Text)) { var value = (await e.DataView.GetTextAsync()).Split('\n', 2); if (value.Length == 2) Moved?.Invoke(this, new(value[1], value[0], group.Id, index)); } e.Handled = true; };
            strip.Children.Add(tab);
        }
        var split = new WorkbenchButton("", () => SplitRequested?.Invoke(this, EventArgs.Empty), "Split editor right (Ctrl+\\)") { Content = new VectorIcon { Kind = "split", Width = 28, Height = 28 }, Padding = new Thickness(0) };
        Children.Add(split); SetColumn(split, 1);
    }
}
