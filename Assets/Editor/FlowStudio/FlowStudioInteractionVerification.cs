using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WhiteWhale.FlowStudio;

/// <summary>Exercises the real runtime controls through UI Toolkit pointer/keyboard events.</summary>
public static class FlowStudioInteractionVerification
{
    private static FlowStudioUI ui;
    private static int step, ticks;
    private static string nearId, travelId, delayId;
    private static readonly List<string> report = new List<string>();
    public static string Begin()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("需要运行纯 UI 场景。");
        ui = UnityEngine.Object.FindFirstObjectByType<FlowStudioUI>();
        if (ui == null) throw new Exception("找不到工作台。");
        report.Clear(); step = ticks = 0;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        return "UI interaction checks started";
    }
    private static void Check(bool value, string label)
    { if (!value) throw new Exception(label); report.Add("PASS " + label); }
    private static void Pointer(VisualElement target, EventType type, Vector2 position, int button = 0)
    {
        var system = new Event { type = type, mousePosition = position, button = button };
        if (type == EventType.MouseDown) using (var evt = PointerDownEvent.GetPooled(system)) { evt.target = target; target.SendEvent(evt); }
        if (type == EventType.MouseDrag || type == EventType.MouseMove) using (var evt = PointerMoveEvent.GetPooled(system)) { evt.target = target; target.SendEvent(evt); }
        if (type == EventType.MouseUp) using (var evt = PointerUpEvent.GetPooled(system)) { evt.target = target; target.SendEvent(evt); }
    }
    private static void Press(string caption)
    {
        Button button = ui.Root.Query<Button>().ToList().First(b => b.text == caption);
        button.Focus();
        using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); }
    }
    private static void Tick()
    {
        if (++ticks % 4 != 0) return;
        try
        {
            var canvas = ui.Root.Q("graph-canvas");
            switch (step++)
            {
                case 0:
                    ui.OpenDocument("monkey_island.event");
                    Check(ui.Root.panel != null && ui.Root.worldBound.width > 500, "saved scene attaches full UI panel without manual repair");
                    Check(UnityEngine.Object.FindObjectsByType<ShipMovement>(FindObjectsSortMode.None).Length == 0 && UnityEngine.Object.FindObjectsByType<OceanSurface>(FindObjectsSortMode.None).Length == 0, "tool scene has no ocean or ship simulation");
                    nearId = ui.Document.nodes.Single(n => n.type == "wait_region").id;
                    travelId = ui.Document.nodes.Single(n => n.type == "wait_travel").id;
                    ui.SelectNode(travelId); break;
                case 1:
                    float distance = ui.Document.Find(travelId).parameters.travelDistance;
                    ui.Root.Q<ScrollView>("node-inspector").Q<FloatField>().value = distance + 3;
                    Check(ui.Document.Find(travelId).parameters.travelDistance == distance + 3 && ui.IsDirty, "inspector input updates node data and dirty state");
                    ui.SaveCurrent();
                    Check(ui.Files.Load(ui.CurrentFile).Find(travelId).parameters.travelDistance == distance + 3 && !ui.IsDirty, "UI save persists edited parameter and clears dirty state");
                    ui.Undo(); Check(ui.Document.Find(travelId).parameters.travelDistance == distance, "parameter change can be undone");
                    ui.Redo(); Check(ui.Document.Find(travelId).parameters.travelDistance == distance + 3, "parameter change can be redone"); ui.Undo();
                    ui.SaveCurrent();
                    Check(ui.Files.Load(ui.CurrentFile).Find(travelId).parameters.travelDistance == distance, "saved parameter restores through same portable file format");
                    break;
                case 2:
                    VisualElement card = ui.Root.Q("node-" + nearId), heading = card.Q(className: "node-heading");
                    Vector2 position = heading.worldBound.center, original = ui.Document.Find(nearId).Position;
                    Pointer(heading, EventType.MouseDown, position);
                    Pointer(card, EventType.MouseDrag, position + new Vector2(90, 45));
                    Pointer(card, EventType.MouseUp, position + new Vector2(90, 45));
                    Check(Vector2.Distance(original, ui.Document.Find(nearId).Position) > 50, "header pointer drag moves node in graph coordinates");
                    ui.Undo(); Check(Vector2.Distance(original, ui.Document.Find(nearId).Position) < .01f, "node drag records one reversible edit"); break;
                case 3:
                    VisualElement row = ui.Root.Q("palette-delay"); int count = ui.Document.nodes.Count;
                    Pointer(row, EventType.MouseDown, row.worldBound.center);
                    Pointer(row, EventType.MouseDrag, canvas.worldBound.center);
                    Pointer(row, EventType.MouseUp, canvas.worldBound.center);
                    Check(ui.Document.nodes.Count == count + 1, "palette pointer drag drops a new node on canvas");
                    delayId = ui.Document.nodes.Single(n => n.type == "delay").id; break;
                case 4:
                    VisualElement output = ui.Root.Q("output-" + nearId + "-next"), input = ui.Root.Q("input-" + delayId);
                    Pointer(output, EventType.MouseDown, output.worldBound.center);
                    Pointer(canvas, EventType.MouseMove, input.worldBound.center);
                    Pointer(canvas, EventType.MouseUp, input.worldBound.center);
                    Check(ui.Document.Output(nearId, "next").to == delayId, "port drag connects nodes and replaces previous output");
                    ui.Undo(); Check(ui.Document.Output(nearId, "next").to != delayId, "connection can be undone");
                    ui.Redo(); Check(ui.Document.Output(nearId, "next").to == delayId, "connection can be redone"); break;
                case 5:
                    ui.SelectNode(delayId);
                    using (var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Delete })) { evt.target = ui.Root; ui.Root.SendEvent(evt); }
                    Check(ui.Document.Find(delayId) == null && !ui.Document.edges.Any(e => e.from == delayId || e.to == delayId), "Delete removes selected node and attached connections");
                    ui.Undo(); Check(ui.Document.Find(delayId) != null, "deletion can be undone");
                    Vector2 pan = new Vector2(ui.Document.panX, ui.Document.panY), at = canvas.worldBound.center;
                    Pointer(canvas, EventType.MouseDown, at, 2); Pointer(canvas, EventType.MouseDrag, at + new Vector2(55, 30), 2); Pointer(canvas, EventType.MouseUp, at + new Vector2(55, 30), 2);
                    Check(Vector2.Distance(pan, new Vector2(ui.Document.panX, ui.Document.panY)) > 30, "middle-button drag pans canvas");
                    float zoom = ui.Document.zoom;
                    using (var evt = WheelEvent.GetPooled(new Event { type = EventType.ScrollWheel, mousePosition = at, delta = new Vector2(0, -3) })) { evt.target = canvas; canvas.SendEvent(evt); }
                    Check(ui.Document.zoom > zoom, "wheel input zooms canvas");
                    ui.OpenDocument("tabletop_main.flow.json"); break;
                case 6:
                    Press("▶ 开始模拟"); Check(ui.Simulation != null, "route rehearsal starts");
                    Press("暂停");
                    for (int i = 0; i < 4 && ui.Simulation.CurrentNode.type != "wait_travel"; i++) Press("单步");
                    Check(ui.Simulation.CurrentNode.type == "wait_travel", "pause and single-step select a pending wait node across event call");
                    break;
                case 7:
                    Press("完成移动");
                    Check(ui.Simulation.CurrentDocument.kind == "event" && ui.Simulation.CurrentNode.type == "wait_region", "simulation opens referenced event graph and highlights arrival wait");
                    Press("进入范围"); break;
                case 8:
                    Check(ui.Simulation.CurrentNode.type == "wait_confirmation", "arrival advances to confirmation");
                    Press("确认提示"); break;
                case 9:
                    Check(ui.Simulation.CurrentNode.type == "run_action", "confirmation advances to first action");
                    Press("玩法成功");
                    Check(ui.Simulation.CurrentNode.type == "run_action", "first action advances to the next action");
                    Press("玩法成功"); break;
                case 10:
                    Check(ui.Simulation.Status == SessionStatus.Completed && ui.Simulation.CurrentDocument.kind == "main", "victory button returns event result and completes main flow");
                    Press("停止"); Check(ui.Simulation == null, "stop button returns to editable document");
                    ui.OpenDocument("monkey_island.event"); ui.SelectNode(nearId);
                    File.WriteAllLines("Captures/FlowStudioInteractionVerification.txt", report);
                    EditorApplication.update -= Tick;
                    UnityEngine.ScreenCapture.CaptureScreenshot("Captures/FlowStudioUI.png");
                    Debug.Log("FLOW_STUDIO_UI_CHECKS_PASS " + report.Count);
                    break;
            }
        }
        catch (Exception exception)
        {
            EditorApplication.update -= Tick; report.Add("FAIL " + exception);
            File.WriteAllLines("Captures/FlowStudioInteractionVerification.txt", report);
            if (ui != null) { ui.StopSimulation(); ui.OpenDocument("monkey_island.event"); }
            Debug.LogWarning("FLOW_STUDIO_UI_CHECKS_FAIL " + exception.Message);
        }
    }
}
