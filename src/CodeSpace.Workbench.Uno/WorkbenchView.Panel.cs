using CodeSpace.Languages;
using CodeSpace.Controls.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace CodeSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private void BuildPanel()
    {
        var grid = new Grid { Background = WorkbenchColors.Background }; grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(33) }); grid.RowDefinitions.Add(new RowDefinition());
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        foreach (var name in new[] { "PROBLEMS", "OUTPUT", "DEBUG CONSOLE", "TERMINAL" })
        {
            var button = new WorkbenchButton(name, () => { _panelTab = name; BuildPanel(); }) { Padding = new Thickness(10, 6, 10, 6), BorderThickness = new Thickness(0, 0, 0, name == _panelTab ? 1 : 0), BorderBrush = WorkbenchColors.Accent };
            button.Content = WorkbenchColors.Text(name, 11, name == _panelTab ? "#ffffff" : "#9d9d9d"); tabs.Children.Add(button);
        }
        header.Children.Add(tabs); var close = new WorkbenchButton("×", () => _layout.SetPanel(visible: false), "Close panel"); header.Children.Add(close); SetColumn(close, 1); grid.Children.Add(header);
        if (_panelBody.Parent is ScrollViewer old) old.Content = null;
        _panelBody.Margin = new Thickness(20, 6, 15, 10); _panelBody.Spacing = 4;
        var scroll = new ScrollViewer { Content = _panelBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }; grid.Children.Add(scroll); SetRow(scroll, 1); _panel.Child = grid; RefreshPanelBody();
    }
    private void RefreshPanelBody()
    {
        _panelBody.Children.Clear();
        if (_panelTab == "OUTPUT") { foreach (var line in _output.TakeLast(200)) _panelBody.Children.Add(WorkbenchColors.Text(line, 12, "#b5b5b5")); }
        else if (_panelTab == "PROBLEMS")
        {
            if (_activeEditor is null) { _panelBody.Children.Add(WorkbenchColors.Text("No active document.")); return; }
            var session = _activeEditor.Session; var diagnostics = LanguageServices.Diagnostics(session.Buffer, session.File.Path).Concat(ExtensionDiagnosticsFor(session.File.Path)).ToArray();
            _panelBody.Children.Add(WorkbenchColors.Text("Lexical bracket checks and diagnostics from activated extensions", 11, "#969696"));
            foreach (var diagnostic in diagnostics.Take(200)) _panelBody.Children.Add(new WorkbenchButton("×  " + diagnostic.Message + "   " + Path.GetFileName(session.File.Path) + ":" + (diagnostic.Line + 1), () => Open(session.File.Path, diagnostic.Line, diagnostic.Character)));
            if (diagnostics.Length == 0) _panelBody.Children.Add(WorkbenchColors.Text("No reported problems in the active document."));
        }
        else if (_panelTab == "DEBUG CONSOLE") _panelBody.Children.Add(WorkbenchColors.Text("No debug session. A debug adapter has not been connected.", 12, "#969696"));
        else
        {
            _panelBody.Children.Add(WorkbenchColors.Text("WORKSPACE SHELL  ·  virtual files only; not an operating-system terminal", 11, "#969696"));
            foreach (var line in _shellOutput.TakeLast(100)) _panelBody.Children.Add(WorkbenchColors.Text(line, 12));
            var prompt = new Grid(); prompt.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); prompt.ColumnDefinitions.Add(new ColumnDefinition());
            prompt.Children.Add(WorkbenchColors.Text("workspace $ ", 12, "#65ad80"));
            var input = new TextBox { FontSize = 12, MinHeight = 24, Background = WorkbenchColors.Background, Foreground = WorkbenchColors.Foreground, BorderThickness = new Thickness(0), Padding = new Thickness(0), PlaceholderText = "help" }; AutomationProperties.SetName(input, "Workspace shell command"); prompt.Children.Add(input); SetColumn(input, 1);
            input.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { RunShell(input.Text); input.Text = ""; e.Handled = true; } }; _panelBody.Children.Add(prompt);
        }
    }
    private readonly List<string> _shellOutput = [];
    private void RunShell(string command)
    {
        _shellOutput.Add("workspace $ " + command); var parts = command.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries); if (parts.Length == 0) return;
        var arg = parts.Length > 1 ? parts[1] : "";
        switch (parts[0])
        {
            case "help": _shellOutput.Add("help · ls · pwd · cat <path> · open <path> · echo <text> · clear"); break;
            case "pwd": _shellOutput.Add("/" + _workspace.Name); break;
            case "ls": _shellOutput.AddRange(_workspace.Files.Keys); break;
            case "cat": if (_workspace.Files.TryGetValue(arg, out var file)) _shellOutput.AddRange(file.Buffer.ToString().Split('\n').Take(200)); else _shellOutput.Add("File not found: " + arg); break;
            case "open": Open(arg); break;
            case "echo": _shellOutput.Add(arg); break;
            case "clear": _shellOutput.Clear(); break;
            default: _shellOutput.Add("Unsupported workspace command. This is not a process shell. Type help."); break;
        }
        RefreshPanelBody();
    }
    public void Log(string text) { _output.Add(text); if (_output.Count > 500) _output.RemoveAt(0); if (_panelTab == "OUTPUT") RefreshPanelBody(); }
    public void Notify(string text) { _message.Text = text; Log(text); StatusChanged?.Invoke(this, text); }
    private void UpdateStatus()
    {
        var session = _activeEditor?.Session;
        _statusLeft.Text = "◇  " + _workspace.Name.ToLowerInvariant() + "    " + (_platform.IsBrowser ? "Browser" : "Desktop");
        if (session is null) { _statusRight.Text = "UTF-8    CodeSpace"; return; }
        var position = session.Buffer.PositionAt(session.Primary.Active);
        _statusRight.Text = $"Ln {position.Line + 1}, Col {position.Character + 1}" + (session.Selections.Count > 1 ? $" ({session.Selections.Count} cursors)" : "") + $"     Spaces: {session.TabSize}     UTF-8     {(session.Eol == "\r\n" ? "CRLF" : "LF")}     {LanguageCatalog.ForPath(session.File.Path).DisplayName}";
    }
}
