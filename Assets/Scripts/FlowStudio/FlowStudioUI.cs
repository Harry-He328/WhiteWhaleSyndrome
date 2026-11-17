using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using WhiteWhale.Flow;

namespace WhiteWhale.FlowStudio
{
    [RequireComponent(typeof(UIDocument))]
    public sealed partial class FlowStudioUI : MonoBehaviour
    {
        public StyleSheet styleSheet;
        public string initialFile;
        [SerializeField] private UnityEngine.Object[] bindingScenes;
        private FlowDocument baseline;
        public FlowDocument Document { get; private set; }
        public FlowSession Simulation { get; private set; }
        public FlowFileStore Files { get; private set; }
        public string CurrentFile { get; private set; }
        public bool IsDirty => Document != null && Document.ToJson() != savedJson;
        public VisualElement Root => root;
        private FlowDocument VisibleDocument => Simulation?.CurrentDocument ?? Document;
        private readonly DocumentHistory history = new DocumentHistory();
        private readonly Dictionary<string, VisualElement> cards = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, Label> summaries = new Dictionary<string, Label>();
        private VisualElement root, canvas, world, wires, grid, palette, overlay, ghost;
        private ScrollView inspector, simulationPane;
        private Label titleLabel, kindLabel, fileLabel, statusLabel, zoomLabel;
        private Label nodeIssues;
        private Button undoButton, redoButton, saveButton, simulateButton, startupButton, addEventButton;
        private string savedJson, selectedNode, selectedEdge, renderedDocument;
        private string draggingNode, gestureBefore, wireFrom, wirePort;
        private Vector2 dragOrigin, nodeOrigin, pointerOrigin, panOrigin, wirePointer;
        private bool panning, paused, paletteMoved;
        private string paletteType;
        private SimulationActions simulationActions;
        private Font uiFont;
        private bool previousRunInBackground;
        private UIDocument uiDocument;
        private const float NodeWidth = 216;
        private const float DataPortY = 128;
        private const string SpawnResultPort = "spawn-result";
        private const string RegionWirePrefix = "range-center:";
        private static readonly Color DataWireColor = new Color(.77f, .65f, 1f);

        private void OnEnable()
        {
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            uiDocument = GetComponent<UIDocument>();
            Files = new FlowFileStore(FlowFileStore.DefaultRoot);
            FlowFiles.CopyBundledFiles(Files);
            LoadSceneBindings();
            UnityEngine.Cursor.lockState = CursorLockMode.None; UnityEngine.Cursor.visible = true;
        }

        private void AttachInterface(VisualElement currentRoot)
        {
            root?.UnregisterCallback<KeyDownEvent>(OnKey);
            root = currentRoot;
            root.Clear();
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            root.AddToClassList("studio"); root.focusable = true;
            if (uiFont == null) uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 15);
            root.style.unityFontDefinition = FontDefinition.FromFont(uiFont);
            BuildShell();
            try
            {
                if (Document == null)
                {
                    string file = string.IsNullOrWhiteSpace(initialFile) ? Files.StartupFile : initialFile;
                    if (file != null) OpenDocument(file);
                    else ShowGraph(FlowDocument.Empty("event", "新事件"));
                }
                else
                {
                    SetSimulationPanel(Simulation != null);
                    RefreshAll();
                }
            }
            catch (Exception exception) { Document = FlowDocument.Empty("event", "新事件"); savedJson = ""; RefreshAll(); Notice(exception.Message, true); }
            root.RegisterCallback<KeyDownEvent>(OnKey);
        }
        private void OnDisable()
        {
            Simulation?.Stop(); Simulation = null;
            root?.UnregisterCallback<KeyDownEvent>(OnKey);
            root = null;
            Application.runInBackground = previousRunInBackground;
            if (uiFont != null) Destroy(uiFont);
            uiFont = null;
        }
        private void Update()
        {
            var currentRoot = uiDocument.rootVisualElement;
            if (currentRoot?.panel == null) return;
            if (root != currentRoot) AttachInterface(currentRoot);
            if (Simulation != null && !paused) Simulation.Advance(Time.unscaledDeltaTime);
        }

        private static VisualElement Box(string classes = null)
        { var element = new VisualElement(); if (classes != null) foreach (string name in classes.Split(' ')) element.AddToClassList(name); return element; }
        private static Label Text(string text, string classes = null)
        { var label = new Label(text); if (classes != null) foreach (string name in classes.Split(' ')) label.AddToClassList(name); return label; }
        private Button Button(string text, Action action, string classes = null)
        {
            var button = new Button(() => Try(action)) { text = text };
            if (classes != null) foreach (string name in classes.Split(' ')) button.AddToClassList(name);
            return button;
        }
        private void Try(Action action)
        { try { action(); } catch (Exception exception) { Notice(exception.Message, true); } }

        private void BuildShell()
        {
            var header = Box("header row"); root.Add(header);
            header.Add(Text("W", "logo"));
            var brand = Box("brand"); brand.Add(Text("流程工作台", "brand-title")); brand.Add(Text("WHITE WHALE / FLOW STUDIO", "eyebrow")); header.Add(brand);
            var spacer = Box(); spacer.style.flexGrow = 1; header.Add(spacer);
            header.Add(Button("游戏入口", () => Guard(() =>
            {
                if (Files.StartupFile == null) Notice("尚未设置游戏入口。");
                else OpenDocument(Files.StartupFile);
            }), "quiet"));
            header.Add(Text("事件制作", "mode-badge"));

            var toolbar = Box("toolbar row"); root.Add(toolbar);
            toolbar.Add(Button("＋ 新建整体流程", () => NewDocument("main")));
            toolbar.Add(Button("＋ 新建事件", () => NewDocument("event")));
            toolbar.Add(Button("打开", OpenLibrary));
            addEventButton = Button("添加事件", OpenEventLibrary);
            toolbar.Add(addEventButton);
            saveButton = Button("保存", () => SaveCurrent()); toolbar.Add(saveButton);
            toolbar.Add(Button("另存为", () => SaveAs()));
            startupButton = Button("设为游戏入口", SetStartupFlow);
            toolbar.Add(startupButton);
            toolbar.Add(Box("separator"));
            undoButton = Button("撤销", Undo); redoButton = Button("重做", Redo);
            toolbar.Add(undoButton); toolbar.Add(redoButton);
            toolbar.Add(Button("检查", ValidateDocument));
            toolbar.Add(Button("适应画布", FitGraph));
            var grow = Box(); grow.style.flexGrow = 1; toolbar.Add(grow);
            simulateButton = Button("▶ 开始模拟", StartSimulation, "primary"); toolbar.Add(simulateButton);

            var body = Box("body row"); root.Add(body);
            var left = Box("palette-panel"); body.Add(left);
            left.Add(Text("节点库", "panel-title")); left.Add(Text("拖到画布，也可点击添加", "muted"));
            var paletteScroll = new ScrollView(); paletteScroll.style.flexGrow = 1; left.Add(paletteScroll);
            var search = new TextField { name = "node-search", tooltip = "按节点名称或类别搜索" };
            search.AddToClassList("node-search"); search.textEdition.placeholder = "搜索节点…";
            left.Insert(2, search);
            search.RegisterValueChangedCallback(evt => { nodeSearch = evt.newValue; BuildPalette(); });
            palette = paletteScroll;
            var center = Box("center"); body.Add(center);
            var docbar = Box("docbar row"); center.Add(docbar);
            kindLabel = Text("事件流程", "kind-badge"); docbar.Add(kindLabel);
            titleLabel = Text("", "document-title"); docbar.Add(titleLabel);
            docbar.Add(Box("grow"));
            zoomLabel = Text("100%", "muted"); docbar.Add(zoomLabel);
            fileLabel = Text("", "file-line"); center.Add(fileLabel);
            BuildSelectionBar(center);
            canvas = Box("canvas"); canvas.name = "graph-canvas"; center.Add(canvas);
            grid = Box("fill"); grid.pickingMode = PickingMode.Ignore; grid.generateVisualContent += DrawGrid; canvas.Add(grid);
            wires = Box("fill"); wires.pickingMode = PickingMode.Ignore; wires.generateVisualContent += DrawWires; canvas.Add(wires);
            world = Box("world"); world.pickingMode = PickingMode.Ignore; canvas.Add(world);
            marquee = Box("selection-marquee"); marquee.pickingMode = PickingMode.Ignore; marquee.style.display = DisplayStyle.None; canvas.Add(marquee);
            canvas.RegisterCallback<PointerDownEvent>(CanvasDown);
            canvas.RegisterCallback<PointerMoveEvent>(CanvasMove);
            canvas.RegisterCallback<PointerUpEvent>(CanvasUp);
            canvas.RegisterCallback<WheelEvent>(CanvasWheel);
            canvas.RegisterCallback<GeometryChangedEvent>(_ => { grid.MarkDirtyRepaint(); wires.MarkDirtyRepaint(); });
            var hint = Text("左键框选 · Shift 多选 · 中键 / 右键拖动平移 · 右键菜单 · Delete 删除 · Ctrl+C / V 复制粘贴", "canvas-hint"); center.Add(hint);

            var right = Box("inspector-panel"); body.Add(right);
            right.Add(Text("节点设置", "panel-title"));
            inspector = new ScrollView(); inspector.name = "node-inspector"; inspector.AddToClassList("inspector-scroll"); right.Add(inspector);
            var simHeading = Button("▸ 展开流程模拟", ToggleSimulationPanel, "simulation-heading"); right.Add(simHeading);
            simulationToggle = simHeading;
            simulationPane = new ScrollView(); simulationPane.name = "simulation-panel"; simulationPane.AddToClassList("simulation-scroll"); right.Add(simulationPane);
            simulationPane.style.display = DisplayStyle.None;
            var footer = Box("footer row"); root.Add(footer);
            statusLabel = Text("就绪", "status"); footer.Add(statusLabel); footer.Add(Box("grow"));
            footer.Add(Text("流程模拟仅预览节点顺序。", "muted"));
            overlay = Box("modal-overlay fill"); overlay.style.display = DisplayStyle.None; root.Add(overlay);
            overlay.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            root.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (contextMenu == null) return;
                var target = evt.target as VisualElement;
                if (target != contextMenu && (target == null || !contextMenu.Contains(target))) CloseContextMenu();
            }, TrickleDown.TrickleDown);
        }

        private void BuildPalette()
        {
            palette.Clear(); string group = null;
            foreach (NodeSpec spec in NodeCatalog.All.Where(s => s.Allowed(Document.kind) &&
                (string.IsNullOrWhiteSpace(nodeSearch) ||
                 (s.label + " " + s.group + " " + s.description).IndexOf(nodeSearch, StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                if (group != spec.group) { group = spec.group; palette.Add(Text(group, "group-label")); }
                var row = Box("palette-node row"); row.name = "palette-" + spec.type;
                var dot = Box("node-dot"); dot.style.backgroundColor = spec.color; row.Add(dot);
                row.Add(Text(spec.label)); row.tooltip = spec.description;
                row.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (Simulation != null || evt.button != 0) return;
                    paletteType = spec.type; paletteMoved = false; pointerOrigin = evt.position;
                    row.CapturePointer(evt.pointerId); evt.StopPropagation();
                });
                row.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (paletteType != spec.type) return;
                    if (Vector2.Distance(pointerOrigin, evt.position) > 5) paletteMoved = true;
                    if (!paletteMoved) return;
                    if (ghost == null) { ghost = Text(spec.label, "drag-ghost"); ghost.pickingMode = PickingMode.Ignore; root.Add(ghost); }
                    Vector2 at = root.WorldToLocal(evt.position); ghost.style.left = at.x + 12; ghost.style.top = at.y + 12;
                });
                row.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (paletteType != spec.type) return;
                    bool inside = canvas.worldBound.Contains(evt.position);
                    if (!paletteMoved || inside)
                    {
                        Vector2 at = paletteMoved ? canvas.WorldToLocal(evt.position) : canvas.contentRect.center;
                        Try(() => AddNode(spec.type, ToGraph(at) - new Vector2(70, 30)));
                    }
                    ghost?.RemoveFromHierarchy(); ghost = null; paletteType = null;
                    row.ReleasePointer(evt.pointerId); evt.StopPropagation();
                });
                row.RegisterCallback<PointerCaptureOutEvent>(_ =>
                {
                    if (paletteType != spec.type) return;
                    ghost?.RemoveFromHierarchy(); ghost = null; paletteType = null;
                });
                palette.Add(row);
            }
            if (Document.kind == "event")
                foreach (var action in actionLibrary.Values.Where(item => item.Matches(nodeSearch)).OrderBy(item => item.Label))
                {
                    var shortcut = Button(action.Label, () => AddAction(action, ToGraph(canvas.contentRect.center)),
                        "palette-node action-shortcut");
                    shortcut.tooltip = action.Detail;
                    palette.Add(shortcut);
                }
            if (palette.childCount == 0) palette.Add(Text("未找到匹配的节点", "muted wrap"));
        }

        private Vector2 Pan => new Vector2(VisibleDocument.panX, VisibleDocument.panY);
        private Vector2 ToGraph(Vector2 point) => (point - Pan) / VisibleDocument.zoom;
        private Vector2 ToCanvas(Vector2 point) => point * VisibleDocument.zoom + Pan;
        private static Vector2 InputPoint(StudioNode node) => node.Position + new Vector2(0, 65);
        private static Vector2 OutputPoint(StudioNode node, int index) => node.Position + new Vector2(NodeWidth, 103 + index * 25);
        private static Vector2 DataInputPoint(StudioNode node) => node.Position + new Vector2(0, DataPortY);
        private static Vector2 DataOutputPoint(StudioNode node) => node.Position + new Vector2(NodeWidth, DataPortY);
        private static float NodeHeight(StudioNode node) => 112 + Math.Max(1, NodeCatalog.Get(node.type)?.ports.Length ?? 0) * 25 +
            (node.SpawnsObject || node.type == "wait_region" ? 25 : 0);

        private void BuildGraph()
        {
            FlowDocument doc = VisibleDocument;
            renderedDocument = doc.id; world.Clear(); cards.Clear(); summaries.Clear();
            foreach (StudioNode node in doc.nodes)
            {
                NodeSpec spec = NodeCatalog.Get(node.type);
                if (spec == null) continue;
                var card = Box("graph-node"); card.name = "node-" + node.id; card.userData = node.id;
                card.style.left = node.x; card.style.top = node.y; card.style.width = NodeWidth;
                card.style.height = NodeHeight(node);
                card.style.borderTopColor = spec.color; world.Add(card); cards[node.id] = card;
                var heading = Box("node-heading row"); card.Add(heading);
                var dot = Box("node-dot"); dot.style.backgroundColor = spec.color; heading.Add(dot);
                var name = Text(node.Name, "node-name"); name.name = "node-title"; heading.Add(name);
                var remove = Button("×", () => DeleteNode(node.id), "node-remove"); remove.name = "delete-" + node.id;
                remove.tooltip = "删除这个节点（可撤销）"; remove.SetEnabled(Simulation == null); heading.Add(remove);
                remove.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                var summary = Text(NodeCatalog.Summary(doc, node, ReferenceLabel, AssetLabel), "node-summary"); card.Add(summary); summaries[node.id] = summary;
                if (node.type != "start" && node.type != "on_signal")
                {
                    var input = Box("port input-port"); input.name = "input-" + node.id; input.style.top = 58; card.Add(input);
                    input.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (wireFrom != null && wirePort != SpawnResultPort) Try(() => FinishWire(node.id));
                        else SelectNode(node.id);
                        evt.StopPropagation();
                    });
                }
                for (int i = 0; i < spec.ports.Length; i++)
                {
                    int index = i;
                    var outputLabel = Text(RouteLabel(node, i), "output-label");
                    outputLabel.name = "route-label-" + i;
                    outputLabel.style.top = 93 + i * 25;
                    card.Add(outputLabel);
                    var output = Box("port output-port"); output.name = "output-" + node.id + "-" + spec.ports[i];
                    output.style.top = 96 + i * 25; output.style.backgroundColor = spec.color; card.Add(output);
                    output.RegisterCallback<PointerDownEvent>(evt => BeginWire(evt, node.id, spec.ports[index]));
                }
                BuildDataPort(card, node);
                card.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 0 && evt.clickCount == 2 && TryOpenEvent(node))
                    { evt.StopPropagation(); return; }
                    if (evt.button == 0) SelectFromPointer(node.id, evt.shiftKey || evt.ctrlKey || evt.commandKey);
                    if (evt.button == 1) ShowNodeMenu(node.id, evt.position);
                    evt.StopPropagation();
                });
                heading.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 1) { ShowNodeMenu(node.id, evt.position); evt.StopPropagation(); return; }
                    if (evt.button != 0) return;
                    if (evt.clickCount == 2 && TryOpenEvent(node))
                    { evt.StopPropagation(); return; }
                    SelectFromPointer(node.id, evt.shiftKey || evt.ctrlKey || evt.commandKey, true);
                    if (Simulation == null && selection.Contains(node.id))
                    {
                        draggingNode = node.id; dragOrigin = evt.position; nodeOrigin = node.Position; gestureBefore = Document.ToJson();
                        groupOrigins = Document.nodes.Where(n => selection.Contains(n.id)).ToDictionary(n => n.id, n => n.Position);
                        card.CapturePointer(evt.pointerId);
                    }
                    evt.StopPropagation();
                });
                card.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (draggingNode != node.id) return;
                    Vector2 delta = ((Vector2)evt.position - dragOrigin) / doc.zoom;
                    foreach (var pair in groupOrigins)
                    {
                        StudioNode moving = doc.Find(pair.Key); moving.Position = pair.Value + delta;
                        cards[pair.Key].style.left = moving.x; cards[pair.Key].style.top = moving.y;
                    }
                    wires.MarkDirtyRepaint(); evt.StopPropagation();
                });
                card.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (draggingNode != node.id) return;
                    history.Record(gestureBefore, Document); draggingNode = null; gestureBefore = null;
                    card.ReleasePointer(evt.pointerId); RefreshMeta(); evt.StopPropagation();
                });
                card.RegisterCallback<PointerCaptureOutEvent>(_ => { if (draggingNode == node.id) EndGesture(); });
            }
            ApplyView(); RefreshNodeStyles();
        }

        private void BuildDataPort(VisualElement card, StudioNode node)
        {
            bool output = node.SpawnsObject;
            if (!output && node.type != "wait_region") return;

            string caption = output ? "生成结果" : "范围中心";
            var label = Text(caption, output ? "output-label object-output-label" : "object-input-label");
            label.style.top = DataPortY - 10;
            var port = Box(output ? "port output-port object-port" : "port input-port object-port");
            port.style.top = DataPortY - 7;
            port.name = (output ? "spawn-result-" : "range-center-") + node.id;
            port.tooltip = output ? "拖到「等待进入范围」的「范围中心」" : "接收「生成物件」的「生成结果」";
            port.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (Simulation != null || evt.button != 0) return;
                if (output) BeginWire(evt, node.id, SpawnResultPort);
                else
                {
                    if (wireFrom != null && wirePort == SpawnResultPort) Try(() => FinishWire(node.id));
                    else SelectNode(node.id);
                    evt.StopPropagation();
                }
            });
            card.Add(label);
            card.Add(port);
        }

        private void BeginWire(PointerDownEvent evt, string nodeId, string port)
        {
            if (Simulation != null || evt.button != 0) return;
            wireFrom = nodeId;
            wirePort = port;
            wirePointer = canvas.WorldToLocal(evt.position);
            canvas.CapturePointer(evt.pointerId);
            wires.MarkDirtyRepaint();
            evt.StopPropagation();
            Notice(port == SpawnResultPort ? "拖到「范围中心」。按 Esc 取消。" :
                "选择或拖到目标节点的执行入口。按 Esc 取消。");
        }

        private void ApplyView()
        {
            FlowDocument doc = VisibleDocument;
            world.style.translate = new Translate(doc.panX, doc.panY);
            world.style.scale = new Scale(new Vector3(doc.zoom, doc.zoom, 1));
            world.style.transformOrigin = new TransformOrigin(0, 0);
            zoomLabel.text = Mathf.RoundToInt(doc.zoom * 100) + "%";
            grid.MarkDirtyRepaint(); wires.MarkDirtyRepaint();
        }
        private void CanvasDown(PointerDownEvent evt)
        {
            root.Focus();
            if (evt.button == 1 || evt.button == 2)
            { panning = true; panButton = evt.button; pointerOrigin = evt.position; panOrigin = Pan; canvas.CapturePointer(evt.pointerId); evt.StopPropagation(); return; }
            if (evt.button != 0) return;
            wireFrom = null;
            selectedEdge = HitEdge(canvas.WorldToLocal(evt.position)); selectedNode = null;
            if (!(evt.shiftKey || evt.ctrlKey || evt.commandKey)) selection.Clear();
            if (selectedEdge == null && Simulation == null)
            {
                boxing = true; boxOrigin = canvas.WorldToLocal(evt.position); boxInitial = new HashSet<string>(selection);
                canvas.CapturePointer(evt.pointerId);
            }
            RefreshNodeStyles(); BuildInspector(); wires.MarkDirtyRepaint();
        }
        private void CanvasMove(PointerMoveEvent evt)
        {
            if (panning)
            {
                Vector2 delta = (Vector2)evt.position - pointerOrigin;
                VisibleDocument.panX = panOrigin.x + delta.x; VisibleDocument.panY = panOrigin.y + delta.y; ApplyView();
            }
            if (wireFrom != null) { wirePointer = canvas.WorldToLocal(evt.position); wires.MarkDirtyRepaint(); }
            if (boxing) UpdateMarquee(canvas.WorldToLocal(evt.position));
        }
        private void CanvasUp(PointerUpEvent evt)
        {
            if (wireFrom != null && canvas.worldBound.Contains(evt.position))
            {
                var target = WireTargetAt(canvas.WorldToLocal(evt.position));
                if (target != null) Try(() => FinishWire(target.id));
            }
            if (panning && panButton == 1 && Vector2.Distance(pointerOrigin, evt.position) < 5) ShowCanvasMenu(evt.position);
            panning = false; boxing = false; marquee.style.display = DisplayStyle.None;
            if (canvas.HasPointerCapture(evt.pointerId)) canvas.ReleasePointer(evt.pointerId);
            RefreshMeta();
        }

        private StudioNode WireTargetAt(Vector2 point)
        {
            bool dataWire = wirePort == SpawnResultPort;
            float nearest = 18 * 18;
            StudioNode target = null;
            foreach (var node in VisibleDocument.nodes)
            {
                if (node == null || (dataWire ? node.type != "wait_region" : node.type == "start" || node.type == "on_signal"))
                    continue;
                float distance = (ToCanvas(dataWire ? DataInputPoint(node) : InputPoint(node)) - point).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                target = node;
            }
            return target;
        }
        private void CanvasWheel(WheelEvent evt)
        {
            Vector2 local = canvas.WorldToLocal(evt.mousePosition), graphPoint = ToGraph(local);
            VisibleDocument.zoom = Mathf.Clamp(VisibleDocument.zoom * Mathf.Exp(-evt.delta.y * .055f), .3f, 1.7f);
            Vector2 pan = local - graphPoint * VisibleDocument.zoom;
            VisibleDocument.panX = pan.x; VisibleDocument.panY = pan.y; ApplyView(); RefreshMeta(); evt.StopPropagation();
        }
        private void EndGesture()
        { if (gestureBefore != null && Simulation == null) history.Record(gestureBefore, Document); draggingNode = null; gestureBefore = null; RefreshMeta(); }

        private void DrawGrid(MeshGenerationContext context)
        {
            if (VisibleDocument == null) return;
            Painter2D painter = context.painter2D;
            painter.strokeColor = new Color(.19f, .23f, .28f, .44f); painter.lineWidth = 1;
            float spacing = 32 * VisibleDocument.zoom;
            float ox = (Pan.x % spacing + spacing) % spacing, oy = (Pan.y % spacing + spacing) % spacing;
            painter.BeginPath();
            for (float x = ox; x < canvas.contentRect.width; x += spacing) { painter.MoveTo(new Vector2(x, 0)); painter.LineTo(new Vector2(x, canvas.contentRect.height)); }
            for (float y = oy; y < canvas.contentRect.height; y += spacing) { painter.MoveTo(new Vector2(0, y)); painter.LineTo(new Vector2(canvas.contentRect.width, y)); }
            painter.Stroke();
        }
        private void DrawWires(MeshGenerationContext context)
        {
            if (VisibleDocument == null) return;
            Painter2D painter = context.painter2D;
            foreach (var (id, from, to, dataWire) in GraphWires())
            {
                Color color = id == selectedEdge ? new Color(1, .82f, .4f) :
                    dataWire ? DataWireColor : new Color(.43f, .65f, .7f);
                Stroke(painter, ToCanvas(from), ToCanvas(to), color, id == selectedEdge ? 4 : 2.2f);
            }
            if (wireFrom != null && VisibleDocument.Find(wireFrom) is StudioNode source)
            {
                bool dataWire = wirePort == SpawnResultPort;
                Vector2 start = dataWire ? DataOutputPoint(source) :
                    OutputPoint(source, Array.IndexOf(NodeCatalog.Get(source.type).ports, wirePort));
                Stroke(painter, ToCanvas(start), wirePointer, dataWire ? DataWireColor : new Color(.98f, .8f, .4f), 3);
            }
        }

        private IEnumerable<(string id, Vector2 from, Vector2 to, bool dataWire)> GraphWires()
        {
            foreach (var edge in VisibleDocument.edges)
            {
                var source = VisibleDocument.Find(edge.from);
                var destination = VisibleDocument.Find(edge.to);
                var spec = source == null ? null : NodeCatalog.Get(source.type);
                if (spec == null || destination == null) continue;
                int port = Array.IndexOf(spec.ports, edge.port);
                if (port >= 0) yield return (edge.id, OutputPoint(source, port), InputPoint(destination), false);
            }

            foreach (var destination in VisibleDocument.nodes.Where(node => node.type == "wait_region" &&
                         NodeParameters.IsSpawnOutput(node.parameters.target)))
            {
                var source = VisibleDocument.Find(NodeParameters.SpawnNodeId(destination.parameters.target));
                if (source?.SpawnsObject == true)
                    yield return (RegionWirePrefix + destination.id, DataOutputPoint(source), DataInputPoint(destination), true);
            }
        }
        private static void Stroke(Painter2D painter, Vector2 from, Vector2 to, Color color, float width)
        {
            float tangent = Mathf.Max(65, Mathf.Abs(to.x - from.x) * .45f);
            painter.strokeColor = color; painter.lineWidth = width; painter.BeginPath(); painter.MoveTo(from);
            painter.BezierCurveTo(from + Vector2.right * tangent, to + Vector2.left * tangent, to); painter.Stroke();
        }
        private string HitEdge(Vector2 point)
        {
            string closestEdge = null;
            float closestDistance = 7 * 7;
            foreach (var (id, from, to, _) in GraphWires())
            {
                Vector2 a = ToCanvas(from), d = ToCanvas(to);
                float tangent = Mathf.Max(65, Mathf.Abs(d.x - a.x) * .45f);
                Vector2 b = a + Vector2.right * tangent, c = d + Vector2.left * tangent, previous = a;
                for (int i = 1; i <= 32; i++)
                {
                    float t = i / 32f, q = 1 - t;
                    Vector2 next = q * q * q * a + 3 * q * q * t * b + 3 * q * t * t * c + t * t * t * d;
                    Vector2 segment = next - previous;
                    Vector2 closest = previous + segment * Mathf.Clamp01(Vector2.Dot(point - previous, segment) / Mathf.Max(.001f, segment.sqrMagnitude));
                    float distance = (point - closest).sqrMagnitude;
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestEdge = id;
                    }
                    previous = next;
                }
            }
            return closestEdge;
        }

        public void AddNode(string type, Vector2 at)
        {
            if (Simulation != null) return;
            if (type == "start" && Document.nodes.Any(n => n.type == "start")) throw new InvalidOperationException("每个流程只能有一个「开始」节点。");
            Edit(() => selectedNode = Document.Add(type, at).id);
            selection.Clear(); selection.Add(selectedNode); selectedEdge = null; BuildGraph(); BuildInspector();
        }

        private void AddAction(PickerOption action, Vector2 position)
        {
            if (Simulation != null) return;
            StudioNode node = null;
            Edit(() =>
            {
                node = Document.Add("run_action", position);
                node.parameters.actionGuid = action.Key;
            });
            BuildGraph();
            SelectNode(node.id);
        }

        private void ChooseNextStep(StudioNode source, string port, string label, StudioEdge previous)
        {
            var options = NodeCatalog.All.Where(spec => spec.Allowed(Document.kind) && spec.type != "start" &&
                    spec.type != "on_signal" && (previous == null || spec.ports.Length != 0))
                .Select(spec => new PickerOption("new:" + spec.type, spec.label, "添加 · " + spec.group, spec.description)).ToList();
            if (Document.kind == "event")
                options.AddRange(actionLibrary.Values.Select(action => new PickerOption("action:" + action.Key,
                    action.Label, "添加 · 玩法", action.Detail)));
            options.AddRange(Document.nodes.Where(node => Document.CanConnect(source.id, node) && node.id != previous?.to)
                .Select(node => new PickerOption("existing:" + node.id, node.Name, "连接已有节点", NodeReferenceDetail(node))));
            OpenPicker((previous == null ? "选择下一步 · " : "调整下一步 · ") + label, options,
                "暂无可连接的节点。", key =>
                {
                    if (key.StartsWith("existing:", StringComparison.Ordinal))
                    {
                        Connect(source.id, port, key.Substring("existing:".Length));
                        return;
                    }
                    StudioNode added = null;
                    Edit(() =>
                    {
                        var destination = Document.Find(previous?.to);
                        Vector2 position = source.Position + new Vector2(NodeWidth + 80, 0);
                        int portIndex = Array.IndexOf(NodeCatalog.Get(source.type).ports, port);
                        if (portIndex > 0) position.y += portIndex * 200;
                        if (destination != null && (destination.Position - position).sqrMagnitude < 35000)
                            destination.Position += new Vector2(NodeWidth + 80, 0);
                        while (Document.nodes.Any(node => Mathf.Abs(position.x - node.x) < NodeWidth + 18 &&
                            Mathf.Abs(position.y - node.y) < 165)) position.y += 190;
                        bool isAction = key.StartsWith("action:", StringComparison.Ordinal);
                        added = Document.Add(isAction ? "run_action" : key.Substring("new:".Length), position);
                        if (isAction)
                        {
                            var action = actionLibrary[key.Substring("action:".Length)];
                            added.parameters.actionGuid = action.Key;
                        }
                        Document.Connect(source.id, port, added.id);
                        if (destination != null)
                            foreach (string next in NodeCatalog.Get(added.type).ports)
                                Document.Connect(added.id, next, destination.id);
                    });
                    BuildGraph();
                    SelectNode(added.id);
                    RevealNode(added);
                });
        }
        public void Connect(string from, string port, string to)
        { if (Simulation != null) return; Edit(() => Document.Connect(from, port, to)); wires.MarkDirtyRepaint(); BuildInspector(); }
        private void FinishWire(string to)
        {
            if (wirePort == SpawnResultPort)
            {
                Edit(() => Document.ConnectRegionCenter(wireFrom, to));
                SelectNode(to);
                Notice("范围中心已连接。");
            }
            else
            {
                Connect(wireFrom, wirePort, to);
                Notice("连线已更新。");
            }
            wireFrom = null;
            wires.MarkDirtyRepaint();
        }

        private static void ClearRegionCenter(StudioNode node)
        {
            node.parameters.target = "";
            node.parameters.destinationPoint = "";
        }
        public void SelectNode(string id)
        { selection.Clear(); if (id != null) selection.Add(id); selectedNode = id; selectedEdge = null; root.Focus(); RefreshNodeStyles(); BuildInspector(); wires.MarkDirtyRepaint(); }
        private void Edit(Action action)
        {
            if (Simulation != null) return;
            string before = Document.ToJson(); action(); history.Record(before, Document); RefreshMeta(); RefreshSummaries();
            RefreshNodeIssues();
        }
        private void RefreshSummaries()
        {
            foreach (StudioNode node in VisibleDocument.nodes)
            {
                if (summaries.TryGetValue(node.id, out Label summary)) summary.text = NodeCatalog.Summary(VisibleDocument, node, ReferenceLabel, AssetLabel);
                if (node.type == "choice" && cards.TryGetValue(node.id, out VisualElement card))
                    for (int i = 0; i < 2; i++) card.Q<Label>("route-label-" + i).text = RouteLabel(node, i);
            }
        }
        public void Undo()
        { if (Simulation != null) return; Document = history.Undo(Document); ClearSelection(); RefreshAll(); }
        public void Redo()
        { if (Simulation != null) return; Document = history.Redo(Document); ClearSelection(); RefreshAll(); }
        public void DeleteSelection()
        {
            if (Simulation != null) return;
            if (selection.Count == 0 && selectedEdge == null) { Notice("先选择要删除的节点或连线。"); return; }
            Edit(() =>
            {
                if (selection.Count > 0)
                {
                    Document.nodes.RemoveAll(n => selection.Contains(n.id)); Document.edges.RemoveAll(e => selection.Contains(e.from) || selection.Contains(e.to));
                    foreach (var node in Document.nodes.Where(node => node.type == "wait_region" &&
                                 NodeParameters.IsSpawnOutput(node.parameters.target) &&
                                 selection.Contains(NodeParameters.SpawnNodeId(node.parameters.target))))
                        ClearRegionCenter(node);
                    if (selection.Contains(Document.entry)) Document.entry = null;
                }
                else if (selectedEdge.StartsWith(RegionWirePrefix, StringComparison.Ordinal))
                {
                    var target = Document.Find(selectedEdge.Substring(RegionWirePrefix.Length));
                    if (target != null) ClearRegionCenter(target);
                }
                else Document.edges.RemoveAll(e => e.id == selectedEdge);
            });
            int count = selection.Count; ClearSelection(); CloseContextMenu(); BuildGraph(); BuildInspector(); RefreshMeta();
            Notice(count > 0 ? "已删除 " + count + " 个节点及其连线。可撤销恢复。" : "连线已断开。");
        }
        private void DuplicateNode()
        {
            if (Simulation != null || !selection.Any(id => Document.Find(id)?.type != "start")) return;
            ClipboardData previous = clipboard; int previousCount = pasteCount;
            CopySelection(); PasteSelection();
            clipboard = previous; pasteCount = previousCount; RefreshSelectionBar();
        }
        private void RefreshNodeStyles()
        {
            foreach (var pair in cards)
            {
                pair.Value.EnableInClassList("selected", selection.Contains(pair.Key));
                bool current = Simulation != null && Simulation.CurrentNode?.id == pair.Key;
                pair.Value.EnableInClassList("executing", current && Simulation.Status != SessionStatus.Failed);
                pair.Value.EnableInClassList("failed", current && Simulation.Status == SessionStatus.Failed);
                pair.Value.EnableInClassList("completed", Simulation?.Completed.Contains(VisibleDocument.id + "/" + pair.Key) ?? false);
            }
            RefreshSelectionBar();
        }
        public void FitGraph()
        {
            if (VisibleDocument.nodes.Count == 0 || canvas.contentRect.width < 50) return;
            float minX = VisibleDocument.nodes.Min(n => n.x), minY = VisibleDocument.nodes.Min(n => n.y);
            float width = VisibleDocument.nodes.Max(n => n.x + NodeWidth) - minX;
            float height = VisibleDocument.nodes.Max(n => n.y + NodeHeight(n)) - minY;
            VisibleDocument.zoom = Mathf.Clamp(Mathf.Min((canvas.contentRect.width - 65) / width, (canvas.contentRect.height - 65) / height), .3f, 1.15f);
            VisibleDocument.panX = (canvas.contentRect.width - width * VisibleDocument.zoom) / 2 - minX * VisibleDocument.zoom;
            VisibleDocument.panY = (canvas.contentRect.height - height * VisibleDocument.zoom) / 2 - minY * VisibleDocument.zoom;
            ApplyView(); RefreshMeta();
        }

        private void RevealNode(StudioNode node)
        {
            if (node == null || canvas.contentRect.width < 50) return;
            Vector2 screen = ToCanvas(node.Position + new Vector2(NodeWidth / 2, 75));
            var view = canvas.contentRect;
            if (screen.x >= 75 && screen.x <= view.width - 75 && screen.y >= 55 && screen.y <= view.height - 55) return;
            Vector2 offset = view.center - screen;
            Document.panX += offset.x;
            Document.panY += offset.y;
            ApplyView();
            RefreshMeta();
        }

        private void BuildInspector()
        {
            inspector.Clear();
            nodeIssues = null;
            refreshPlacementPreview = null;
            placementPicked = null;
            if (selection.Count > 1)
            {
                inspector.Add(Text("已选择 " + selection.Count + " 个节点", "inspector-title"));
                inspector.Add(Text("拖动任意选中节点的标题可一起移动。可批量复制、删除，或使用下方按钮对齐。", "muted wrap"));
                inspector.Add(Button("左对齐", () => AlignSelection(false)));
                inspector.Add(Button("顶端对齐", () => AlignSelection(true)));
                inspector.Add(Button("复制选中节点", CopySelection));
                inspector.Add(Button("删除选中节点", DeleteSelection, "danger"));
                return;
            }
            StudioNode node = VisibleDocument.Find(selectedNode);
            if (node == null)
            {
                if (selectedEdge != null)
                {
                    bool dataWire = selectedEdge.StartsWith(RegionWirePrefix, StringComparison.Ordinal);
                    inspector.Add(Text(dataWire ? "生成结果 → 范围中心" : "已选择连线", "inspector-title"));
                    if (Simulation == null) inspector.Add(Button("断开连线", DeleteSelection, "danger"));
                }
                else
                {
                    inspector.Add(Text(KindLabel(VisibleDocument.kind), "inspector-title"));
                    inspector.Add(Text("选择节点查看设置，或从「开始」添加第一步。", "muted wrap"));
                    if (Simulation == null && Document.Find(Document.entry) != null)
                        inspector.Add(Button("选中开始节点", () => SelectNode(Document.entry), "primary"));
                    if (Simulation == null)
                    {
                        Field(inspector, "流程名称", Document.title, value => Edit(() => Document.title = value));
                        if (Document.kind == "event" && Document.prefabInputs.Count != 0) BuildPrefabInputs();
                    }
                    inspector.Add(Text("节点 " + VisibleDocument.nodes.Count + "   /   连线 " + VisibleDocument.edges.Count, "muted"));
                }
                return;
            }
            NodeSpec spec = NodeCatalog.Get(node.type);
            inspector.Add(Text(spec.label, "inspector-title")); inspector.Add(Text(spec.description, "muted wrap"));
            var fields = Box("parameter-fields"); inspector.Add(fields);
            BuildNodeFields(fields, node);
            nodeIssues = Text("", "node-issues wrap");
            fields.Add(nodeIssues);
            RefreshNodeIssues();
            if (Simulation == null && Document.kind == "event") BuildPublication(fields, node);
            fields.SetEnabled(Simulation == null);
            if (spec.ports.Length > 0) inspector.Add(Text("下一步", "group-label"));
            for (int i = 0; i < spec.ports.Length; i++)
            {
                string port = spec.ports[i]; StudioEdge edge = VisibleDocument.Output(node.id, port);
                string route = RouteLabel(node, i);
                string next = edge != null ? VisibleDocument.Find(edge.to)?.Name : null;
                var row = Box("connection-row"); inspector.Add(row);
                row.Add(Text(route + (next == null ? " · 未连接" : " → " + next), "wrap"));
                if (Simulation == null)
                {
                    bool firstStep = node.type == "start" && VisibleDocument.nodes.Count == 2 && next != null;
                    var choose = Button(firstStep ? "添加第一步" : next == null ? "选择下一步" : "更改下一步",
                        () => ChooseNextStep(node, port, route, edge), "quiet");
                    choose.name = "choose-next-" + port;
                    row.Add(choose);
                    if (edge != null) row.Add(Button("断开", () =>
                    {
                        Edit(() => Document.edges.Remove(edge));
                        wires.MarkDirtyRepaint();
                        BuildInspector();
                    }, "small"));
                }
            }
            if (Simulation == null)
            {
                var buttons = Box("row inspector-actions"); inspector.Add(buttons);
                if (node.type != "start") buttons.Add(Button("复制节点", DuplicateNode));
                buttons.Add(Button("删除节点", DeleteSelection, "danger"));
            }
            var remarks = Field(inspector, "备注", node.remarks ?? "", value => Edit(() => node.remarks = value), true);
            remarks.name = "node-remarks";
            remarks.AddToClassList("node-remarks");
            remarks.isReadOnly = Simulation != null;
        }

        private string RouteLabel(StudioNode node, int index)
        {
            var spec = NodeCatalog.Get(node.type);
            if (node.type == "choice")
            {
                string option = index == 0 ? node.parameters.firstChoice : node.parameters.secondChoice;
                if (!string.IsNullOrWhiteSpace(option)) return option;
            }
            return spec.portLabels[index];
        }

        private void RefreshNodeIssues()
        {
            if (nodeIssues == null || VisibleDocument.Find(selectedNode) is not StudioNode node) return;
            var problems = FlowValidation.ParameterIssues(VisibleDocument, node, Files.ResolveAsset)
                .Concat(SelectionIssues(VisibleDocument, node)).Distinct().ToArray();
            nodeIssues.text = string.Join("\n", problems);
            nodeIssues.style.display = problems.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void BuildPublication(VisualElement fields, StudioNode node)
        {
            var candidates = FlowExposedParameter.Publishable(node).ToList();
            if (candidates.Count == 0) return;
            var settings = new Foldout { text = "更多设置", value = false };
            fields.Add(settings);
            var foldout = new Foldout { name = "published-parameters", text = "可公开的设置", value = false };
            settings.Add(foldout);
            foreach (var field in candidates)
            {
                var setting = node.published?.FirstOrDefault(entry => entry?.key == field.Key);
                Bool(foldout, "公开「" + field.Label + "」", setting != null, show =>
                {
                    Edit(() =>
                    {
                        node.published ??= new List<PublishedFlowParameter>();
                        if (show) node.published.Add(new PublishedFlowParameter { key = field.Key, label = field.Label });
                        else node.published.RemoveAll(entry => entry?.key == field.Key);
                    });
                    BuildInspector();
                    var refreshed = inspector.Q<Foldout>(name: "published-parameters");
                    if (refreshed != null)
                    {
                        refreshed.GetFirstAncestorOfType<Foldout>()?.SetValueWithoutNotify(true);
                        refreshed.value = true;
                    }
                });
                if (setting != null)
                    Field(foldout, "显示名称", setting.label, label => Edit(() => setting.label = label.Trim()));
            }
        }

        private void BuildEventPicker(VisualElement fields, NodeParameters p)
        {
            var titles = SavedEvents().ToDictionary(entry => entry.file, entry => entry.title + "  /  " + entry.file);
            titles[""] = "选择事件";
            string current = p.eventFile ?? "";
            if (!titles.ContainsKey(current)) titles.Add(current, current);
            var picker = new DropdownField("事件", titles.Keys.ToList(), current, file => titles[file], file => titles[file]);
            picker.RegisterValueChangedCallback(evt => { Edit(() => p.eventFile = evt.newValue); BuildInspector(); });
            fields.Add(picker);
            if (!string.IsNullOrEmpty(current))
                inspector.Add(Button("打开事件", () => Guard(() => OpenDocument(current))));
        }

        private IEnumerable<(string file, string title)> SavedEvents()
        {
            foreach (string file in Files.List().Where(FlowFileStore.IsEvent))
            {
                string title;
                try { title = Files.ReadEvent(file).graph.title; }
                catch (Exception) { continue; }
                yield return (file, title);
            }
        }

        private void OpenEventLibrary()
        {
            if (Simulation != null || Document.kind != "main") return;
            var modal = Modal("添加事件", "选择事件后，分别设置成功和失败分支。");
            var list = new ScrollView(); list.style.maxHeight = 340; modal.Add(list);
            foreach (var entry in SavedEvents())
                list.Add(Button(entry.title + "\n" + entry.file, () =>
                {
                    CloseModal();
                    Edit(() =>
                    {
                        var position = (canvas.contentRect.center - new Vector2(Document.panX, Document.panY)) / Document.zoom;
                        var node = Document.Add("call_event", position);
                        node.parameters.eventFile = entry.file;
                        selectedNode = node.id;
                    });
                    BuildGraph();
                    SelectNode(selectedNode);
                }, "file-button"));
            modal.Add(Button("关闭", CloseModal));
        }

        private void ChoiceField(VisualElement fields, string label, string value,
            IEnumerable<(string value, string label)> choices, Action<string> assign)
        {
            var names = choices.GroupBy(choice => choice.value).ToDictionary(group => group.Key, group => group.First().label);
            value = value ?? "";
            if (!names.ContainsKey(value)) names.Add(value, "当前选项不可用");
            var picker = new DropdownField(label, names.Keys.ToList(), value, key => names[key], key => names[key]);
            picker.RegisterValueChangedCallback(evt =>
            {
                Edit(() => assign(evt.newValue));
                BuildInspector();
            });
            fields.Add(picker);
        }
        private static TextField Field(VisualElement parent, string label, string value, Action<string> changed, bool multiline = false)
        {
            var field = new TextField(label) { value = value, isDelayed = true, multiline = multiline };
            field.AddToClassList("parameter"); parent.Add(field); field.RegisterValueChangedCallback(evt => changed(evt.newValue));
            return field;
        }
        private static void Number(VisualElement parent, string label, float value, Action<float> changed)
        { var field = new FloatField(label) { value = value, isDelayed = true }; field.AddToClassList("parameter"); parent.Add(field); field.RegisterValueChangedCallback(evt => changed(evt.newValue)); }
        private static void Bool(VisualElement parent, string label, bool value, Action<bool> changed)
        { var field = new Toggle(label) { value = value }; field.AddToClassList("parameter"); parent.Add(field); field.RegisterValueChangedCallback(evt => changed(evt.newValue)); }

        private void RefreshAll()
        { wireFrom = null; selection.RemoveWhere(id => VisibleDocument.Find(id) == null); CloseContextMenu(); BuildPalette(); BuildGraph(); BuildInspector(); BuildSimulationPane(); RefreshMeta(); }
        private void RefreshMeta()
        {
            if (Document == null) return;
            titleLabel.text = VisibleDocument.title + (Simulation == null && IsDirty ? "  •" : "");
            kindLabel.text = KindLabel(VisibleDocument.kind);
            fileLabel.text = Simulation != null ? "模拟路径  /  " + Simulation.Breadcrumb :
                "流程文件  /  " + (CurrentFile ?? "尚未保存");
            undoButton.SetEnabled(Simulation == null && history.CanUndo); redoButton.SetEnabled(Simulation == null && history.CanRedo);
            saveButton.SetEnabled(Simulation == null); simulateButton.SetEnabled(Simulation == null);
            bool isMain = Document.kind == "main" && Simulation == null;
            addEventButton.style.display = isMain ? DisplayStyle.Flex : DisplayStyle.None;
            startupButton.style.display = isMain ? DisplayStyle.Flex : DisplayStyle.None;
            bool isStartup = isMain && CurrentFile != null && IsStartupFlow();
            startupButton.text = isStartup ? "当前游戏入口" : "设为游戏入口";
            startupButton.SetEnabled(!isStartup || IsDirty);
            RefreshSelectionBar();
        }

        private bool IsStartupFlow()
        {
            try { return string.Equals(CurrentFile, Files.StartupFile, StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        private void SetStartupFlow()
        {
            if (Simulation != null || Document.kind != "main") return;
            var errors = CheckGraph(out _);
            if (errors.Count != 0) { ValidateDocument(); return; }
            SaveCurrent(() =>
            {
                Files.SetStartupFile(CurrentFile);
                RefreshMeta();
                Notice("游戏入口已设为 " + Document.title);
            });
        }
        private void Notice(string message, bool error = false)
        { if (statusLabel == null) return; statusLabel.text = message; statusLabel.EnableInClassList("error-text", error); }

        public void OpenDocument(string file)
        {
            if (Simulation != null) StopSimulation();
            string name = FlowFileStore.FileName(file);
            var graph = LoadEditorGraph(name);
            CurrentFile = name;
            ShowGraph(graph);
        }

        private FlowDocument LoadEditorGraph(string file)
        {
            var graph = Files.Load(file);
            MigrateLegacyPlacement(graph);
            return graph;
        }

        private void ShowGraph(FlowDocument graph)
        {
            Document = graph;
            baseline = graph.Clone();
            savedJson = graph.ToJson();
            history.Clear();
            ClearSelection();
            RefreshAll();
            Notice("已打开 " + graph.title);
        }

        private static string KindLabel(string kind) => kind == "main" ? "整体流程" : "事件流程";

        private bool TryOpenEvent(StudioNode node)
        {
            if (Simulation != null || node.type != "call_event" || string.IsNullOrWhiteSpace(node.parameters.eventFile)) return false;
            Try(() => Guard(() => OpenDocument(node.parameters.eventFile)));
            return true;
        }

        private void NewDocument(string kind) => Guard(() =>
        {
            Document = FlowDocument.Empty(kind, "新" + KindLabel(kind));
            baseline = null;
            CurrentFile = null; savedJson = ""; history.Clear(); ClearSelection(); RefreshAll();
            SelectNode(Document.entry);
            Notice("选择「添加第一步」开始编排。");
        });
        public void SaveCurrent(Action after = null)
        {
            if (Simulation != null) return;
            // Commit delayed parameter fields before saving, including Ctrl+S while typing.
            root.Focus();
            if (CurrentFile == null || (Document.kind == "event" && !FlowFileStore.IsEvent(CurrentFile))) { SaveAs(after); return; }
            SaveTo(CurrentFile);
            after?.Invoke();
        }

        private void SaveTo(string file)
        {
            var draft = Document.Clone();
            if (file == CurrentFile) Files.MergePublishedValues(file, draft, baseline);
            Files.Save(file, draft);
            CurrentFile = file;
            Document = draft;
            baseline = draft.Clone();
            savedJson = draft.ToJson();
            RefreshAll();
            Notice("已保存 " + file);
        }

        private void SaveAs(Action after = null)
        {
            if (Simulation != null) return;
            bool saveEvent = Document.kind == "event";
            VisualElement modal = Modal("保存流程文件", "");
            var name = new TextField("文件名") { value = FlowFileStore.FileName(CurrentFile ?? ("new_" + Document.kind), saveEvent) }; modal.Add(name);
            modal.Add(Text("保存位置：" + (saveEvent ? Files.EventRoot : Files.Root), "muted wrap path-label"));
            modal.Add(Button("保存", () =>
            {
                string file = FlowFileStore.FileName(name.value, saveEvent);
                if (FlowFileStore.IsEvent(file) != saveEvent) { Notice("只有事件流程可保存为 .event 文件。", true); return; }
                Action save = () => { SaveTo(file); CloseModal(); after?.Invoke(); };
                if (File.Exists(Files.PathFor(file)) && file != CurrentFile)
                {
                    VisualElement confirm = Modal("覆盖已有文件？", "将替换文件「" + file + "」。");
                    confirm.Add(Button("覆盖并保存", save, "primary")); confirm.Add(Button("取消", CloseModal));
                }
                else save();
            }, "primary"));
            modal.Add(Button("取消", CloseModal)); name.Focus();
        }
        private void OpenLibrary()
        {
            if (Simulation != null) { Notice("请先停止模拟，再打开其他文件。", true); return; }
            VisualElement modal = Modal("流程文件", "选择整体流程或事件流程。");
            var list = new ScrollView(); list.style.maxHeight = 340; modal.Add(list);
            foreach (string file in Files.List())
            {
                string caption = file;
                try { FlowDocument doc = Files.Load(file); caption = KindLabel(doc.kind) + "  /  " + doc.title + "\n" + file; } catch { caption += "（无法读取）"; }
                list.Add(Button(caption, () => { CloseModal(); Guard(() => OpenDocument(file)); }, "file-button"));
            }
            modal.Add(Button("关闭", CloseModal));
        }
        private void Guard(Action action)
        {
            if (Simulation != null) { Notice("请先停止模拟。", true); return; }
            if (!IsDirty) { action(); return; }
            VisualElement modal = Modal("保存当前修改？", "当前流程有尚未保存的更改。");
            modal.Add(Button("保存并继续", () => { CloseModal(); SaveCurrent(action); }, "primary"));
            modal.Add(Button("放弃修改并继续", () => { CloseModal(); action(); })); modal.Add(Button("取消", CloseModal));
        }
        private VisualElement Modal(string title, string subtitle)
        {
            overlay.Clear();
            overlay.style.display = DisplayStyle.Flex;
            var modal = Box("modal");
            overlay.Add(modal);
            modal.Add(Text(title, "modal-title"));
            if (!string.IsNullOrWhiteSpace(subtitle)) modal.Add(Text(subtitle, "muted wrap"));
            return modal;
        }
        private void CloseModal() { overlay.Clear(); overlay.style.display = DisplayStyle.None; root.Focus(); }
        private void ValidateDocument()
        {
            var errors = CheckGraph(out _);
            ShowIssues(errors);
        }

        private void ShowIssues(List<FlowIssue> errors)
        {
            VisualElement modal = Modal(errors.Count == 0 ? "检查通过" : "发现 " + errors.Count + " 个问题",
                errors.Count == 0 ? "可以开始模拟。" : "选择问题可定位对应节点。");
            var list = new ScrollView(); list.style.maxHeight = 330; modal.Add(list);
            foreach (var error in errors)
            {
                if (error.Node == null || string.IsNullOrEmpty(error.Node.id))
                {
                    list.Add(Text(error.ToString(), "validation-error wrap"));
                    continue;
                }
                var row = Button(error.ToString(), () =>
                {
                    CloseModal();
                    void Locate()
                    {
                        if (error.File != null) OpenDocument(error.File);
                        SelectNode(error.Node.id);
                        RevealNode(Document.Find(error.Node.id));
                    }
                    if (error.File == null) Locate(); else Guard(Locate);
                }, "diagnostic-button");
                list.Add(row);
            }
            modal.Add(Button("返回编辑", CloseModal, "primary"));
        }

        public void StartSimulation()
        {
            if (Simulation != null) return;
            var errors = CheckGraph(out var graph);
            if (errors.Count > 0) { ShowIssues(errors); return; }
            simulationActions = new SimulationActions(); Simulation = new FlowSession(graph, LoadEditorGraph, simulationActions);
            paused = false; ClearSelection(); SetSimulationPanel(true);
            Simulation.Changed += SimulationChanged; RefreshAll();
            Notice("模拟已开始。");
        }

        private List<FlowIssue> CheckGraph(out FlowDocument graph)
        {
            graph = Document.Clone();
            try
            {
                if (FlowFileStore.IsEvent(CurrentFile)) Files.MergePublishedValues(CurrentFile, graph, baseline);
                return FlowValidation.CheckDetails(graph, LoadEditorGraph, Files.ResolveAsset, SelectionIssues);
            }
            catch (Exception error) { return new List<FlowIssue> { new FlowIssue(graph, null, null, error.Message) }; }
        }
        public void StopSimulation()
        {
            if (Simulation == null) return;
            Simulation.Changed -= SimulationChanged; Simulation.Stop(); Simulation = null; simulationActions = null;
            SetSimulationPanel(false);
            ClearSelection(); RefreshAll(); Notice("模拟已结束。");
        }
        private void SimulationChanged()
        {
            if (Simulation.CurrentDocument != null && renderedDocument != VisibleDocument.id) { ClearSelection(); BuildGraph(); }
            RefreshNodeStyles(); RefreshMeta(); BuildSimulationPane();
        }
        private void Resolve(string port)
        {
            // A single step may have selected a wait node without entering it yet.
            // Initialize that node before delivering the external condition.
            string waitingId = Simulation.CurrentNode?.id;
            Simulation.Advance(0, 1);
            if (Simulation.CurrentNode?.id == waitingId && Simulation.Status == SessionStatus.Running)
            { simulationActions.Resolve(port); Simulation.Advance(0); }
        }
        private void BuildSimulationPane()
        {
            simulationPane.Clear();
            if (Simulation == null)
            {
                simulationPane.Add(Text("流程模拟", "inspector-title"));
                simulationPane.Add(Text("预览节点顺序。场景表现可在游戏中查看。", "muted wrap"));
                simulationPane.Add(Button("▶ 开始模拟", StartSimulation, "primary")); return;
            }
            string status = Simulation.Status == SessionStatus.Running ? paused ? "模拟已暂停" : "模拟运行中" :
                Simulation.Status == SessionStatus.Completed ? "模拟完成" :
                Simulation.ExecutionError == null ? "流程结果为失败" : "模拟出错";
            simulationPane.Add(Text(status, "inspector-title"));
            simulationPane.Add(Text(Simulation.Message, "wrap"));
            var controls = Box("row"); simulationPane.Add(controls);
            controls.Add(Button(paused ? "继续" : "暂停", () => { paused = !paused; BuildSimulationPane(); }));
            controls.Add(Button("单步", () => { paused = true; Simulation.Advance(0, 1); BuildSimulationPane(); }));
            controls.Add(Button("停止", StopSimulation, "danger"));
            StudioNode node = Simulation.CurrentNode;
            if (Simulation.Status == SessionStatus.Running && node != null)
            {
                switch (node.type)
                {
                    case "wait_near": simulationPane.Add(Button("满足距离条件", () => Resolve("next"), "primary")); break;
                    case "wait_region": simulationPane.Add(Button("进入范围", () => Resolve("next"), "primary")); break;
                    case "wait_travel": simulationPane.Add(Button("完成移动", () => Resolve("next"), "primary")); break;
                    case "wait_signal":
                        simulationPane.Add(Button("模拟操作 · " + FlowSignals.Label(node.parameters.signal), () =>
                        {
                            Simulation.Advance(0, 1);
                            if (Simulation.ReceiveSignal(node.parameters.signal)) Simulation.Advance(0);
                        }, "primary"));
                        break;
                    case "wait_confirmation":
                        simulationPane.Add(Text(node.parameters.note, "prompt-preview wrap"));
                        simulationPane.Add(Button("确认提示", () => Resolve("next"), "primary"));
                        break;
                    case "choice":
                        simulationPane.Add(Text(node.parameters.note, "prompt-preview wrap"));
                        simulationPane.Add(Button(node.parameters.firstChoice, () => Resolve("first"), "primary"));
                        simulationPane.Add(Button(node.parameters.secondChoice, () => Resolve("second")));
                        break;
                    case "branch" when node.parameters.checkDistance:
                        simulationPane.Add(Button("条件满足", () => Resolve("yes"), "primary"));
                        simulationPane.Add(Button("条件不满足", () => Resolve("no")));
                        break;
                    case "delay": simulationPane.Add(Button("跳过等待", () => Resolve("next"))); break;
                    case "run_action":
                        var results = Box("row"); simulationPane.Add(results);
                        results.Add(Button("玩法成功", () => Resolve("success"), "primary")); results.Add(Button("玩法失败", () => Resolve("failure"), "danger")); break;
                }
            }
            foreach (string effect in simulationActions.Effects.TakeLast(5)) simulationPane.Add(Text("↳ " + effect, "effect-line wrap"));
            simulationPane.Add(Text("模拟记录", "group-label"));
            foreach (string line in Simulation.Log.TakeLast(16)) simulationPane.Add(Text(line, "log-line wrap"));
        }
        private void OnKey(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                wireFrom = null; wires.MarkDirtyRepaint(); CloseContextMenu(); CloseModal(); boxing = false; marquee.style.display = DisplayStyle.None; evt.StopPropagation(); return;
            }
            if (overlay.resolvedStyle.display != DisplayStyle.None) return;
            var target = evt.target as VisualElement;
            bool textEditing = evt.target is TextElement || evt.target is TextField || evt.target is FloatField ||
                target?.GetFirstAncestorOfType<TextField>() != null || target?.GetFirstAncestorOfType<FloatField>() != null;
            if (evt.ctrlKey || evt.commandKey)
            {
                if (evt.keyCode == KeyCode.S) { Try(() => SaveCurrent()); evt.StopPropagation(); return; }
                if (textEditing) return;
                if (evt.keyCode == KeyCode.Z) { if (evt.shiftKey) Redo(); else Undo(); evt.StopPropagation(); }
                if (evt.keyCode == KeyCode.Y) { Redo(); evt.StopPropagation(); }
                if (evt.keyCode == KeyCode.D && Simulation == null) { DuplicateNode(); evt.StopPropagation(); }
                if (evt.keyCode == KeyCode.A && Simulation == null) { SelectAll(); evt.StopPropagation(); }
                if (evt.keyCode == KeyCode.C && Simulation == null) { CopySelection(); evt.StopPropagation(); }
                if (evt.keyCode == KeyCode.V && Simulation == null) { PasteSelection(); evt.StopPropagation(); }
            }
            if (!textEditing && (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)) { DeleteSelection(); evt.StopPropagation(); }
        }
    }
}
