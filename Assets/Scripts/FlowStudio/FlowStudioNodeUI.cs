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
        private sealed class BindingChoice
        {
            public string Label;
            public GameObject EditorObject;
            public bool IsRegion;
            public (string value, string label, bool region)[] Points;
        }

        private readonly Dictionary<string, BindingChoice> sceneBindings = new Dictionary<string, BindingChoice>();

        private void BuildNodeFields(VisualElement fields, StudioNode node)
        {
            var p = node.parameters;
            switch (node.type)
            {
                case "call_event": BuildEventPicker(fields, p); break;
                case "on_signal":
                    Field(fields, "信号名称", p.signal, v => Edit(() => p.signal = v.Trim()));
                    BuildEventPicker(fields, p);
                    break;
                case "wait_near":
                    ObjectReferenceField(fields, "对象", node, p.actor, v => p.actor = v);
                    ObjectReferenceField(fields, "目标", node, p.target, v => p.target = v);
                    Number(fields, "距离（米）", p.radius, v => Edit(() => p.radius = v));
                    Bool(fields, "只计算水平距离", p.planarDistance, v => Edit(() => p.planarDistance = v));
                    break;
                case "wait_travel":
                    ObjectReferenceField(fields, "对象", node, p.actor, v => p.actor = v);
                    Number(fields, "累计位移（米）", p.travelDistance, v => Edit(() => p.travelDistance = v));
                    Bool(fields, "只计算水平位移", p.planarDistance, v => Edit(() => p.planarDistance = v));
                    break;
                case "wait_region":
                    ObjectReferenceField(fields, "对象", node, p.actor, v => p.actor = v);
                    ObjectReferenceField(fields, "区域所属对象", node, p.target, v =>
                    {
                        p.target = v;
                        p.destinationPoint = RegionChoices(v, PrefabForReference(v)).FirstOrDefault().value;
                    }, (reference, prefab) => RegionChoices(reference, prefab).Any());
                    ChoiceField(fields, "区域", p.destinationPoint,
                        RegionChoices(p.target, PrefabForReference(p.target)), v => p.destinationPoint = v);
                    break;
                case "delay": Number(fields, "等待秒数", p.seconds, v => Edit(() => p.seconds = v)); break;
                case "wait_signal": Field(fields, "信号名称", p.signal, v => Edit(() => p.signal = v.Trim())); break;
                case "wait_confirmation": Field(fields, "提示内容", p.note, v => Edit(() => p.note = v), true); break;
                case "stage": Field(fields, "阶段名称", p.stage, v => Edit(() => p.stage = v)); break;
                case "spawn": BuildSpawnFields(fields, node); break;
                case "transfer":
                    ObjectReferenceField(fields, "要移动的对象", node, p.item, v => p.item = v);
                    BuildDestination(fields, node);
                    break;
                case "run_action":
                    AssetField(fields, "动作资源", p.actionGuid, typeof(FlowAction), v =>
                    {
                        p.actionGuid = v;
                        p.item = "";
                        p.destination = "";
                    });
                    if (Files.ResolveAsset(p.actionGuid) is FlowAction action)
                    {
                        if (!string.IsNullOrEmpty(action.SubjectLabel))
                            ObjectReferenceField(fields, action.SubjectLabel, node, p.item, v => p.item = v,
                                (reference, prefab) => prefab == null || action.AcceptsSubject(prefab));
                        if (!string.IsNullOrEmpty(action.TargetLabel))
                            ObjectReferenceField(fields, action.TargetLabel, node, p.destination, v => p.destination = v,
                                (reference, prefab) => prefab == null || action.AcceptsTarget(prefab));
                    }
                    break;
                case "set_flag": Bool(fields, "条件值", p.value, v => Edit(() => p.value = v)); break;
                case "branch":
                    ChoiceField(fields, "条件", p.flag, VisibleDocument.nodes
                        .Where(candidate => candidate.type == "set_flag" && VisibleDocument.CanReferenceOutput(candidate.id, node.id))
                        .Select(candidate => (NodeParameters.ConditionOutput(candidate.id), candidate.title)).Prepend(("", "请选择")), v => p.flag = v);
                    break;
                case "end": Bool(fields, "成功结束", p.success, v => Edit(() => p.success = v)); break;
            }
        }

        private void BuildSpawnFields(VisualElement fields, StudioNode node)
        {
            var p = node.parameters;
            if (VisibleDocument.prefabInputs.Count != 0 || !string.IsNullOrEmpty(p.prefabInput))
                ChoiceField(fields, "资源来源", p.prefabInput, VisibleDocument.prefabInputs
                    .Select(input => (input.id, input.label)).Prepend(("", "直接选择预制体")), value =>
                    {
                        p.prefabInput = value;
                        p.prefabGuid = "";
                        p.sourcePoint = "";
                        if (!string.IsNullOrEmpty(value))
                            node.published?.RemoveAll(setting => setting?.key == nameof(NodeParameters.prefabGuid));
                    });
            if (string.IsNullOrEmpty(p.prefabInput))
                AssetField(fields, "预制体", p.prefabGuid, typeof(GameObject), v => { p.prefabGuid = v; p.sourcePoint = ""; });
            BuildDestination(fields, node);
            ChoiceField(fields, "摆放方式", p.placement, new[] {
                ("anchor", "对齐目标位置"), ("relative", "按目标方向偏移"), ("projected", "沿参考方向投影到水平面")
            }, v => { p.placement = v; if (v != "projected") p.orientation = ""; });
            if (p.placement == "projected")
                ObjectReferenceField(fields, "朝向参考", node, p.orientation, v => p.orientation = v);
            if (p.placement != "anchor")
            {
                var offset = new Vector3Field("右 / 上 / 前偏移（米）") { value = p.placementOffset };
                offset.RegisterValueChangedCallback(evt => Edit(() => p.placementOffset = evt.newValue));
                fields.Add(offset);
            }
            var prefab = Files.ResolveAsset(VisibleDocument.PrefabGuid(node)) as GameObject;
            var points = Points(prefab).Select(point => (point.value, point.label)).Prepend(("", "预制体原点")).ToArray();
            if (points.Length > 1 || !string.IsNullOrEmpty(p.sourcePoint))
                ChoiceField(fields, "预制体对齐点", p.sourcePoint, points, v => p.sourcePoint = v);
            var scale = new Foldout { text = "高级摆放设置", value = false };
            Number(scale, "缩放倍率", p.spawnScale, v => Edit(() => p.spawnScale = v));
            Bool(scale, "归入目标对象的根层级", p.parentToTargetRoot, v => Edit(() => p.parentToTargetRoot = v));
            fields.Add(scale);
        }

        private void BuildDestination(VisualElement fields, StudioNode node)
        {
            var p = node.parameters;
            ObjectReferenceField(fields, "目标对象", node, p.destination, v => { p.destination = v; p.destinationPoint = ""; });
            var points = ReferencePoints(p.destination, PrefabForReference(p.destination))
                .Select(point => (point.value, point.label)).Prepend(("", "对象原点")).ToArray();
            if (points.Length > 1 || !string.IsNullOrEmpty(p.destinationPoint))
                ChoiceField(fields, "定位点", p.destinationPoint, points, v => p.destinationPoint = v);
        }

        private void BuildPrefabInputs()
        {
            var list = new Foldout { text = "共享预制体参数", value = Document.prefabInputs.Count != 0 };
            inspector.Add(list);
            foreach (var input in Document.prefabInputs)
            {
                Field(list, "名称", input.label, v => Edit(() => input.label = v));
                AssetField(list, "预制体", input.guid, typeof(GameObject), v => input.guid = v);
                Bool(list, "在事件资产公开", input.published, v => Edit(() => input.published = v));
                if (!Document.nodes.Any(node => node.parameters.prefabInput == input.id))
                    list.Add(Button("移除「" + input.label + "」", () => { Edit(() => Document.prefabInputs.Remove(input)); BuildInspector(); }));
            }
            list.Add(Button("添加共享预制体", () =>
            {
                Edit(() => Document.prefabInputs.Add(new FlowPrefabInput()));
                BuildInspector();
            }));
        }

        private void AssetField(VisualElement parent, string label, string guid, Type type, Action<string> assign)
        {
#if UNITY_EDITOR
            var field = new UnityEditor.UIElements.ObjectField(label)
            {
                objectType = type, allowSceneObjects = false, value = Files.ResolveAsset(guid)
            };
            field.RegisterValueChangedCallback(evt =>
            {
                string next = "";
                if (evt.newValue != null)
                {
                    string path = UnityEditor.AssetDatabase.GetAssetPath(evt.newValue);
                    if (string.IsNullOrEmpty(path) || UnityEditor.AssetDatabase.LoadMainAssetAtPath(path) != evt.newValue ||
                        (type == typeof(GameObject) && !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
                    {
                        field.SetValueWithoutNotify(Files.ResolveAsset(guid));
                        Notice("请选择 Project 中的对应资源。", true);
                        return;
                    }
                    next = UnityEditor.AssetDatabase.AssetPathToGUID(path);
                }
                Edit(() => assign(next));
                BuildInspector();
            });
            parent.Add(field);
#else
            parent.Add(Text(label + " · " + (AssetLabel(guid) ?? "未选择"), "muted"));
#endif
        }

        private void LoadSceneBindings()
        {
            sceneBindings.Clear();
            var ambiguous = new HashSet<string>();
            void Collect(Scene scene, bool preview)
            {
                foreach (var binding in scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<FlowSceneBindings>(true))
                             .SelectMany(component => component.Objects))
                {
                    if (binding?.target == null || string.IsNullOrEmpty(binding.key) || ambiguous.Contains(binding.key)) continue;
                    if (sceneBindings.ContainsKey(binding.key))
                    {
                        sceneBindings.Remove(binding.key);
                        ambiguous.Add(binding.key);
                        continue;
                    }
                    sceneBindings.Add(binding.key, new BindingChoice
                    {
                        Label = binding.DisplayName,
                        EditorObject = preview ? null : binding.target,
                        IsRegion = binding.target.GetComponent<FlowRegion>() != null,
                        Points = Points(binding.target).ToArray()
                    });
                }
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) Collect(scene, false);
            }
#if UNITY_EDITOR
            foreach (var sceneAsset in (bindingScenes ?? Array.Empty<UnityEngine.Object>()).Distinct())
            {
                string path = UnityEditor.AssetDatabase.GetAssetPath(sceneAsset);
                if (sceneAsset is not UnityEditor.SceneAsset || SceneManager.GetSceneByPath(path).isLoaded) continue;
                var preview = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene(path);
                try { Collect(preview, true); }
                finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview); }
            }
#endif
        }

        private static IEnumerable<(string value, string label, bool region)> Points(GameObject owner) =>
            FlowAnchor.On(owner != null ? owner.transform : null).GroupBy(point => point.Id).Where(group => group.Count() == 1)
                .Select(group => (group.Key, group.First().name, group.First().GetComponent<FlowRegion>() != null));

        private IEnumerable<(string value, string label)> RegionChoices(string reference, GameObject prefab)
        {
            if (prefab?.GetComponent<FlowRegion>() != null ||
                (reference != null && sceneBindings.TryGetValue(reference, out var scene) && scene.IsRegion))
                yield return ("", "所选对象本身");
            foreach (var point in ReferencePoints(reference, prefab).Where(point => point.region))
                yield return (point.value, point.label);
        }

        private IEnumerable<(string value, string label, bool region)> ReferencePoints(string reference, GameObject prefab) =>
            reference != null && sceneBindings.TryGetValue(reference, out var binding) ? binding.Points : Points(prefab);

        private GameObject PrefabForReference(string reference) => NodeParameters.IsSpawnOutput(reference) &&
            VisibleDocument.Find(NodeParameters.SpawnNodeId(reference)) is StudioNode source
                ? Files.ResolveAsset(VisibleDocument.PrefabGuid(source)) as GameObject : null;

        private string AssetLabel(string guid) => Files.ResolveAsset(guid)?.name;

        private string ReferenceLabel(string reference)
        {
            if (reference == NodeParameters.SignalObject) return "信号携带的对象";
            if (NodeParameters.IsSpawnOutput(reference))
                return VisibleDocument.Find(NodeParameters.SpawnNodeId(reference))?.title ?? "生成节点已丢失";
            return reference != null && sceneBindings.TryGetValue(reference, out var binding) ? binding.Label : "场景引用不可用";
        }

        private void ObjectReferenceField(VisualElement fields, string label, StudioNode consumer, string value, Action<string> assign,
            Func<string, GameObject, bool> accepts = null)
        {
            bool Accept(string reference, GameObject target) => accepts == null || accepts(reference, target);
            var choices = new List<(string value, string label)> { ("", "请选择") };
            if (Accept(NodeParameters.SignalObject, null))
                choices.Add((NodeParameters.SignalObject, "信号携带的对象"));
            choices.AddRange(sceneBindings.Where(pair => Accept(pair.Key, pair.Value.EditorObject)).Select(pair => (pair.Key, pair.Value.Label)));
            foreach (var node in VisibleDocument.nodes.Where(node => node.SpawnsObject && VisibleDocument.CanReferenceOutput(node.id, consumer.id)))
            {
                string reference = NodeParameters.SpawnOutput(node.id);
                if (Accept(reference, PrefabForReference(reference))) choices.Add((reference, "生成 · " + node.title));
            }
            ChoiceField(fields, label, value, choices, assign);
        }
    }
}
