using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhiteWhale.FlowStudio;

/// <summary>UI-only regression checks run on a disposable document, without saving user content.</summary>
public static class FlowStudioFrontendVerification
{
    private static FlowStudioUI ui;
    private static string previousFile, temporaryFile, a, b, start;
    private static int step, ticks;
    private static readonly List<string> report = new List<string>();
    public static string Begin()
    {
        ui = UnityEngine.Object.FindFirstObjectByType<FlowStudioUI>();
        if (ui == null || !Application.isPlaying) throw new Exception("请运行 Flow Studio 场景。");
        if (ui.IsDirty || ui.Simulation != null) throw new Exception("请先保存当前文档并停止模拟，再运行 UI 检查。");
        previousFile = ui.CurrentFile;
        temporaryFile = "__frontend_check_" + Guid.NewGuid().ToString("N") + ".flow.json";
        var fixture = FlowDocument.Empty("event", "前端交互检查");
        fixture.panX = 40; fixture.panY = 90; fixture.zoom = .8f;
        fixture.nodes[0].Position = Vector2.zero; start = fixture.nodes[0].id;
        fixture.nodes[1].Position = new Vector2(870, 0);
        StudioNode first = fixture.Add("delay", new Vector2(290, 0)); first.remarks = "等待 A"; a = first.id;
        StudioNode second = fixture.Add("delay", new Vector2(580, 0)); second.remarks = "等待 B"; b = second.id;
        fixture.Connect(start, "next", a); fixture.Connect(a, "next", b); fixture.Connect(b, "next", fixture.nodes[1].id);
        ui.Files.Save(temporaryFile, fixture); ui.OpenDocument(temporaryFile);
        step = ticks = 0; report.Clear();
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        return "Frontend checks started on an isolated temporary file";
    }
    private static void Check(bool condition, string label)
    { if (!condition) throw new Exception(label); report.Add("PASS " + label); }
    private static void Pointer(VisualElement target, EventType type, Vector2 at, int button = 0, EventModifiers modifiers = EventModifiers.None)
    {
        var system = new Event { type = type, mousePosition = at, button = button, modifiers = modifiers };
        if (type == EventType.MouseDown) using (var evt = PointerDownEvent.GetPooled(system)) { evt.target = target; target.SendEvent(evt); }
        else if (type == EventType.MouseUp) using (var evt = PointerUpEvent.GetPooled(system)) { evt.target = target; target.SendEvent(evt); }
        else using (var evt = PointerMoveEvent.GetPooled(system)) { evt.target = target; target.SendEvent(evt); }
    }
    private static void Press(VisualElement button)
    { button.Focus(); using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); } }
    private static void Key(VisualElement target, KeyCode key, EventModifiers modifiers = EventModifiers.None)
    { using (var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key, modifiers = modifiers })) { evt.target = target; target.SendEvent(evt); } }
    private static void Tick()
    {
        if (++ticks % 5 != 0) return;
        try
        {
            VisualElement canvas = ui.Root.Q("graph-canvas");
            switch (step++)
            {
                case 0:
                    Check(ui.Root.Q("delete-" + a) != null && ui.Root.Q("delete-selection") != null, "node title and fixed selection bar expose delete actions");
                    Press(ui.Root.Q("delete-" + a));
                    Check(ui.Document.Find(a) == null && !ui.Document.edges.Any(e => e.from == a || e.to == a), "node X removes the node and attached wires");
                    ui.Undo(); Check(ui.Document.Find(a) != null && ui.Document.edges.Count == 3, "one undo restores node and all attached wires"); break;
                case 1:
                    ui.SelectNode(b); Press(ui.Root.Q("delete-selection"));
                    Check(ui.Document.Find(b) == null, "fixed delete button removes selected node without scrolling inspector");
                    ui.Undo(); break;
                case 2:
                    var firstCard = ui.Root.Q("node-" + a); var secondCard = ui.Root.Q("node-" + b);
                    Pointer(firstCard, EventType.MouseDown, firstCard.worldBound.center);
                    Pointer(secondCard, EventType.MouseDown, secondCard.worldBound.center, 0, EventModifiers.Shift);
                    Check(ui.SelectedNodes.Count == 2, "Shift-click selects multiple nodes");
                    break;
                case 3:
                    Vector2 firstPosition = ui.Document.Find(a).Position, secondPosition = ui.Document.Find(b).Position;
                    var card = ui.Root.Q("node-" + a); var heading = card.Q(className: "node-heading");
                    Vector2 at = heading.worldBound.center;
                    Pointer(heading, EventType.MouseDown, at); Pointer(card, EventType.MouseDrag, at + new Vector2(60, 40)); Pointer(card, EventType.MouseUp, at + new Vector2(60, 40));
                    Check(Vector2.Distance(ui.Document.Find(a).Position - firstPosition, ui.Document.Find(b).Position - secondPosition) < .01f && ui.Document.Find(a).y > 1,
                        "dragging a selected title moves the whole group together");
                    ui.Undo(); Check(ui.Document.Find(a).Position == firstPosition && ui.Document.Find(b).Position == secondPosition, "group movement is one undoable operation");
                    break;
                case 4:
                    Rect first = ui.Root.Q("node-" + a).worldBound, second = ui.Root.Q("node-" + b).worldBound;
                    Vector2 from = new Vector2(first.xMin - 10, first.yMin - 10), to = new Vector2(second.xMax + 10, second.yMax + 10);
                    Pointer(canvas, EventType.MouseDown, from); Pointer(canvas, EventType.MouseDrag, to); Pointer(canvas, EventType.MouseUp, to);
                    Check(ui.SelectedNodes.Count == 2 && ui.SelectedNodes.Contains(a) && ui.SelectedNodes.Contains(b), "empty-canvas drag box-selects intersecting nodes");
                    Key(ui.Root, KeyCode.C, EventModifiers.Control); Key(ui.Root, KeyCode.V, EventModifiers.Control);
                    Check(ui.Document.nodes.Count == 6 && ui.SelectedNodes.Count == 2, "Ctrl+C and Ctrl+V paste the selected group");
                    Check(ui.Document.edges.Count(e => ui.SelectedNodes.Contains(e.from) && ui.SelectedNodes.Contains(e.to)) == 1, "group paste remaps and preserves internal wire");
                    Check(!ui.SelectedNodes.Contains(a) && !ui.SelectedNodes.Contains(b), "pasted nodes receive new identities");
                    Press(ui.Root.Q("delete-selection")); Check(ui.Document.nodes.Count == 4, "batch delete removes pasted group");
                    ui.Undo(); Check(ui.Document.nodes.Count == 6, "batch deletion can be undone in one step"); ui.Undo(); break;
                case 5:
                    var search = ui.Root.Q<TextField>("node-search"); search.value = "等待时间";
                    Check(ui.Root.Q("palette-delay") != null && ui.Root.Q("palette-transfer") == null, "node search filters the visible palette");
                    search.value = "不存在的节点";
                    Check(ui.Root.Q("palette-delay") == null, "empty node-search results are handled");
                    search.value = ""; break;
                case 6:
                    VisualElement target = ui.Root.Q("node-" + a);
                    Pointer(target, EventType.MouseDown, target.worldBound.center, 1);
                    Check(ui.Root.Q("graph-context-menu") != null, "right-click opens node context menu");
                    var delete = ui.Root.Q("graph-context-menu").Query<Button>().ToList().Single(button => button.text.StartsWith("删除"));
                    Press(delete); Check(ui.Document.Find(a) == null && ui.Root.Q("graph-context-menu") == null, "context delete removes node and closes menu");
                    ui.Undo(); break;
                case 7:
                    ui.SelectNode(a);
                    var inspector = ui.Root.Q<ScrollView>("node-inspector");
                    var field = inspector.Q<TextField>("node-remarks");
                    Check(field.label == "备注" && field.multiline && field == inspector.Children().Last(),
                        "multiline remarks appear at the bottom of the node inspector");
                    Check(inspector.Query<TextField>().ToList().All(input => input.label != "节点名称"),
                        "node names have no editable field");
                    Check(ui.Root.Q("node-" + a).Q<Label>("node-title").text == NodeCatalog.Get("delay").label,
                        "remarks do not change the fixed node title");
                    field.Focus();
                    Key(field, KeyCode.Delete); Check(ui.Document.Find(a) != null, "Delete inside a text field does not remove the selected node");
                    Key(ui.Root, KeyCode.A, EventModifiers.Control); Check(ui.SelectedNodes.Count == 4, "Ctrl+A selects all nodes");
                    Key(ui.Root, KeyCode.Delete); Check(ui.Document.nodes.Count == 0 && ui.Document.entry == null && ui.Document.edges.Count == 0, "delete-all clears nodes, entry and wires");
                    ui.Undo(); Check(ui.Document.nodes.Count == 4 && ui.Document.entry == start, "undo restores full graph after delete-all");
                    break;
                case 8:
                    ui.SelectNode(a);
                    Vector2 outside = canvas.worldBound.center;
                    Pointer(canvas, EventType.MouseDown, outside, 1); Pointer(canvas, EventType.MouseUp, outside, 1);
                    Check(ui.Root.Q("graph-context-menu") != null, "right-click on empty canvas opens canvas menu");
                    Key(ui.Root, KeyCode.Escape); Check(ui.Root.Q("graph-context-menu") == null, "Escape closes context menu");
                    var toggle = ui.Root.Query<Button>().ToList().Single(button => button.text.Contains("展开流程模拟"));
                    Check(ui.Root.Q("simulation-panel").resolvedStyle.display == DisplayStyle.None, "simulation pane starts collapsed to free inspector space");
                    Press(toggle); break;
                case 9:
                    Check(ui.Root.Q("simulation-panel").resolvedStyle.display != DisplayStyle.None, "simulation pane can be expanded explicitly");
                    Finish(true); break;
            }
        }
        catch (Exception exception) { report.Add("FAIL " + exception); Finish(false); }
    }
    private static void Finish(bool success)
    {
        EditorApplication.update -= Tick;
        File.WriteAllLines("Captures/FlowStudioFrontendVerification.txt", report);
        var collapse = ui.Root.Query<Button>().ToList().FirstOrDefault(button => button.text.Contains("收起流程模拟"));
        if (collapse != null) Press(collapse);
        ui.OpenDocument(previousFile);
        // Only this run's UUID-named temporary document is removed.
        string path = ui.Files.PathFor(temporaryFile);
        string asset = "Assets/StreamingAssets/Flows/" + temporaryFile;
        if (!AssetDatabase.DeleteAsset(asset) && File.Exists(path)) File.Delete(path);
        Debug.Log("FLOW_STUDIO_FRONTEND_" + (success ? "PASS " : "FAIL ") + report.Count);
    }
}
