using CodeSpace.Controls.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace CodeSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private UIElement BuildTitleBar()
    {
        var bar = new Grid { Background = WorkbenchColors.Brush("#181818"), BorderBrush = WorkbenchColors.Border, BorderThickness = new Thickness(0, 0, 0, 1) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var menu = new StackPanel { Orientation = Orientation.Horizontal };
        var brand = WorkbenchColors.Text("⌘", 21, "#4daafc"); brand.Margin = new Thickness(13, 0, 12, 0); menu.Children.Add(brand);
        var menus = new Dictionary<string, string[]>
        {
            ["File"] = ["workbench.action.files.newUntitledFile", "workbench.action.files.openFile", "workbench.action.files.save", "codespace.exportWorkspace", "codespace.importWorkspace", "workbench.action.closeActiveEditor"],
            ["Edit"] = ["undo", "redo", "editor.action.clipboardCutAction", "editor.action.clipboardCopyAction", "editor.action.clipboardPasteAction", "actions.find", "editor.action.startFindReplaceAction"],
            ["Selection"] = ["editor.action.selectAll", "editor.action.addSelectionToNextFindMatch", "editor.action.copyLinesDownAction"],
            ["View"] = ["workbench.action.showCommands", "workbench.action.toggleSidebarVisibility", "workbench.action.togglePanel", "workbench.action.splitEditor", "editor.action.toggleMinimap", "editor.action.toggleRenderWhitespace"],
            ["Go"] = ["workbench.action.quickOpen", "workbench.action.gotoLine", "workbench.action.gotoSymbol"],
            ["Run"] = ["workbench.view.debug", "codespace.extensions.probe"],
            ["Terminal"] = ["workbench.action.terminal.toggleTerminal"],
            ["Help"] = ["codespace.help", "codespace.about"]
        };
        foreach (var entry in menus)
        {
            var button = new WorkbenchButton(entry.Key) { Height = 34, Padding = new Thickness(7, 0, 7, 0) };
            button.Click += (_, _) =>
            {
                var flyout = new MenuFlyout();
                foreach (var id in entry.Value)
                {
                    var command = _commands.All.FirstOrDefault(c => c.Id == id); if (command is null) continue;
                    var item = new MenuFlyoutItem { Text = command.Title + (command.Shortcut.Length > 0 ? "   " + command.Shortcut : "") };
                    item.Click += (_, _) => Execute(id); flyout.Items.Add(item);
                }
                flyout.ShowAt(button);
            };
            menu.Children.Add(button);
        }
        bar.Children.Add(menu);
        var search = new WorkbenchButton("⌕    CodeSpace", () => Execute("workbench.action.quickOpen"), "Search files (Ctrl+P)") { Height = 25, MaxWidth = 470, MinWidth = 110, Margin = new Thickness(24, 3, 20, 3), BorderThickness = new Thickness(1), BorderBrush = WorkbenchColors.Brush("#3c3c3c"), CornerRadius = new CornerRadius(5), Background = WorkbenchColors.Brush("#222222"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetAutomationId(search, "CommandCenter"); bar.Children.Add(search); SetColumn(search, 1);
        var layouts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
        layouts.Children.Add(new WorkbenchButton("▥", () => Execute("workbench.action.toggleSidebarVisibility"), "Toggle side bar")); layouts.Children.Add(new WorkbenchButton("▤", () => Execute("workbench.action.togglePanel"), "Toggle panel")); layouts.Children.Add(new WorkbenchButton("◫", () => Execute("workbench.action.splitEditor"), "Split editor"));
        bar.Children.Add(layouts); SetColumn(layouts, 2);
        SizeChanged += (_, _) => menu.Visibility = ActualWidth < 760 ? Visibility.Collapsed : Visibility.Visible;
        return bar;
    }
    private UIElement BuildStatusBar()
    {
        var bar = new Grid { Background = WorkbenchColors.Brush("#181818"), BorderBrush = WorkbenchColors.Border, BorderThickness = new Thickness(0, 1, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var remote = new WorkbenchButton("><", () => Notify("Local workspace. Remote development is not connected."), "Workspace location") { Width = 35, Background = WorkbenchColors.Accent, Padding = new Thickness(8, 0, 8, 0) }; bar.Children.Add(remote);
        _statusLeft.Margin = new Thickness(10, 0, 12, 0); bar.Children.Add(_statusLeft); SetColumn(_statusLeft, 1);
        _message.Margin = new Thickness(4, 0, 8, 0); bar.Children.Add(_message); SetColumn(_message, 2);
        _statusRight.Margin = new Thickness(8, 0, 14, 0); bar.Children.Add(_statusRight); SetColumn(_statusRight, 3); return bar;
    }
}
