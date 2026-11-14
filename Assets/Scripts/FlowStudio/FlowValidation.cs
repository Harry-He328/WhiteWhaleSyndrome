using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WhiteWhale.Flow;

namespace WhiteWhale.FlowStudio
{
    public static class FlowValidation
    {
        public static List<string> Check(FlowDocument document, Func<string, FlowDocument> load = null,
            Func<string, UnityEngine.Object> resolveAsset = null)
        {
            var errors = new List<string>();
            CheckDocument(document, load, resolveAsset, new HashSet<string>(), errors);
            return errors;
        }

        private static void CheckDocument(FlowDocument doc, Func<string, FlowDocument> load, Func<string, UnityEngine.Object> resolve,
            HashSet<string> active, List<string> errors)
        {
            if (doc?.nodes == null || doc.edges == null || doc.version != 1 || (doc.kind != "event" && doc.kind != "main"))
            { errors.Add("流程格式无效。"); return; }
            if (active.Count >= 16 || !active.Add(doc.id)) { errors.Add("事件引用存在循环或超过层数限制。"); return; }
            try
            {
                if (doc.nodes.Count > 1000) { errors.Add("单个流程最多支持 1000 个节点。"); return; }
                var ids = new HashSet<string>();
                foreach (var input in doc.prefabInputs ?? Enumerable.Empty<FlowPrefabInput>())
                    if (input == null || string.IsNullOrWhiteSpace(input.id) || !ids.Add(input.id))
                        errors.Add(doc.title + "：预制体参数的编号为空或重复。");
                ids.Clear();
                foreach (var node in doc.nodes)
                {
                    if (node?.parameters == null || string.IsNullOrWhiteSpace(node.id) || !ids.Add(node.id))
                    { errors.Add(doc.title + "：节点编号、参数缺失或编号重复。"); continue; }
                    var spec = NodeCatalog.Get(node.type);
                    string prefix = doc.title + " / " + node.title + "：";
                    if (spec == null || !spec.Allowed(doc.kind)) { errors.Add(prefix + "节点类型不可用。"); continue; }
                    ValidateParameters(doc, node, prefix, resolve, errors);
                    if (node.type == "call_event" || node.type == "on_signal")
                    {
                        try
                        {
                            string file = FlowFileStore.FileName(node.parameters.eventFile);
                            if (load == null) continue;
                            var child = load(file);
                            if (child?.kind != "event") errors.Add(prefix + "请选择事件流程。");
                            else CheckDocument(child, load, resolve, active, errors);
                        }
                        catch (Exception exception) { errors.Add(prefix + exception.Message); }
                    }
                }
                if (doc.Find(doc.entry)?.type != "start" || doc.nodes.Count(node => node?.type == "start") != 1)
                    errors.Add(doc.title + "：需要一个开始节点。");
                var ports = new HashSet<(string, string)>();
                foreach (var edge in doc.edges)
                {
                    var source = edge == null ? null : doc.Find(edge.from);
                    var target = edge == null ? null : doc.Find(edge.to);
                    if (source == null || target == null || target.type == "start" || target.type == "on_signal" ||
                        NodeCatalog.Get(source.type)?.ports.Contains(edge.port) != true || !ports.Add((edge.from, edge.port)))
                        errors.Add(doc.title + "：存在无效或重复的连线。");
                }
                var reached = new HashSet<string>();
                var visiting = new HashSet<string>();
                bool cycle = false;
                void Visit(string id)
                {
                    if (visiting.Contains(id)) { cycle = true; return; }
                    if (doc.Find(id) == null || !reached.Add(id)) return;
                    visiting.Add(id);
                    foreach (var edge in doc.edges.Where(edge => edge?.from == id)) Visit(edge.to);
                    visiting.Remove(id);
                }
                Visit(doc.entry);
                foreach (var node in doc.nodes.Where(node => node?.parameters != null))
                {
                    string prefix = doc.title + " / " + node.title + "：";
                    if (node.type != "on_signal" && !reached.Contains(node.id)) errors.Add(prefix + "无法从入口到达。");
                    foreach (string port in NodeCatalog.Get(node.type)?.ports ?? Array.Empty<string>())
                        if (!ports.Contains((node.id, port))) errors.Add(prefix + "存在未连接的出口。");
                    foreach (string reference in node.parameters.ObjectReferences(node.type).Where(NodeParameters.IsSpawnOutput))
                    {
                        string producer = NodeParameters.SpawnNodeId(reference);
                        if (doc.Find(producer)?.SpawnsObject != true || !doc.CanReferenceOutput(producer, node.id))
                            errors.Add(prefix + "存在尚未生成所引用对象的执行路径。");
                    }
                    if (node.type == "branch" && node.parameters.flag?.StartsWith(NodeParameters.ConditionPrefix, StringComparison.Ordinal) == true)
                    {
                        string producer = node.parameters.flag.Substring(NodeParameters.ConditionPrefix.Length);
                        if (doc.Find(producer)?.type != "set_flag" || !doc.CanReferenceOutput(producer, node.id))
                            errors.Add(prefix + "存在尚未设置所引用条件的执行路径。");
                    }
                }
                if (cycle) errors.Add(doc.title + "：当前流程不支持循环。");
            }
            finally { active.Remove(doc.id); }
        }

        private static void ValidateParameters(FlowDocument doc, StudioNode node, string prefix,
            Func<string, UnityEngine.Object> resolve, List<string> errors)
        {
            var p = node.parameters;
            bool Missing(string value) => string.IsNullOrWhiteSpace(value);
            bool Positive(float value) => float.IsFinite(value) && value > 0;
            void Require(bool condition, string message) { if (!condition) errors.Add(prefix + message); }
            GameObject Prefab(string reference) => resolve != null && NodeParameters.IsSpawnOutput(reference) &&
                doc.Find(NodeParameters.SpawnNodeId(reference)) is StudioNode source && source.SpawnsObject
                ? resolve(doc.PrefabGuid(source)) as GameObject : null;

            void CheckPoint(GameObject owner, string point, string field)
            {
                if (owner != null && FlowAnchor.Find(owner.transform, point) == null)
                    errors.Add(prefix + field + "在所选资源上不存在或编号重复。");
            }

            switch (node.type)
            {
                case "wait_near":
                    Require(!Missing(p.actor) && !Missing(p.target) && Positive(p.radius), "请选择两个对象和有效的距离。");
                    break;
                case "wait_travel":
                    Require(!Missing(p.actor) && Positive(p.travelDistance), "请选择对象和有效的累计位移。");
                    break;
                case "wait_region":
                    Require(!Missing(p.actor) && !Missing(p.target), "请选择对象和区域。");
                    var regionOwner = Prefab(p.target);
                    if (regionOwner != null)
                        Require(FlowAnchor.Find(regionOwner.transform, p.destinationPoint)?.GetComponent<FlowRegion>() != null,
                            "所选区域在资源上不可用。");
                    break;
                case "delay":
                    Require(float.IsFinite(p.seconds) && p.seconds >= 0, "等待秒数无效。");
                    break;
                case "wait_signal": case "on_signal":
                    Require(!Missing(p.signal), "请填写信号名称。");
                    break;
                case "branch":
                    Require(!Missing(p.flag), "请选择条件。");
                    break;
                case "spawn":
                    string guid = doc.PrefabGuid(node);
                    Require(!Missing(guid) && !Missing(p.destination), "请选择预制体和目标位置。");
                    Require(p.placement == "anchor" || p.placement == "relative" || p.placement == "projected", "摆放方式无效。");
                    Require(Positive(p.spawnScale) && float.IsFinite(p.placementOffset.x) &&
                        float.IsFinite(p.placementOffset.y) && float.IsFinite(p.placementOffset.z), "位置偏移或缩放无效。");
                    if (p.placement == "projected") Require(!Missing(p.orientation), "请选择朝向参考对象。");
                    if (resolve != null && !Missing(guid))
                    {
                        var prefab = resolve(guid) as GameObject;
                        Require(prefab != null, "预制体不存在。");
                        CheckPoint(prefab, p.sourcePoint, "预制体对齐点");
                    }
                    CheckPoint(Prefab(p.destination), p.destinationPoint, "目标位置");
                    break;
                case "transfer":
                    Require(!Missing(p.item) && !Missing(p.destination), "请选择要移动的对象和目标位置。");
                    CheckPoint(Prefab(p.destination), p.destinationPoint, "目标位置");
                    break;
                case "run_action":
                    Require(!Missing(p.actionGuid), "请选择动作资源。");
                    if (resolve != null && !Missing(p.actionGuid))
                    {
                        var action = resolve(p.actionGuid) as FlowAction;
                        Require(action != null, "动作资源不存在。");
                        if (action == null) break;
                        if (!string.IsNullOrEmpty(action.SubjectLabel))
                        {
                            Require(!Missing(p.item), "请选择" + action.SubjectLabel + "。");
                            var prefab = Prefab(p.item);
                            if (prefab != null) Require(action.AcceptsSubject(prefab), "所选对象不符合动作要求。");
                        }
                        if (!string.IsNullOrEmpty(action.TargetLabel))
                        {
                            Require(!Missing(p.destination), "请选择" + action.TargetLabel + "。");
                            var prefab = Prefab(p.destination);
                            if (prefab != null) Require(action.AcceptsTarget(prefab), "所选目标不符合动作要求。");
                        }
                    }
                    break;
            }
        }
    }
}
