using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using WhiteWhale.Flow;

namespace WhiteWhale.FlowStudio
{
    public sealed partial class FlowStudioUI
    {
        private sealed class PickerOption
        {
            public readonly string Key, Label, Group, Detail;

            public PickerOption(string key, string label, string group = "", string detail = "")
            { Key = key; Label = label; Group = group; Detail = detail; }

            public bool Matches(string query) => string.IsNullOrWhiteSpace(query) ||
                (Label + " " + Group + " " + Detail).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class Place
        {
            public string Id;
            public Vector3 Position;
            public Quaternion Rotation;
            public bool Region;
            public float RegionRadius;
            public bool RegionHorizontalOnly;
        }

        private sealed class SceneObject
        {
            public string Key, Name, Scene;
            public Place Origin;
            public Place[] Points;
            public bool Pickable;
            public readonly Dictionary<string, (bool subject, bool target)> Actions =
                new Dictionary<string, (bool subject, bool target)>();
        }

        private readonly Dictionary<string, SceneObject> sceneBindings = new Dictionary<string, SceneObject>();
        private readonly Dictionary<string, PickerOption> actionLibrary = new Dictionary<string, PickerOption>();
        private Action refreshPlacementPreview;
        private Action<Vector2> placementPicked;

        private static string SceneName(Scene scene) => scene.name switch
        {
            "MainScene" => "房间",
            "Ocean" => "海上",
            _ => scene.name
        };

        private static Place PositionOf(Transform point, string id)
        {
            var region = point.GetComponent<FlowRegion>();
            return new Place
            {
                Id = id, Position = point.position, Rotation = point.rotation,
                Region = region != null, RegionRadius = region != null ? region.Radius : 0,
                RegionHorizontalOnly = region != null && region.HorizontalOnly
            };
        }

        private static IEnumerable<Place> Points(GameObject owner) =>
            FlowAnchor.On(owner != null ? owner.transform : null)
                .GroupBy(anchor => anchor.Id).Where(group => group.Count() == 1)
                .Select(group => group.First()).Select(anchor => PositionOf(anchor.transform, anchor.Id));

        private void LoadSceneBindings()
        {
            sceneBindings.Clear();
            var ambiguous = new HashSet<string>();
            actionLibrary.Clear();
            foreach (var option in AssetOptions(true)) actionLibrary[option.Key] = option;
            var actions = actionLibrary.Keys.Select(guid => (guid, Files.ResolveAsset(guid) as FlowAction))
                .Where(entry => entry.Item2 != null).ToArray();

            void Collect(Scene scene)
            {
                var roots = scene.GetRootGameObjects();
                var bindings = roots.SelectMany(root => root.GetComponentsInChildren<FlowSceneBindings>(true))
                    .SelectMany(component => component.Objects).Where(binding => binding?.target != null &&
                        !string.IsNullOrWhiteSpace(binding.key)).ToList();
                var players = roots.SelectMany(root => root.GetComponentsInChildren<PlayerPickup>(true)).Take(2).ToArray();
                if (players.Length == 1)
                    bindings.Add(new FlowBinding { key = NodeParameters.PlayerObject, target = players[0].gameObject, label = "玩家" });
                foreach (var group in bindings.GroupBy(binding => binding.target))
                {
                    var binding = group.OrderByDescending(item => item.key == NodeParameters.PlayerObject)
                        .ThenByDescending(item => item.key.StartsWith("scene:", StringComparison.Ordinal)).First();
                    var target = binding.target;
                    var entry = new SceneObject
                    {
                        Key = binding.key, Name = binding.DisplayName, Scene = SceneName(scene),
                        Origin = PositionOf(target.transform, ""), Points = Points(target).ToArray(),
                        Pickable = IsPickable(target)
                    };
                    foreach (var (guid, action) in actions)
                        entry.Actions[guid] = (action.AcceptsSubject(target), action.AcceptsTarget(target));
                    foreach (var alias in group)
                    {
                        if (ambiguous.Contains(alias.key)) continue;
                        if (sceneBindings.TryGetValue(alias.key, out var prior) && prior != entry)
                        {
                            sceneBindings.Remove(alias.key);
                            ambiguous.Add(alias.key);
                        }
                        else sceneBindings[alias.key] = entry;
                    }
                }
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) Collect(scene);
            }
#if UNITY_EDITOR
            foreach (var sceneAsset in (bindingScenes ?? Array.Empty<UnityEngine.Object>()).Distinct())
            {
                string path = UnityEditor.AssetDatabase.GetAssetPath(sceneAsset);
                if (sceneAsset is not UnityEditor.SceneAsset || SceneManager.GetSceneByPath(path).isLoaded) continue;
                var preview = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene(path);
                try { Collect(preview); }
                finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview); }
            }
#endif
        }

        private IEnumerable<PickerOption> AssetOptions(bool action)
        {
#if UNITY_EDITOR
            IEnumerable<string> guids = action
                ? UnityEditor.TypeCache.GetTypesDerivedFrom<FlowAction>().Where(type => !type.IsAbstract)
                    .SelectMany(type => UnityEditor.AssetDatabase.FindAssets("t:" + type.Name, new[] { "Assets" })).Distinct()
                : UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (action && Files.ResolveAsset(guid) is FlowAction asset)
                    yield return new PickerOption(guid, asset.DisplayName, "玩法", asset.Description);
                else if (!action && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(path);
                    string group = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
                    yield return new PickerOption(guid, UnityEditor.ObjectNames.NicifyVariableName(name), group);
                }
            }
#else
            foreach (var pair in Files.AvailableAssets())
            {
                if (action && pair.Value is FlowAction asset)
                    yield return new PickerOption(pair.Key, asset.DisplayName, "玩法", asset.Description);
                else if (!action && pair.Value is GameObject prefab)
                    yield return new PickerOption(pair.Key, prefab.name, "物件");
            }
#endif
        }

        private string AssetLabel(string guid)
        {
            var asset = Files.ResolveAsset(guid);
            if (asset is FlowAction action) return action.DisplayName;
#if UNITY_EDITOR
            if (asset != null) return UnityEditor.ObjectNames.NicifyVariableName(asset.name);
#endif
            return asset?.name;
        }

        private GameObject PrefabForReference(FlowDocument document, string reference) =>
            NodeParameters.IsSpawnOutput(reference) && document.Find(NodeParameters.SpawnNodeId(reference)) is StudioNode source
                ? Files.ResolveAsset(document.PrefabGuid(source)) as GameObject : null;

        private string ReferenceLabel(string reference)
        {
            if (reference == NodeParameters.SignalObject) return "玩家拾取的物件";
            if (NodeParameters.IsSpawnOutput(reference))
                return VisibleDocument.Find(NodeParameters.SpawnNodeId(reference))?.Name ?? "生成节点已删除";
            return reference != null && sceneBindings.TryGetValue(reference, out var binding) ? binding.Name : "场景物件不可用";
        }

        private string NodeReferenceDetail(StudioNode node)
        {
            if (string.IsNullOrWhiteSpace(node.remarks))
                return NodeCatalog.Summary(VisibleDocument, node, ReferenceLabel, AssetLabel);
            string line = node.remarks.Trim().Split('\n', '\r')[0];
            return line.Length > 60 ? line.Substring(0, 60) + "…" : line;
        }

        private bool SameObject(string first, string second) =>
            !string.IsNullOrEmpty(first) && (first == second ||
                second != null && sceneBindings.TryGetValue(first, out var a) &&
                sceneBindings.TryGetValue(second, out var b) && a == b);

        private bool AcceptsAction(string key, GameObject prefab, string guid, FlowAction action, bool isTarget)
        {
            if (prefab != null) return isTarget ? action.AcceptsTarget(prefab) : action.AcceptsSubject(prefab);
            return key == NodeParameters.SignalObject || (sceneBindings.TryGetValue(key, out var entry) &&
                entry.Actions.TryGetValue(guid, out var roles) && (isTarget ? roles.target : roles.subject));
        }

        private static bool IsPickable(GameObject target) => target != null &&
            target.TryGetComponent<Item>(out var item) && LabelSystem.HasLabel(item, ItemLabel.Pickable);

        private bool AcceptsPickup(string key, GameObject prefab) => prefab != null ? IsPickable(prefab) :
            key == NodeParameters.SignalObject || (sceneBindings.TryGetValue(key, out var binding) && binding.Pickable);

        private static bool HasPickupBefore(FlowDocument document, StudioNode consumer) =>
            document.nodes.Any(node => node.type == "wait_signal" && node.parameters.signal == FlowSignals.ItemPickedUp &&
                document.CanReferenceOutput(node.id, consumer.id));

        private IEnumerable<StudioNode> PreviousSpawnNodes(StudioNode consumer) =>
            VisibleDocument.nodes.Where(node => node.SpawnsObject && VisibleDocument.CanReferenceOutput(node.id, consumer.id));

        private void GeneratedTargetField(VisualElement fields, StudioNode consumer)
        {
            var p = consumer.parameters;
            string NameOf(StudioNode node) => AssetLabel(VisibleDocument.PrefabGuid(node)) ?? node.Name;
            IEnumerable<PickerOption> Options() => PreviousSpawnNodes(consumer).Select(node =>
                new PickerOption(NodeParameters.SpawnOutput(node.id), NameOf(node), "已生成物件", NodeReferenceDetail(node)));

            string selected = null;
            if (!string.IsNullOrEmpty(p.target))
            {
                var source = PreviousSpawnNodes(consumer).FirstOrDefault(node => NodeParameters.SpawnOutput(node.id) == p.target);
                selected = source == null ? "当前目标不可用" : NameOf(source);
                if (source != null && !string.IsNullOrWhiteSpace(source.remarks))
                    selected += " · " + NodeReferenceDetail(source);
            }
            PickField(fields, "靠近目标", selected, "选择生成的物件", Options, value => p.target = value,
                "暂无可选物件。请先在此节点之前连接「生成物件」。");
        }

        private IEnumerable<(string key, string name, string group, string detail, GameObject prefab)> ReferenceOptions(StudioNode consumer)
        {
            foreach (var entry in sceneBindings.Values.Distinct())
                yield return (entry.Key, entry.Name, entry.Scene, "", null);
            foreach (var node in PreviousSpawnNodes(consumer))
            {
                string key = NodeParameters.SpawnOutput(node.id);
                yield return (key, node.Name, "已生成物件", NodeReferenceDetail(node), PrefabForReference(VisibleDocument, key));
            }
        }

        private void ObjectField(VisualElement fields, string label, StudioNode node, string value, Action<string> assign,
            Func<string, GameObject, bool> accepts = null, string other = null, string placeholder = "选择物件",
            string empty = "暂无符合条件的物件。")
        {
            bool Accept(string key, GameObject prefab) => !SameObject(key, other) && (accepts?.Invoke(key, prefab) ?? true);
            IEnumerable<PickerOption> Options()
            {
                foreach (var (key, name, group, detail, prefab) in ReferenceOptions(node))
                    if (Accept(key, prefab)) yield return new PickerOption(key, name, group, detail);
                if (Accept(NodeParameters.SignalObject, null) && HasPickupBefore(VisibleDocument, node))
                    yield return new PickerOption(NodeParameters.SignalObject, "玩家拾取的物件", "玩家操作");
            }
            string selected = !string.IsNullOrEmpty(value) ? ReferenceLabel(value) : null;
            if (sceneBindings.TryGetValue(value ?? "", out var binding)) selected = binding.Scene + " › " + binding.Name;
            PickField(fields, label, selected, placeholder, Options, assign, empty);
        }

        private IEnumerable<PickerOption> Locations(StudioNode consumer, string other, bool sceneOnly)
        {
            foreach (var (key, name, group, detail, prefab) in ReferenceOptions(consumer))
            {
                if (SameObject(key, other) || sceneOnly && NodeParameters.IsSpawnOutput(key)) continue;
                if (!sceneBindings.ContainsKey(key) && prefab == null) continue;
                yield return new PickerOption(key, name, group, detail);
            }
        }

        private (Place point, string group, string owner) SelectedPlace(FlowDocument document, string reference, string pointId)
        {
            if (!string.IsNullOrEmpty(reference) && sceneBindings.TryGetValue(reference, out var item))
                return (string.IsNullOrEmpty(pointId) ? item.Origin : item.Points.FirstOrDefault(point => point.Id == pointId),
                    item.Scene, item.Name);
            var prefab = PrefabForReference(document, reference);
            return (string.IsNullOrEmpty(pointId) && prefab != null ? PositionOf(prefab.transform, "") :
                Points(prefab).FirstOrDefault(point => point.Id == pointId), "已生成物件",
                NodeParameters.IsSpawnOutput(reference) ? document.Find(NodeParameters.SpawnNodeId(reference))?.Name : null);
        }

        private void LocationField(VisualElement fields, string label, StudioNode node, string reference,
            Action<string> assign, bool sceneOnly = false, string other = null)
        {
            var (point, group, owner) = SelectedPlace(VisibleDocument, reference, "");
            string text = null;
            if (!string.IsNullOrEmpty(reference))
                text = point == null ? "位置不可用" : group + " › " + owner;
            if (point != null && node.type != "wait_region" && !string.IsNullOrWhiteSpace(node.parameters.placeLabel))
                text = node.parameters.placeLabel + " · " + text;
            PickField(fields, label, text, "选择位置",
                () => Locations(node, other, sceneOnly), assign, sceneOnly ? "暂无可选的场景位置。" : "暂无可选位置。");
            if (point != null && node.type != "wait_region" && (node.type != "spawn" || node.parameters.placement != "projected"))
                AddPlacementPreview(fields, node);
        }

        private void MigrateLegacyPlacement(FlowDocument graph)
        {
            foreach (var node in graph.nodes.Where(node => node?.parameters != null))
            {
                var p = node.parameters;
                if (node.type == "wait_region")
                {
                    if (!p.placeRadius)
                    {
                        var center = SelectedPlace(graph, p.target, p.destinationPoint).point;
                        if (center?.Region != true) continue;
                        p.radius = center.RegionRadius;
                        p.planarDistance = center.RegionHorizontalOnly;
                        p.placementOffset = Vector3.zero;
                        p.customLocation = false;
                        p.placeRadius = true;
                    }
                    ConvertLegacyDestination(graph, node, p.target);
                }
                else if (node.SpawnsObject || node.type == "transfer")
                {
                    ConvertLegacyDestination(graph, node, p.destination);
                    if (!node.SpawnsObject || string.IsNullOrEmpty(p.sourcePoint)) continue;
                    var prefab = Files.ResolveAsset(graph.PrefabGuid(node)) as GameObject;
                    if (prefab == null) continue;
                    var point = FlowAnchor.Find(prefab.transform, p.sourcePoint);
                    if (point == null) continue;
                    p.spawnOriginOffset = Quaternion.Inverse(prefab.transform.rotation) *
                        (point.position - prefab.transform.position);
                    p.sourcePoint = "";
                }
            }
        }

        private void ConvertLegacyDestination(FlowDocument graph, StudioNode node, string reference)
        {
            var p = node.parameters;
            if (string.IsNullOrEmpty(p.destinationPoint)) return;
            var origin = SelectedPlace(graph, reference, "").point;
            var point = SelectedPlace(graph, reference, p.destinationPoint).point;
            if (origin == null || point == null) return;

            float scale = NodeParameters.IsSpawnOutput(reference) ?
                graph.Find(NodeParameters.SpawnNodeId(reference))?.parameters?.spawnScale ?? 1f : 1f;
            Quaternion intoOrigin = Quaternion.Inverse(origin.Rotation);
            Vector3 offset = intoOrigin * (point.Position - origin.Position) * scale;
            if (node.SpawnsObject && p.placement == "projected")
                p.projectedAnchorOffset = offset;
            else
            {
                Quaternion relativeRotation = intoOrigin * point.Rotation;
                bool usesOffset = node.SpawnsObject ? p.placement == "relative" : p.customLocation;
                p.placementOffset = offset + (usesOffset ? relativeRotation * p.placementOffset : Vector3.zero);
                if (node.SpawnsObject) p.placement = p.placementOffset == Vector3.zero ? "anchor" : "relative";
                else p.customLocation = p.placementOffset != Vector3.zero;
                if (node.type != "wait_region") p.destinationAngles = relativeRotation.eulerAngles;
            }
            p.destinationPoint = "";
        }

        private void AddPlacementPreview(VisualElement fields, StudioNode node)
        {
            var details = new Foldout { name = "position-preview", text = "位置预览", value = false };
            fields.Add(details);
            var summary = Text("", "muted wrap");
            details.Add(summary);
            var diagram = Box("placement-preview");
            details.Add(diagram);
            bool CanAdjust() => node.type == "spawn" ? node.parameters.placement == "relative" : node.parameters.customLocation;
            Vector3 Offset() => CanAdjust() ? node.parameters.placementOffset : Vector3.zero;
            float Scale(Vector3 offset)
            {
                float span = Mathf.Max(2f, Mathf.Abs(offset.x), Mathf.Abs(offset.z));
                return Mathf.Max(.0001f, (Mathf.Min(diagram.contentRect.width, diagram.contentRect.height) / 2 - 12) / span);
            }
            void UpdateSummary()
            {
                var offset = Offset();
                bool relative = NodeParameters.IsSpawnOutput(node.parameters.destination);
                summary.text = relative ? "位置随生成物件移动。" : "十字为基准位置，绿色标记为当前落点。";
                if (CanAdjust()) summary.text += "\n点击示意图可调整水平偏移。";
                if (offset.y != 0) summary.text += "\n高度偏移 " + offset.y.ToString("0.##") + " 米";
            }
            diagram.generateVisualContent += context =>
            {
                var painter = context.painter2D;
                Vector2 center = diagram.contentRect.center;
                Vector3 offset = Offset();
                float scale = Scale(offset);
                Vector2 placed = center + new Vector2(offset.x, -offset.z) * scale;
                painter.strokeColor = new Color(.34f, .46f, .55f);
                painter.lineWidth = 1;
                painter.BeginPath();
                painter.MoveTo(new Vector2(8, center.y));
                painter.LineTo(new Vector2(diagram.contentRect.width - 8, center.y));
                painter.MoveTo(new Vector2(center.x, 8));
                painter.LineTo(new Vector2(center.x, diagram.contentRect.height - 8));
                painter.Stroke();
                painter.strokeColor = new Color(.44f, .84f, .73f);
                painter.lineWidth = 2;
                painter.BeginPath();
                painter.Arc(placed, 5, 0, 360);
                painter.Stroke();
            };
            refreshPlacementPreview = () => { UpdateSummary(); diagram.MarkDirtyRepaint(); };
            RefreshPlacementPreview();
            details.RegisterValueChangedCallback(_ => RefreshPlacementPreview());
            diagram.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || !CanAdjust() || Simulation != null) return;
                Vector2 direction = (Vector2)evt.localPosition - diagram.contentRect.center;
                float scale = Scale(Offset());
                placementPicked?.Invoke(new Vector2(Mathf.Round(direction.x / scale * 100) / 100,
                    Mathf.Round(-direction.y / scale * 100) / 100));
                evt.StopPropagation();
            });
        }

        private void RefreshPlacementPreview() => refreshPlacementPreview?.Invoke();

        private void ResourceField(VisualElement fields, string label, string guid, bool action, Action<string> assign)
        {
            string asset = AssetLabel(guid);
            PickField(fields, label, asset ?? (string.IsNullOrEmpty(guid) ? null : "所选内容不可用"),
                action ? "选择玩法" : "选择物件", () => action ? actionLibrary.Values : AssetOptions(false), assign,
                action ? "暂无可用玩法。" : "暂无可生成的物件。");
#if UNITY_EDITOR
            if (!action && Files.ResolveAsset(guid) is GameObject prefab)
            {
                var preview = new Image { scaleMode = ScaleMode.ScaleToFit, tooltip = asset };
                preview.AddToClassList("asset-preview");
                preview.style.display = DisplayStyle.None;
                fields.Add(preview);
                int attempts = 0;
                IVisualElementScheduledItem update = null;
                update = preview.schedule.Execute(() =>
                {
                    var thumbnail = UnityEditor.AssetPreview.GetAssetPreview(prefab);
                    if (thumbnail != null)
                    {
                        preview.image = thumbnail;
                        preview.style.display = DisplayStyle.Flex;
                    }
                    if (thumbnail != null || ++attempts == 20) update.Pause();
                }).Every(125);
            }
#endif
        }

        private void PickField(VisualElement fields, string label, string selected, string placeholder,
            Func<IEnumerable<PickerOption>> options, Action<string> assign, string empty)
        {
            var field = Box("picker-field");
            field.Add(Text(label, "picker-label"));
            string title = placeholder.StartsWith("选择", StringComparison.Ordinal) ? placeholder : "选择物件";
            var choose = Button(selected ?? placeholder, () => OpenPicker(title, options(), empty, key =>
            {
                Edit(() => assign(key));
                BuildInspector();
            }), "picker-button");
            choose.tooltip = selected;
            field.Add(choose);
            fields.Add(field);
        }

        private void OpenPicker(string title, IEnumerable<PickerOption> options, string empty, Action<string> select)
        {
            var items = options.OrderBy(option => option.Group).ThenBy(option => option.Label).ToArray();
            var modal = Modal(title, "");
            var search = new TextField();
            search.textEdition.placeholder = "搜索名称或位置";
            if (items.Length != 0) modal.Add(search);
            var list = new ScrollView();
            list.style.maxHeight = 360;
            modal.Add(list);
            void Refresh(string query)
            {
                list.Clear();
                string group = null;
                int count = 0;
                foreach (var option in items.Where(option => option.Matches(query)))
                {
                    if (count == 80) { list.Add(Text("结果过多，请缩小搜索范围。", "muted wrap")); break; }
                    count++;
                    if (group != option.Group)
                    {
                        group = option.Group;
                        if (!string.IsNullOrEmpty(group)) list.Add(Text(group, "group-label"));
                    }
                    var row = Button(option.Label, () => { CloseModal(); select(option.Key); }, "file-button picker-result");
                    list.Add(row);
                    if (!string.IsNullOrWhiteSpace(option.Detail))
                        list.Add(Text(option.Detail, "picker-description muted wrap"));
                }
                if (count == 0) list.Add(Text(items.Length == 0 ? empty : "未找到匹配结果。", "muted wrap"));
            }
            search.RegisterValueChangedCallback(evt => Refresh(evt.newValue));
            modal.Add(Button("取消", CloseModal));
            Refresh("");
            if (items.Length != 0) search.Focus();
        }

        private IEnumerable<string> SelectionIssues(FlowDocument document, StudioNode node)
        {
            var p = node.parameters;
            if (node.type == "wait_signal" && !string.IsNullOrEmpty(p.signal) && FlowSignals.Label(p.signal) == null)
                yield return "所选玩家操作不可用，请重新选择。";
            if (node.type == "wait_signal" && !string.IsNullOrEmpty(p.item) &&
                (sceneBindings.ContainsKey(p.item) || PrefabForReference(document, p.item) != null) &&
                !AcceptsPickup(p.item, PrefabForReference(document, p.item)))
                yield return "所选物件不可拾取。请选择其他物件，或设为不限物件。";
            foreach (string key in p.ObjectReferences(node.type).Where(key => !string.IsNullOrEmpty(key)).Distinct())
            {
                if (key == NodeParameters.SignalObject)
                {
                    if (!HasPickupBefore(document, node))
                        yield return "当前分支没有可引用的拾取物件。请添加拾取条件或选择其他物件。";
                }
                else if (NodeParameters.IsSpawnOutput(key))
                {
                    if (document.Find(NodeParameters.SpawnNodeId(key))?.SpawnsObject != true ||
                        !document.CanReferenceOutput(NodeParameters.SpawnNodeId(key), node.id))
                        yield return "所选物件在此节点执行前尚未生成。请调整连线或重新选择。";
                }
                else if (sceneBindings.Count != 0 && !sceneBindings.ContainsKey(key))
                    yield return key == NodeParameters.PlayerShip ? "当前场景未配置玩家船。" : "所选场景物件不可用，请重新选择。";
            }
            if ((node.type == "branch" && p.checkDistance || node.type == "wait_region") &&
                p.target != NodeParameters.PlayerShip && SameObject(NodeParameters.PlayerShip, p.target))
                yield return "请选择玩家船以外的目标。";
            if (node.type == "transfer" && p.item != p.destination && SameObject(p.item, p.destination))
                yield return "目标位置与物件自身重合，请重新选择。";

            string destination = node.type == "wait_region" ? p.target : p.destination;
            if ((node.type == "spawn" || node.type == "transfer" || node.type == "wait_region") &&
                !string.IsNullOrEmpty(destination) && sceneBindings.ContainsKey(destination))
            {
                var place = SelectedPlace(document, destination, p.destinationPoint).point;
                if (place == null || node.type == "wait_region" && !p.placeRadius && !place.Region)
                    yield return "所选位置或区域已不可用，请重新选择。";
            }

            if (node.type != "run_action" || Files.ResolveAsset(p.actionGuid) is not FlowAction action) yield break;
            if (SameObject(p.item, p.destination)) yield return "物件与目标位置重复，请重新选择。";
            if (!string.IsNullOrEmpty(action.SubjectLabel) && !string.IsNullOrEmpty(p.item) &&
                sceneBindings.ContainsKey(p.item) && !AcceptsAction(p.item, null, p.actionGuid, action, false))
                yield return "「" + action.SubjectLabel + "」所选物件不适用，请重新选择。";
            if (!string.IsNullOrEmpty(action.TargetLabel) && !string.IsNullOrEmpty(p.destination) &&
                sceneBindings.ContainsKey(p.destination) && !AcceptsAction(p.destination, null, p.actionGuid, action, true))
                yield return "「" + action.TargetLabel + "」所选位置不适用，请重新选择。";
        }
    }
}
