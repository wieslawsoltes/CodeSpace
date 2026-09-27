using CodeSpace.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace CodeSpace.Controls.Uno;

public sealed record FileTreeEntry(string Path, string Name, int Depth, bool IsFolder, bool Expanded);
public sealed class FileTreeControl : SKCanvasElement
{
    private readonly HashSet<string> _collapsed = [];
    private readonly List<FileTreeEntry> _rows = [];
    private Workspace? _workspace;
    private float _scroll;
    private const float RowHeight = 22;
    public string? SelectedPath { get; set; }
    public event EventHandler<string>? FileActivated;
    public event EventHandler<FileTreeEntry>? EntryContextRequested;
    public Workspace? Workspace
    {
        get => _workspace;
        set { if (_workspace is not null) _workspace.Changed -= WorkspaceChanged; _workspace = value; if (value is not null) value.Changed += WorkspaceChanged; Rebuild(); }
    }
    public FileTreeControl()
    {
        AutomationProperties.SetName(this, "Workspace file tree");
        PointerWheelChanged += (_, e) => { _scroll = Math.Clamp(_scroll - e.GetCurrentPoint(this).Properties.MouseWheelDelta / 120f * RowHeight * 3, 0, Math.Max(0, _rows.Count * RowHeight - (float)ActualHeight)); Invalidate(); e.Handled = true; };
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var index = (int)((e.GetCurrentPoint(this).Position.Y + _scroll) / RowHeight);
            if (index < 0 || index >= _rows.Count) return; var row = _rows[index];
            if (row.IsFolder) { if (!_collapsed.Add(row.Path)) _collapsed.Remove(row.Path); Rebuild(); }
            else { SelectedPath = row.Path; Invalidate(); FileActivated?.Invoke(this, row.Path); } e.Handled = true;
        };
        RightTapped += (_, e) => { var index = (int)((e.GetPosition(this).Y + _scroll) / RowHeight); if (index >= 0 && index < _rows.Count) EntryContextRequested?.Invoke(this, _rows[index]); e.Handled = true; };
    }
    private void WorkspaceChanged(object? sender, EventArgs e) => Rebuild();
    public void Rebuild()
    {
        _rows.Clear(); if (_workspace is null) { Invalidate(); return; }
        var directories = new SortedSet<string>(StringComparer.Ordinal); var files = _workspace.Files.Keys.ToArray();
        foreach (var path in files) { var index = path.IndexOf('/'); while (index >= 0) { directories.Add(path[..index]); index = path.IndexOf('/', index + 1); } }
        void Add(string parent, int depth)
        {
            var prefix = parent.Length == 0 ? "" : parent + "/";
            foreach (var directory in directories.Where(d => d.StartsWith(prefix, StringComparison.Ordinal) && !d[prefix.Length..].Contains('/')))
            {
                var expanded = !_collapsed.Contains(directory); _rows.Add(new(directory, directory[prefix.Length..], depth, true, expanded)); if (expanded) Add(directory, depth + 1);
            }
            foreach (var file in files.Where(f => f.StartsWith(prefix, StringComparison.Ordinal) && !f[prefix.Length..].Contains('/'))) _rows.Add(new(file, file[prefix.Length..], depth, false, false));
        }
        Add("", 0); _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _rows.Count * RowHeight - (float)ActualHeight)); Invalidate();
    }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        canvas.Save(); canvas.ClipRect(new(0, 0, (float)area.Width, (float)area.Height));
        using var paint = new SKPaint { IsAntialias = true }; using var font = new SKFont(CodeSpace.Rendering.Skia.EditorRenderer.DefaultTypeface ?? SKTypeface.Default, 13);
        paint.Color = SKColor.Parse("#181818"); canvas.DrawRect(0, 0, (float)area.Width, (float)area.Height, paint);
        var first = Math.Max(0, (int)(_scroll / RowHeight)); var last = Math.Min(_rows.Count, first + (int)(area.Height / RowHeight) + 2);
        for (var i = first; i < last; i++)
        {
            var row = _rows[i]; var y = i * RowHeight - _scroll; var x = 12 + row.Depth * 16;
            if (row.Path == SelectedPath) { paint.Color = SKColor.Parse("#37373d"); canvas.DrawRect(0, y, (float)area.Width, RowHeight, paint); }
            if (row.IsFolder)
            {
                paint.Color = SKColor.Parse("#b5b5b5"); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1.2f;
                using var path = new SKPath(); if (row.Expanded) { path.MoveTo(x, y + 9); path.LineTo(x + 4, y + 13); path.LineTo(x + 8, y + 9); } else { path.MoveTo(x + 2, y + 7); path.LineTo(x + 6, y + 11); path.LineTo(x + 2, y + 15); }
                canvas.DrawPath(path, paint); paint.Style = SKPaintStyle.Fill;
            }
            else
            {
                var ext = System.IO.Path.GetExtension(row.Path); paint.Color = SKColor.Parse(ext switch { ".cs" => "#65ad80", ".ts" or ".js" => "#d8ba62", ".json" => "#cbcb41", ".md" => "#7abaff", ".xaml" or ".xml" => "#e8a56d", _ => "#999999" });
                var glyph = ext switch { ".cs" => "#", ".ts" => "TS", ".js" => "JS", ".json" => "{}", ".md" => "M", _ => "◇" };
                using var small = new SKFont(font.Typeface, 10); canvas.DrawText(glyph, x, y + 15, SKTextAlign.Left, small, paint);
            }
            paint.Color = SKColor.Parse("#cccccc"); canvas.DrawText(row.Name, x + 21, y + 16, SKTextAlign.Left, font, paint);
        }
        canvas.Restore();
    }
}
