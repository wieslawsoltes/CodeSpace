using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeSpace.Docking;

public enum SplitAxis { Horizontal, Vertical }
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TabGroup), "group")]
[JsonDerivedType(typeof(SplitNode), "split")]
public abstract record DockNode(string Id);
public sealed record TabGroup(string Id, string[] Tabs, string? ActiveTab) : DockNode(Id);
public sealed record SplitNode(string Id, SplitAxis Axis, double Ratio, DockNode First, DockNode Second) : DockNode(Id);
public sealed record DockState(int Version, DockNode Root, double SideBarWidth, double PanelHeight, bool SideBarVisible, bool PanelVisible);

/// <summary>Immutable, serializable docking model. Host controls decide how to present each node.</summary>
public sealed class DockLayout
{
    public DockState State { get; private set; } = new(1, new TabGroup("primary", [], null), 254, 190, true, true);
    public event EventHandler? Changed;
    public IEnumerable<TabGroup> Groups => Enumerate(State.Root);
    private static IEnumerable<TabGroup> Enumerate(DockNode node)
    {
        if (node is TabGroup group) { yield return group; yield break; }
        var split = (SplitNode)node; foreach (var item in Enumerate(split.First)) yield return item; foreach (var item in Enumerate(split.Second)) yield return item;
    }
    private static DockNode Map(DockNode node, string id, Func<DockNode, DockNode> change)
    {
        if (node.Id == id) return change(node);
        return node is SplitNode split ? split with { First = Map(split.First, id, change), Second = Map(split.Second, id, change) } : node;
    }
    public void Open(string tab, string groupId = "primary")
    {
        if (!Groups.Any(g => g.Id == groupId)) throw new ArgumentException("Unknown group.", nameof(groupId));
        Set(State with { Root = Map(State.Root, groupId, n => { var g = (TabGroup)n; return g with { Tabs = g.Tabs.Contains(tab) ? g.Tabs : [.. g.Tabs, tab], ActiveTab = tab }; }) });
    }
    public void Close(string tab, string groupId)
    {
        Set(State with { Root = Map(State.Root, groupId, n => { var g = (TabGroup)n; var tabs = g.Tabs.Where(t => t != tab).ToArray(); return g with { Tabs = tabs, ActiveTab = g.ActiveTab == tab ? tabs.LastOrDefault() : g.ActiveTab }; }) });
    }
    public string Split(string groupId, SplitAxis axis, string? tab = null)
    {
        if (!Groups.Any(g => g.Id == groupId)) throw new ArgumentException("Unknown group.");
        var id = Guid.NewGuid().ToString("N");
        Set(State with { Root = Map(State.Root, groupId, n => new SplitNode(Guid.NewGuid().ToString("N"), axis, 0.5, n, new TabGroup(id, tab is null ? [] : [tab], tab))) }); return id;
    }
    public void Move(string tab, string sourceId, string targetId, int targetIndex = int.MaxValue)
    {
        var source = Groups.Single(g => g.Id == sourceId); var target = Groups.Single(g => g.Id == targetId);
        if (!source.Tabs.Contains(tab)) throw new ArgumentException("Tab is not in source group.");
        var root = Map(State.Root, sourceId, n => { var g = (TabGroup)n; var tabs = g.Tabs.Where(t => t != tab).ToArray(); return g with { Tabs = tabs, ActiveTab = g.ActiveTab == tab ? tabs.FirstOrDefault() : g.ActiveTab }; });
        root = Map(root, targetId, n => { var g = (TabGroup)n; var tabs = g.Tabs.Where(t => t != tab).ToList(); tabs.Insert(Math.Clamp(targetIndex, 0, tabs.Count), tab); return g with { Tabs = tabs.ToArray(), ActiveTab = tab }; });
        Set(State with { Root = root });
    }
    public void Resize(string splitId, double ratio)
    {
        if (!double.IsFinite(ratio)) throw new ArgumentOutOfRangeException(nameof(ratio));
        Set(State with { Root = Map(State.Root, splitId, n => n is SplitNode split ? split with { Ratio = Math.Clamp(ratio, 0.1, 0.9) } : n) });
    }
    public void SetSideBar(bool? visible = null, double? width = null) => Set(State with { SideBarVisible = visible ?? State.SideBarVisible, SideBarWidth = Math.Clamp(width ?? State.SideBarWidth, 150, 700) });
    public void SetPanel(bool? visible = null, double? height = null) => Set(State with { PanelVisible = visible ?? State.PanelVisible, PanelHeight = Math.Clamp(height ?? State.PanelHeight, 70, 700) });
    public void Reset() => Set(new DockState(1, new TabGroup("primary", [], null), 254, 190, true, true));
    public string Serialize() => JsonSerializer.Serialize(State);
    public void Restore(string json)
    {
        var state = JsonSerializer.Deserialize<DockState>(json) ?? throw new FormatException("Invalid docking state.");
        if (state.Version != 1 || state.Root is null) throw new FormatException("Unknown docking schema.");
        var ids = new HashSet<string>(); Validate(state.Root, ids, 0);
        if (!double.IsFinite(state.SideBarWidth) || !double.IsFinite(state.PanelHeight)) throw new FormatException("Invalid dimensions.");
        Set(state with { SideBarWidth = Math.Clamp(state.SideBarWidth, 150, 700), PanelHeight = Math.Clamp(state.PanelHeight, 70, 700) });
    }
    private static void Validate(DockNode node, HashSet<string> ids, int depth)
    {
        if (depth > 20 || !ids.Add(node.Id)) throw new FormatException("Invalid or duplicate docking node.");
        if (node is TabGroup group)
        {
            if (group.Tabs is null || group.Tabs.Distinct().Count() != group.Tabs.Length || group.ActiveTab is not null && !group.Tabs.Contains(group.ActiveTab)) throw new FormatException("Invalid tab group.");
        }
        else if (node is SplitNode split)
        {
            if (!double.IsFinite(split.Ratio) || split.Ratio < 0.1 || split.Ratio > 0.9) throw new FormatException("Invalid split ratio.");
            Validate(split.First, ids, depth + 1); Validate(split.Second, ids, depth + 1);
        }
    }
    private void Set(DockState state) { State = state; Changed?.Invoke(this, EventArgs.Empty); }
}
