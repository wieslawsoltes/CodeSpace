using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;
using Windows.Foundation;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;

namespace CodeSpace.Controls.Uno;

public static class WorkbenchColors
{
    public static SolidColorBrush Brush(string color)
    {
        var c = SKColor.Parse(color); return new SolidColorBrush(Windows.UI.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue));
    }
    public static SolidColorBrush Background => Brush("#1f1f1f");
    public static SolidColorBrush Sidebar => Brush("#181818");
    public static SolidColorBrush Foreground => Brush("#cccccc");
    public static SolidColorBrush Muted => Brush("#9d9d9d");
    public static SolidColorBrush Border => Brush("#2b2b2b");
    public static SolidColorBrush Accent => Brush("#0078d4");
    public static TextBlock Text(string text, double size = 13, string color = "#cccccc") => new() { Text = text, FontSize = size, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
}
public static class KeyModifiers
{
    public static bool Down(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
    public static bool Control => Down(VirtualKey.Control) || Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows);
    public static bool Shift => Down(VirtualKey.Shift);
    public static bool Alt => Down(VirtualKey.Menu);
}
public sealed class WorkbenchButton : Button
{
    public WorkbenchButton(string text, Action? clicked = null, string? tooltip = null)
    {
        Content = WorkbenchColors.Text(text); Background = WorkbenchColors.Brush("#00000000"); Foreground = WorkbenchColors.Foreground;
        BorderThickness = new Thickness(0); CornerRadius = new CornerRadius(0); Padding = new Thickness(8, 3, 8, 3); MinWidth = 0; MinHeight = 0;
        HorizontalContentAlignment = HorizontalAlignment.Left; VerticalContentAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(this, tooltip ?? text); if (tooltip is not null) ToolTipService.SetToolTip(this, tooltip);
        if (clicked is not null) Click += (_, _) => clicked();
    }
}
public sealed class DockSplitter : Border
{
    private bool _dragging;
    private Point _previous;
    public event EventHandler<double>? Delta;
    public DockSplitter(bool vertical = true)
    {
        Background = WorkbenchColors.Border;
        if (vertical) Width = 4; else Height = 4;
        PointerEntered += (_, _) => Background = WorkbenchColors.Accent;
        PointerExited += (_, _) => { if (!_dragging) Background = WorkbenchColors.Border; };
        PointerPressed += (_, e) => { _dragging = true; _previous = e.GetCurrentPoint(null).Position; CapturePointer(e.Pointer); e.Handled = true; };
        PointerMoved += (_, e) =>
        {
            if (!_dragging) return; var point = e.GetCurrentPoint(null).Position;
            Delta?.Invoke(this, vertical ? point.X - _previous.X : point.Y - _previous.Y); _previous = point; e.Handled = true;
        };
        PointerReleased += (_, e) => { _dragging = false; ReleasePointerCapture(e.Pointer); Background = WorkbenchColors.Border; e.Handled = true; };
        PointerCaptureLost += (_, _) => { _dragging = false; Background = WorkbenchColors.Border; };
    }
}

public sealed class VectorIcon : SKCanvasElement
{
    public string Kind { get; set; } = "files";
    public string Color { get; set; } = "#a7a7a7";
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        canvas.Save(); canvas.Translate((float)(area.Width - 24) / 2, (float)(area.Height - 24) / 2);
        using var p = new SKPaint { Color = SKColor.Parse(Color), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.45f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        void Line(float x1, float y1, float x2, float y2) => canvas.DrawLine(x1, y1, x2, y2, p);
        switch (Kind)
        {
            case "files": canvas.DrawRect(7, 5, 13, 16, p); Line(4, 17, 4, 2); Line(4, 2, 16, 2); break;
            case "search": canvas.DrawCircle(10, 10, 7, p); Line(15, 15, 22, 22); break;
            case "source": canvas.DrawCircle(7, 4, 2.5f, p); canvas.DrawCircle(7, 20, 2.5f, p); canvas.DrawCircle(18, 7, 2.5f, p); Line(7, 7, 7, 17); using (var path = new SKPath()) { path.MoveTo(7, 16); path.CubicTo(7, 12, 18, 16, 18, 10); canvas.DrawPath(path, p); } break;
            case "debug": using (var path = new SKPath()) { path.MoveTo(5, 3); path.LineTo(20, 12); path.LineTo(5, 21); path.Close(); canvas.DrawPath(path, p); } break;
            case "extensions": canvas.DrawRect(3, 3, 7, 7, p); canvas.DrawRect(3, 14, 7, 7, p); canvas.DrawRect(14, 14, 7, 7, p); canvas.Save(); canvas.RotateDegrees(15, 17, 6); canvas.DrawRect(14, 2, 7, 7, p); canvas.Restore(); break;
            case "settings": canvas.DrawCircle(12, 12, 7, p); canvas.DrawCircle(12, 12, 3, p); for (var i = 0; i < 8; i++) { var a = i * MathF.PI / 4; Line(12 + 8 * MathF.Cos(a), 12 + 8 * MathF.Sin(a), 12 + 10 * MathF.Cos(a), 12 + 10 * MathF.Sin(a)); } break;
            case "account": canvas.DrawCircle(12, 12, 10, p); canvas.DrawCircle(12, 9, 3.5f, p); using (var path = new SKPath()) { path.MoveTo(5, 19); path.CubicTo(7, 12, 17, 12, 19, 19); canvas.DrawPath(path, p); } break;
            case "split": canvas.DrawRect(3, 4, 18, 16, p); Line(12, 4, 12, 20); break;
            default: Line(5, 6, 19, 6); Line(5, 12, 19, 12); Line(5, 18, 19, 18); break;
        }
        canvas.Restore();
    }
}

public sealed class ActivityBar : Grid
{
    private readonly Dictionary<string, Border> _entries = [];
    public event EventHandler<string>? Selected;
    public ActivityBar()
    {
        Width = 48; Background = WorkbenchColors.Sidebar;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new StackPanel(); var bottom = new StackPanel(); Children.Add(top); Children.Add(bottom); SetRow(bottom, 1);
        foreach (var (id, label) in new[] { ("files", "Explorer (Ctrl+Shift+E)"), ("search", "Search (Ctrl+Shift+F)"), ("source", "Source Control"), ("debug", "Run and Debug"), ("extensions", "Extensions (Ctrl+Shift+X)") }) Add(top, id, label);
        Add(bottom, "account", "Workspace information"); Add(bottom, "settings", "Manage / Settings"); Activate("files");
    }
    private void Add(StackPanel panel, string id, string label)
    {
        var icon = new VectorIcon { Kind = id, Width = 44, Height = 46 };
        var button = new WorkbenchButton("", () => { Activate(id); Selected?.Invoke(this, id); }, label) { Content = icon, Padding = new Thickness(0), Width = 46, Height = 48 };
        var border = new Border { BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = WorkbenchColors.Brush("#00000000"), Child = button };
        _entries[id] = border; panel.Children.Add(border);
    }
    public void Activate(string id)
    {
        foreach (var entry in _entries) { entry.Value.BorderBrush = entry.Key == id ? WorkbenchColors.Accent : WorkbenchColors.Brush("#00000000"); if (entry.Value.Child is Button b && b.Content is VectorIcon icon) { icon.Color = entry.Key == id ? "#ffffff" : "#8b8b8b"; icon.Invalidate(); } }
    }
}
