using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace WhiteWhale.FlowStudio
{
    public sealed partial class FlowStudioUI
    {
        private readonly HashSet<string> selection = new HashSet<string>();
        public IReadOnlyCollection<string> SelectedNodes => selection;
        private Dictionary<string, Vector2> groupOrigins;
        private HashSet<string> boxInitial;
        private VisualElement marquee, contextMenu;
        private Label selectionLabel;
        private Button deleteButton, copyButton, pasteButton, deselectButton, simulationToggle;
        private string nodeSearch = "";
        private bool boxing, simulationExpanded;
        private int panButton, pasteCount;
        private Vector2 boxOrigin;
        private ClipboardData clipboard;

        [Serializable]
        private sealed class ClipboardData
        {
            public List<StudioNode> nodes = new List<StudioNode>();
            public List<StudioEdge> edges = new List<StudioEdge>();
        }

        private void BuildSelectionBar(VisualElement parent)
        {
            var bar = Box("selection-bar row"); parent.Add(bar);
            selectionLabel = Text("点击节点，或在空白处拖动框选", "selection-label"); bar.Add(selectionLabel);
            bar.Add(Box("grow"));
            copyButton = Button("复制", CopySelection, "small"); copyButton.name = "copy-selection"; bar.Add(copyButton);
            pasteButton = Button("粘贴", PasteSelection, "small"); pasteButton.name = "paste-selection"; bar.Add(pasteButton);
            deleteButton = Button("删除选中", DeleteSelection, "small danger"); deleteButton.name = "delete-selection"; bar.Add(deleteButton);
            deselectButton = Button("取消选择", () => { ClearSelection(); RefreshNodeStyles(); BuildInspector(); wires.MarkDirtyRepaint(); }, "small");
            bar.Add(deselectButton);
        }
        private void RefreshSelectionBar()
        {
            if (selectionLabel == null) return;
            selectionLabel.text = selection.Count > 0 ? "已选择 " + selection.Count + " 个节点" : selectedEdge != null ? "已选择 1 条连线" : "点击节点，或在空白处拖动框选";
            bool editing = Simulation == null;
            deleteButton.SetEnabled(editing && (selection.Count > 0 || selectedEdge != null));
            copyButton.SetEnabled(editing && selection.Any(id => Document.Find(id)?.type != "start"));
            pasteButton.SetEnabled(editing && clipboard != null && clipboard.nodes.Count > 0);
            deselectButton.SetEnabled(selection.Count > 0 || selectedEdge != null);
        }
        private void ClearSelection()
        { selection.Clear(); selectedNode = selectedEdge = null; }

        public void SelectAll()
        {
            if (Simulation != null) return;
            selection.Clear(); foreach (StudioNode node in Document.nodes) selection.Add(node.id);
            selectedNode = selection.FirstOrDefault(); selectedEdge = null; root.Focus();
            RefreshNodeStyles(); BuildInspector(); wires.MarkDirtyRepaint();
        }
        private void SelectFromPointer(string id, bool additive, bool keepGroup = false)
        {
            if (Simulation != null) { SelectNode(id); return; }
            if (additive)
            {
                if (!selection.Add(id)) selection.Remove(id);
            }
            else if (!keepGroup || !selection.Contains(id)) { selection.Clear(); selection.Add(id); }
            selectedNode = selection.Contains(id) ? id : selection.FirstOrDefault(); selectedEdge = null;
            root.Focus(); RefreshNodeStyles(); BuildInspector(); wires.MarkDirtyRepaint();
        }
        private void UpdateMarquee(Vector2 at)
        {
            Rect rectangle = Rect.MinMaxRect(Mathf.Min(boxOrigin.x, at.x), Mathf.Min(boxOrigin.y, at.y), Mathf.Max(boxOrigin.x, at.x), Mathf.Max(boxOrigin.y, at.y));
            if (rectangle.width < 4 && rectangle.height < 4) return;
            marquee.style.display = DisplayStyle.Flex; marquee.style.left = rectangle.x; marquee.style.top = rectangle.y;
            marquee.style.width = rectangle.width; marquee.style.height = rectangle.height;
            selection.Clear(); foreach (string id in boxInitial) selection.Add(id);
            foreach (StudioNode node in Document.nodes)
            {
                Vector2 position = ToCanvas(node.Position);
                float height = 112 + Math.Max(1, NodeCatalog.Get(node.type).ports.Length) * 25;
                if (rectangle.Overlaps(new Rect(position, new Vector2(NodeWidth, height) * Document.zoom))) selection.Add(node.id);
            }
            selectedNode = selection.FirstOrDefault(); selectedEdge = null; RefreshNodeStyles(); BuildInspector();
        }
        public void DeleteNode(string id)
        { if (Simulation == null) { SelectNode(id); DeleteSelection(); } }

        public void CopySelection()
        {
            if (Simulation != null) return;
            var copied = Document.nodes.Where(n => selection.Contains(n.id) && n.type != "start").ToList();
            if (copied.Count == 0) { Notice("请选择可复制的节点；开始节点保持唯一。"); return; }
            var data = new ClipboardData { nodes = copied };
            var ids = new HashSet<string>(copied.Select(n => n.id));
            data.edges = Document.edges.Where(e => ids.Contains(e.from) && ids.Contains(e.to)).ToList();
            clipboard = JsonUtility.FromJson<ClipboardData>(JsonUtility.ToJson(data)); pasteCount = 0;
            RefreshSelectionBar(); Notice("已复制 " + copied.Count + " 个节点及其内部连线。Ctrl+V 粘贴。");
        }
        public void PasteSelection() => PasteAt(null);
        private void PasteAt(Vector2? point)
        {
            if (Simulation != null || clipboard == null) return;
            var source = clipboard.nodes.Where(n => NodeCatalog.Get(n.type)?.Allowed(Document.kind) ?? false).ToList();
            if (source.Count == 0) { Notice("复制的节点不适用于当前流程类型。", true); return; }
            Vector2 offset = point.HasValue ? point.Value - new Vector2(source.Min(n => n.x), source.Min(n => n.y)) : Vector2.one * (32 * ++pasteCount);
            Edit(() =>
            {
                var map = new Dictionary<string, string>(); ClearSelection();
                foreach (StudioNode node in source)
                {
                    StudioNode copy = JsonUtility.FromJson<StudioNode>(JsonUtility.ToJson(node));
                    copy.published?.Clear();
                    copy.id = Guid.NewGuid().ToString("N"); copy.Position += offset; map[node.id] = copy.id;
                    Document.nodes.Add(copy); selection.Add(copy.id);
                }
                foreach (StudioEdge edge in clipboard.edges)
                    if (map.ContainsKey(edge.from) && map.ContainsKey(edge.to))
                        Document.edges.Add(new StudioEdge { from = map[edge.from], port = edge.port, to = map[edge.to] });
                foreach (string id in map.Values)
                {
                    var copy = Document.Find(id);
                    if (copy.type == "branch" && copy.parameters.flag?.StartsWith(NodeParameters.ConditionPrefix, StringComparison.Ordinal) == true &&
                        map.TryGetValue(copy.parameters.flag.Substring(NodeParameters.ConditionPrefix.Length), out string copiedCondition))
                        copy.parameters.flag = NodeParameters.ConditionOutput(copiedCondition);
                    copy.parameters.RemapObjects(copy.type, reference =>
                        NodeParameters.IsSpawnOutput(reference) && map.TryGetValue(NodeParameters.SpawnNodeId(reference), out string mapped)
                            ? NodeParameters.SpawnOutput(mapped) : reference);
                }
                selectedNode = selection.FirstOrDefault();
            });
            BuildGraph(); BuildInspector(); root.Focus(); Notice("已粘贴 " + source.Count + " 个节点，保留内部连线。可撤销。");
        }
        private void AlignSelection(bool top)
        {
            if (Simulation != null || selection.Count < 2) return;
            var nodes = Document.nodes.Where(n => selection.Contains(n.id)).ToList();
            Edit(() =>
            {
                float value = top ? nodes.Min(n => n.y) : nodes.Min(n => n.x);
                foreach (StudioNode node in nodes) if (top) node.y = value; else node.x = value;
            });
            BuildGraph(); BuildInspector();
        }
        private void ToggleSimulationPanel() => SetSimulationPanel(!simulationExpanded);
        private void SetSimulationPanel(bool expanded)
        {
            simulationExpanded = expanded;
            simulationPane.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            simulationToggle.text = expanded ? "▾ 收起模拟与运行记录" : "▸ 展开模拟与运行记录";
        }

        private VisualElement ContextAt(Vector2 position)
        {
            CloseContextMenu();
            contextMenu = Box("context-menu"); contextMenu.name = "graph-context-menu";
            Vector2 local = root.WorldToLocal(position);
            contextMenu.style.left = Mathf.Clamp(local.x, 5, Mathf.Max(5, root.contentRect.width - 220));
            contextMenu.style.top = Mathf.Clamp(local.y, 5, Mathf.Max(5, root.contentRect.height - 320));
            root.Add(contextMenu); return contextMenu;
        }
        private void CloseContextMenu() { contextMenu?.RemoveFromHierarchy(); contextMenu = null; }
        private void MenuAction(VisualElement menu, string caption, Action action, bool enabled = true)
        {
            Button button = Button(caption, () => { CloseContextMenu(); action(); }, "context-action");
            button.SetEnabled(enabled); menu.Add(button);
        }
        private void ShowNodeMenu(string id, Vector2 position)
        {
            if (Simulation != null) return;
            if (!selection.Contains(id)) SelectNode(id);
            VisualElement menu = ContextAt(position);
            menu.Add(Text(selection.Count > 1 ? "选中 " + selection.Count + " 个节点" : Document.Find(id).title, "context-title"));
            MenuAction(menu, "复制       Ctrl+C", CopySelection);
            MenuAction(menu, "创建副本   Ctrl+D", DuplicateNode);
            MenuAction(menu, "删除       Delete", DeleteSelection);
            MenuAction(menu, "取消选择", () => { ClearSelection(); RefreshNodeStyles(); BuildInspector(); });
        }
        private void ShowCanvasMenu(Vector2 position)
        {
            if (Simulation != null) return;
            VisualElement menu = ContextAt(position); Vector2 at = ToGraph(canvas.WorldToLocal(position));
            menu.Add(Text("画布操作", "context-title"));
            MenuAction(menu, "在这里粘贴   Ctrl+V", () => PasteAt(at), clipboard != null);
            MenuAction(menu, "选择全部     Ctrl+A", SelectAll);
            MenuAction(menu, "适应画布", FitGraph);
            MenuAction(menu, "撤销         Ctrl+Z", Undo, history.CanUndo);
            MenuAction(menu, "重做         Ctrl+Y", Redo, history.CanRedo);
        }
    }
}
