using CodeSpace.Core;
using CodeSpace.Editor;
using CodeSpace.Languages;
using CodeSpace.Rendering.Skia;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;

namespace CodeSpace.Controls.Uno;

/// <summary>A custom-rendered editor. The tiny Uno text input bridge never owns the document or its history.</summary>
public sealed class CodeEditorControl : Grid, IDisposable
{
    private sealed class EditorSurface(CodeEditorControl owner) : SKCanvasElement
    {
        protected override void RenderOverride(SKCanvas canvas, Size area) => owner.Renderer.Draw(canvas, new SKRect(0, 0, (float)area.Width, (float)area.Height), owner.Session, owner.Viewport);
    }
    private readonly EditorSurface _surface;
    private readonly EditorInputBridge _input;
    private readonly DispatcherTimer _caret = new() { Interval = TimeSpan.FromMilliseconds(530) };
    private readonly DispatcherTimer _findTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly Border _findBox;
    private readonly TextBox _findInput, _replaceInput;
    private readonly TextBlock _findCount;
    private bool _ignoreText;
    private bool _dragging, _scrollDragging;
    private bool _disposed;
    private int _anchor;
    private bool _findCase, _findRegex;
    public EditorSession Session { get; }
    public EditorViewport Viewport { get; } = new();
    public EditorRenderer Renderer { get; } = new();
    public event EventHandler<string>? CommandRequested;
    public event EventHandler<string>? Error;
    public event EventHandler? CaretChanged;
    public CodeEditorControl(EditorSession session)
    {
        Session = session; Background = WorkbenchColors.Background;
        _surface = new EditorSurface(this); Children.Add(_surface);
        _input = new EditorInputBridge { KeyProcessor = InputKeyDown, Width = 1, Height = 1, MinWidth = 0, MinHeight = 0, Opacity = 0.02, AcceptsReturn = true, IsSpellCheckEnabled = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(0), BorderThickness = new Thickness(0), TabIndex = 0 };
        AutomationProperties.SetName(_input, "Code editor input: " + session.File.Path); AutomationProperties.SetHelpText(_input, "Custom editor input. Full document screen-reader navigation is not yet implemented."); Children.Add(_input);
        _input.TextChanged += (_, _) => { if (_ignoreText || _input.Text.Length == 0) return; var text = _input.Text; _ignoreText = true; _input.Text = ""; _ignoreText = false; Session.Insert(text); };
        _input.GotFocus += (_, _) => { Viewport.Focused = true; Viewport.CaretVisible = true; _caret.Start(); _surface.Invalidate(); };
        _input.LostFocus += (_, _) => { Viewport.Focused = false; _caret.Stop(); _surface.Invalidate(); };
        _caret.Tick += (_, _) => { Viewport.CaretVisible = !Viewport.CaretVisible; _surface.Invalidate(); };
        _surface.PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(_surface); if (!point.Properties.IsLeftButtonPressed) return; FocusEditor();
            var x = (float)point.Position.X; var y = (float)point.Position.Y;
            if (x >= ActualWidth - 12) { _scrollDragging = true; ScrollFromPointer(y); }
            else if (Viewport.ShowMinimap && x > ActualWidth - Viewport.MinimapWidth) ScrollFromPointer(y);
            else if (x < 22)
            {
                var line = Session.Buffer.PositionAt(Renderer.HitTest(Session, Viewport, x, y)).Line;
                if (!Viewport.Breakpoints.Add(line)) Viewport.Breakpoints.Remove(line); _surface.Invalidate();
            }
            else
            {
                var offset = Renderer.HitTest(Session, Viewport, x, y); _anchor = KeyModifiers.Shift ? Session.Primary.Anchor : offset;
                Session.Select(_anchor, offset, KeyModifiers.Alt); _dragging = true;
            }
            _surface.CapturePointer(e.Pointer); e.Handled = true;
        };
        _surface.PointerMoved += (_, e) =>
        {
            var point = e.GetCurrentPoint(_surface).Position;
            if (_scrollDragging) ScrollFromPointer((float)point.Y);
            else if (_dragging)
            {
                if (point.Y < 0) Viewport.ScrollY = Math.Max(0, Viewport.ScrollY - Viewport.LineHeight);
                if (point.Y > ActualHeight) Viewport.ScrollY += Viewport.LineHeight;
                Session.Select(_anchor, Renderer.HitTest(Session, Viewport, (float)point.X, (float)point.Y));
            }
        };
        _surface.PointerReleased += (_, e) => { _dragging = _scrollDragging = false; _surface.ReleasePointerCapture(e.Pointer); e.Handled = true; };
        _surface.PointerCaptureLost += (_, _) => _dragging = _scrollDragging = false;
        _surface.DoubleTapped += (_, e) => { var point = e.GetPosition(_surface); Session.SelectWord(Renderer.HitTest(Session, Viewport, (float)point.X, (float)point.Y)); e.Handled = true; };
        _surface.PointerWheelChanged += (_, e) =>
        {
            var delta = e.GetCurrentPoint(_surface).Properties.MouseWheelDelta;
            if (KeyModifiers.Control) { Viewport.FontSize = Math.Clamp(Viewport.FontSize + Math.Sign(delta), 9, 32); Renderer.Invalidate(); }
            else if (KeyModifiers.Shift) Viewport.ScrollX = Math.Max(0, Viewport.ScrollX - delta / 120f * 60);
            else Viewport.ScrollY = Math.Max(0, Viewport.ScrollY - delta / 120f * Viewport.LineHeight * 3);
            _surface.Invalidate(); e.Handled = true;
        };
        var findPanel = new StackPanel { Spacing = 4, Padding = new Thickness(6) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        _findInput = Input("Find", 200); row.Children.Add(_findInput);
        row.Children.Add(new WorkbenchButton("Aa", () => { _findCase = !_findCase; UpdateFind(); }, "Toggle match case"));
        row.Children.Add(new WorkbenchButton(".*", () => { _findRegex = !_findRegex; UpdateFind(); }, "Toggle regular expression"));
        row.Children.Add(new WorkbenchButton("↑", () => NextMatch(false), "Previous match")); row.Children.Add(new WorkbenchButton("↓", () => NextMatch(true), "Next match"));
        row.Children.Add(new WorkbenchButton("×", HideFind, "Close find")); findPanel.Children.Add(row);
        var replacement = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 }; _replaceInput = Input("Replace", 200); replacement.Children.Add(_replaceInput);
        replacement.Children.Add(new WorkbenchButton("Replace All", () => { try { Session.ReplaceAll(_findInput.Text, _replaceInput.Text, _findCase, _findRegex); UpdateFind(); } catch (Exception e) { Error?.Invoke(this, e.Message); } }));
        _findCount = WorkbenchColors.Text("", 11); replacement.Children.Add(_findCount); findPanel.Children.Add(replacement);
        _findBox = new Border { Child = findPanel, Background = WorkbenchColors.Brush("#252526"), BorderBrush = WorkbenchColors.Border, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 18, 0), Visibility = Visibility.Collapsed }; Children.Add(_findBox);
        _findInput.TextChanged += (_, _) => { _findTimer.Stop(); _findTimer.Start(); };
        _findInput.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { NextMatch(!KeyModifiers.Shift); e.Handled = true; } else if (e.Key == VirtualKey.Escape) { HideFind(); e.Handled = true; } };
        _findTimer.Tick += (_, _) => { _findTimer.Stop(); UpdateFind(); };
        Session.Changed += OnChanged; Session.SelectionChanged += OnSelectionChanged;
        Loaded += (_, _) => { if (Viewport.Focused) _caret.Start(); _surface.Invalidate(); };
        Unloaded += (_, _) => { _caret.Stop(); _findTimer.Stop(); };
        SizeChanged += (_, _) => _surface.Invalidate();
    }
    private static TextBox Input(string placeholder, double width)
    {
        var input = new TextBox { PlaceholderText = placeholder, Width = width, FontSize = 13, MinHeight = 27, Padding = new Thickness(5, 2, 5, 2), Background = WorkbenchColors.Brush("#313131"), Foreground = WorkbenchColors.Foreground, BorderBrush = WorkbenchColors.Brush("#454545"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(0) };
        AutomationProperties.SetName(input, placeholder); return input;
    }
    private void ScrollFromPointer(float y) { Viewport.ScrollY = Math.Max(0, y / Math.Max(1, ActualHeight) * Session.Buffer.LineCount * Viewport.LineHeight - ActualHeight / 2); _surface.Invalidate(); }
    public void FocusEditor() => _input.Focus(FocusState.Programmatic);
    public void InvalidateEditor() { Renderer.Invalidate(); _surface.Invalidate(); }
    public void ShowFind(bool replace = false) { _findBox.Visibility = Visibility.Visible; if (Session.Primary.Length > 0 && Session.Primary.Length < 200) _findInput.Text = Session.SelectedText; _findInput.Focus(FocusState.Programmatic); _findInput.SelectAll(); UpdateFind(); }
    public void HideFind() { _findBox.Visibility = Visibility.Collapsed; Viewport.FindMatches = []; _surface.Invalidate(); FocusEditor(); }
    private void UpdateFind()
    {
        try { Viewport.FindMatches = Session.Find(_findInput.Text, _findCase, _findRegex); _findCount.Text = Viewport.FindMatches.Count + " matches"; }
        catch (Exception e) { Viewport.FindMatches = []; _findCount.Text = "Invalid pattern"; Error?.Invoke(this, e.Message); }
        _surface.Invalidate();
    }
    private void NextMatch(bool forward)
    {
        UpdateFind(); var matches = Viewport.FindMatches; if (matches.Count == 0) return;
        var match = forward ? matches.FirstOrDefault(m => m.Start >= Session.Primary.End && m.Start != Session.Primary.Start) : matches.LastOrDefault(m => m.End <= Session.Primary.Start && m.Start != Session.Primary.Start);
        if (match == default) match = forward ? matches[0] : matches[^1]; Session.Select(match.Start, match.End);
    }
    private void OnChanged(object? sender, DocumentChangedEventArgs e) { if (_findBox.Visibility == Visibility.Visible) { _findTimer.Stop(); _findTimer.Start(); } _surface.Invalidate(); }
    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        Viewport.CaretVisible = true; Renderer.EnsureCaretVisible(Session, Viewport, (float)ActualWidth, (float)ActualHeight); _surface.Invalidate(); CaretChanged?.Invoke(this, EventArgs.Empty);
    }
    public async Task CopyAsync(bool cut = false)
    {
        try
        {
            var text = Session.Primary.Length == 0 ? Session.Buffer.GetLine(Session.Buffer.PositionAt(Session.Primary.Active).Line).Text + Session.Eol : Session.SelectedText;
            var data = new DataPackage(); data.SetText(text); Clipboard.SetContent(data);
            if (cut && Session.Primary.Length > 0) Session.Insert("");
            await Task.CompletedTask;
        }
        catch (Exception e) { Error?.Invoke(this, e.Message); }
    }
    public async Task PasteAsync()
    {
        try { var content = Clipboard.GetContent(); if (content.Contains(StandardDataFormats.Text)) Session.Insert(await content.GetTextAsync()); }
        catch (Exception e) { Error?.Invoke(this, "Clipboard permission: " + e.Message); }
    }
    private void InputKeyDown(KeyRoutedEventArgs e)
    {
        try
        {
            var control = KeyModifiers.Control; var shift = KeyModifiers.Shift;
            if (control)
            {
                switch (e.Key)
                {
                    case VirtualKey.A: Session.SelectAll(); break;
                    case VirtualKey.C: _ = CopyAsync(); break;
                    case VirtualKey.X when shift: CommandRequested?.Invoke(this, "workbench.view.extensions"); break;
                    case VirtualKey.X: _ = CopyAsync(true); break;
                    case VirtualKey.V: _ = PasteAsync(); break;
                    case VirtualKey.Z: if (shift) Session.Redo(); else Session.Undo(); break;
                    case VirtualKey.Y: Session.Redo(); break;
                    case VirtualKey.D: Session.AddNextOccurrence(); break;
                    case VirtualKey.E when shift: CommandRequested?.Invoke(this, "workbench.view.explorer"); break;
                    case VirtualKey.F: if (shift) CommandRequested?.Invoke(this, "workbench.action.findInFiles"); else ShowFind(); break;
                    case VirtualKey.H: ShowFind(true); break;
                    case VirtualKey.P: CommandRequested?.Invoke(this, shift ? "workbench.action.showCommands" : "workbench.action.quickOpen"); break;
                    case VirtualKey.S: CommandRequested?.Invoke(this, shift ? "workbench.action.files.saveAs" : "workbench.action.files.save"); break;
                    case VirtualKey.N: CommandRequested?.Invoke(this, "workbench.action.files.newUntitledFile"); break;
                    case VirtualKey.O: CommandRequested?.Invoke(this, "workbench.action.files.openFile"); break;
                    case VirtualKey.B: CommandRequested?.Invoke(this, "workbench.action.toggleSidebarVisibility"); break;
                    case VirtualKey.J: CommandRequested?.Invoke(this, "workbench.action.togglePanel"); break;
                    case VirtualKey.G: CommandRequested?.Invoke(this, "workbench.action.gotoLine"); break;
                    case VirtualKey.W: CommandRequested?.Invoke(this, "workbench.action.closeActiveEditor"); break;
                    case VirtualKey.Space: CommandRequested?.Invoke(this, "editor.action.triggerSuggest"); break;
                    case VirtualKey.Home: Session.Move(CursorMove.DocumentStart, shift); break;
                    case VirtualKey.End: Session.Move(CursorMove.DocumentEnd, shift); break;
                    case VirtualKey.Left: Session.Move(CursorMove.Left, shift, true); break;
                    case VirtualKey.Right: Session.Move(CursorMove.Right, shift, true); break;
                    default:
                        if ((int)e.Key == 220) CommandRequested?.Invoke(this, "workbench.action.splitEditor");
                        else if ((int)e.Key == 191) Session.ToggleLineComment(LanguageCatalog.ForPath(Session.File.Path).LineComment is "#" ? "#" : "//");
                        else return;
                        break;
                }
                e.Handled = true; return;
            }
            switch (e.Key)
            {
                case VirtualKey.Left: Session.Move(CursorMove.Left, shift); break;
                case VirtualKey.Right: Session.Move(CursorMove.Right, shift); break;
                case VirtualKey.Up: Session.Move(CursorMove.Up, shift); break;
                case VirtualKey.Down: Session.Move(CursorMove.Down, shift); break;
                case VirtualKey.Home: Session.Move(CursorMove.Home, shift); break;
                case VirtualKey.End: Session.Move(CursorMove.End, shift); break;
                case VirtualKey.PageUp: Session.Move(CursorMove.PageUp, shift, pageSize: Math.Max(1, (int)(ActualHeight / Viewport.LineHeight))); break;
                case VirtualKey.PageDown: Session.Move(CursorMove.PageDown, shift, pageSize: Math.Max(1, (int)(ActualHeight / Viewport.LineHeight))); break;
                case VirtualKey.Back: Session.Delete(true); break;
                case VirtualKey.Delete: Session.Delete(false); break;
                case VirtualKey.Enter: Session.InsertNewLine(); break;
                case VirtualKey.Tab: Session.Indent(shift); break;
                case VirtualKey.Escape: HideFind(); Session.Select(Session.Primary.Active, Session.Primary.Active); break;
                case VirtualKey.F1: CommandRequested?.Invoke(this, "workbench.action.showCommands"); break;
                case VirtualKey.F3: NextMatch(!shift); break;
                default: return;
            }
            e.Handled = true;
        }
        catch (Exception exception) { Error?.Invoke(this, exception.Message); e.Handled = true; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _input.KeyProcessor = null; _caret.Stop(); _findTimer.Stop(); Session.Changed -= OnChanged; Session.SelectionChanged -= OnSelectionChanged; Renderer.Dispose();
    }
}
