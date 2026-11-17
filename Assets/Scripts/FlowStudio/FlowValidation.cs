using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WhiteWhale.Flow;

namespace WhiteWhale.FlowStudio
{
    public sealed class FlowIssue
    {
        public FlowDocument Document { get; }
        public StudioNode Node { get; }
        public string File { get; }
        public string Message { get; }

        public FlowIssue(FlowDocument document, StudioNode node, string file, string message)
        {
            Document = document;
            Node = node;
            File = file;
            Message = message;
        }

        public override string ToString() => (Document?.title ?? "流程") +
            (Node == null ? "" : " / " + Node.Name) + "：" + Message;
    }

    public static class FlowValidation
    {
        public static List<string> Check(FlowDocument document, Func<string, FlowDocument> load = null,
            Func<string, UnityEngine.Object> resolveAsset = null) =>
            CheckDetails(document, load, resolveAsset).Select(issue => issue.ToString()).ToList();

        public static List<FlowIssue> CheckDetails(FlowDocument document, Func<string, FlowDocument> load,
            Func<string, UnityEngine.Object> resolveAsset, Func<FlowDocument, StudioNode, IEnumerable<string>> extra = null)
        {
            var issues = new List<FlowIssue>();
            CheckDocument(document, null, load, resolveAsset, extra, new HashSet<string>(), issues);
            return issues;
        }

        private static void CheckDocument(FlowDocument doc, string file, Func<string, FlowDocument> load,
            Func<string, UnityEngine.Object> resolve, Func<FlowDocument, StudioNode, IEnumerable<string>> extra,
            HashSet<string> active, List<FlowIssue> issues)
        {
            void Add(StudioNode node, string message) => issues.Add(new FlowIssue(doc, node, file, message));
            if (doc?.nodes == null || doc.edges == null || doc.version != 1 || (doc.kind != "event" && doc.kind != "main"))
            { Add(null, "流程格式无效。"); return; }
            if (active.Count >= 16 || !active.Add(doc.id)) { Add(null, "事件引用存在循环或超过层数限制。"); return; }
            try
            {
                if (doc.nodes.Count > 1000) { Add(null, "单个流程最多支持 1000 个节点。"); return; }
                var ids = new HashSet<string>();
                foreach (var input in doc.prefabInputs ?? Enumerable.Empty<FlowPrefabInput>())
                    if (input == null || string.IsNullOrWhiteSpace(input.id) || !ids.Add(input.id))
                        Add(null, "共享物件的编号为空或重复。");
                ids.Clear();
                foreach (var node in doc.nodes)
                {
                    if (node?.parameters == null || string.IsNullOrWhiteSpace(node.id) || !ids.Add(node.id))
                    { Add(node, "节点数据缺失或重复。"); continue; }
                    var spec = NodeCatalog.Get(node.type);
                    if (spec == null || !spec.Allowed(doc.kind)) { Add(node, "当前流程不支持此节点类型。"); continue; }
                    foreach (string message in ParameterIssues(doc, node, resolve)) Add(node, message);
                    if (extra != null)
                        foreach (string message in extra(doc, node)) Add(node, message);
                    if (node.type != "call_event") continue;
                    try
                    {
                        if (string.IsNullOrWhiteSpace(node.parameters.eventFile))
                        { Add(node, "请选择事件。"); continue; }
                        string childFile = FlowFileStore.FileName(node.parameters.eventFile);
                        if (load == null) continue;
                        var child = load(childFile);
                        if (child?.kind != "event") Add(node, "请选择事件流程。");
                        else CheckDocument(child, childFile, load, resolve, extra, active, issues);
                    }
                    catch (Exception exception) { Add(node, exception.Message); }
                }

                if (doc.Find(doc.entry)?.type != "start" || doc.nodes.Count(node => node?.type == "start") != 1)
                    Add(null, "请保留一个「开始」节点。");
                var ports = new HashSet<(string, string)>();
                foreach (var edge in doc.edges)
                {
                    var source = edge == null ? null : doc.Find(edge.from);
                    var target = edge == null ? null : doc.Find(edge.to);
                    if (source == null || target == null || target.type == "start" || target.type == "on_signal" ||
                        NodeCatalog.Get(source.type)?.ports.Contains(edge.port) != true || !ports.Add((edge.from, edge.port)))
                        Add(source, "请重新连接无效或重复的路线。");
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
                    if (node.type != "on_signal" && !reached.Contains(node.id)) Add(node, "此节点未接入流程，请连接前一个节点。");
                    var spec = NodeCatalog.Get(node.type);
                    if (spec != null)
                        for (int index = 0; index < spec.ports.Length; index++)
                            if (!ports.Contains((node.id, spec.ports[index])))
                                Add(node, "请为「" + spec.portLabels[index] + "」选择下一步。");
                    foreach (string reference in node.parameters.ObjectReferences(node.type).Where(NodeParameters.IsSpawnOutput))
                    {
                        string producer = NodeParameters.SpawnNodeId(reference);
                        if (doc.Find(producer)?.SpawnsObject != true || !doc.CanReferenceOutput(producer, node.id))
                            Add(node, "部分分支在此节点执行前尚未生成所选物件。请调整连线或重新选择。");
                    }
                    if (node.type == "branch" && !node.parameters.checkDistance &&
                        node.parameters.flag?.StartsWith(NodeParameters.ConditionPrefix, StringComparison.Ordinal) == true)
                    {
                        string producer = node.parameters.flag.Substring(NodeParameters.ConditionPrefix.Length);
                        if (doc.Find(producer)?.type != "set_flag" || !doc.CanReferenceOutput(producer, node.id))
                            Add(node, "请在到达此节点的每条分支上先记录结果。");
                    }
                }
                if (cycle) Add(null, "流程存在循环连线，请调整连接顺序。");
            }
            finally { active.Remove(doc.id); }
        }

        public static List<string> ParameterIssues(FlowDocument doc, StudioNode node, Func<string, UnityEngine.Object> resolve)
        {
            var errors = new List<string>();
            var p = node.parameters;
            bool Missing(string value) => string.IsNullOrWhiteSpace(value);
            bool Positive(float value) => float.IsFinite(value) && value > 0;
            bool ValidOffset() => float.IsFinite(p.placementOffset.x) && float.IsFinite(p.placementOffset.y) &&
                float.IsFinite(p.placementOffset.z);
            void Require(bool condition, string message) { if (!condition) errors.Add(message); }
            GameObject Prefab(string reference) => resolve != null && NodeParameters.IsSpawnOutput(reference) &&
                doc.Find(NodeParameters.SpawnNodeId(reference)) is StudioNode source && source.SpawnsObject
                ? resolve(doc.PrefabGuid(source)) as GameObject : null;

            void CheckPoint(GameObject owner, string point, string field)
            {
                if (owner != null && FlowAnchor.Find(owner.transform, point) == null)
                    errors.Add(field + "已不可用，请重新选择。");
            }

            switch (node.type)
            {
                case "wait_near":
                    Require(NodeParameters.IsSpawnOutput(p.target), "请选择前面节点生成的物件作为靠近目标。");
                    Require(Positive(p.radius), "距离上限须大于 0 米。");
                    break;
                case "branch" when p.checkDistance:
                    Require(!Missing(p.target), "请选择参照对象。");
                    Require(p.target != NodeParameters.PlayerShip, "请选择玩家船以外的参照对象。");
                    Require(Positive(p.radius), "距离上限须大于 0 米。");
                    break;
                case "wait_travel":
                    Require(Positive(p.travelDistance), "累计移动距离须大于 0 米。");
                    break;
                case "wait_region":
                    Require(!Missing(p.target), "请连接范围中心或选择场景位置。");
                    Require(p.target != NodeParameters.PlayerShip, "请选择玩家船以外的位置或区域。");
                    if (p.placeRadius)
                    {
                        Require(Positive(p.radius), "半径须大于 0 米。");
                        Require(!p.customLocation || ValidOffset(), "中心位置的偏移无效。");
                        CheckPoint(Prefab(p.target), p.destinationPoint, "中心位置");
                        break;
                    }
                    var regionOwner = Prefab(p.target);
                    if (regionOwner != null)
                        Require(FlowAnchor.Find(regionOwner.transform, p.destinationPoint)?.GetComponent<FlowRegion>() != null,
                            "原范围不可用，请重新设置中心位置和半径。");
                    break;
                case "delay":
                    Require(float.IsFinite(p.seconds) && p.seconds >= 0, "时长须大于或等于 0 秒。");
                    break;
                case "wait_signal":
                    Require(!Missing(p.signal), "请选择玩家操作。");
                    break;
                case "choice":
                    Require(!Missing(p.note), "请填写提示内容。");
                    Require(!Missing(p.firstChoice), "请填写选项一。");
                    Require(!Missing(p.secondChoice), "请填写选项二。");
                    Require(Missing(p.firstChoice) || Missing(p.secondChoice) || p.firstChoice.Trim() != p.secondChoice.Trim(),
                        "选项一和选项二不能相同。");
                    break;
                case "branch":
                    Require(!Missing(p.flag), "请选择结果来源。");
                    break;
                case "spawn":
                    string guid = doc.PrefabGuid(node);
                    Require(!Missing(guid), "请选择物件。");
                    Require(!Missing(p.destination), "请选择放置位置。");
                    Require(p.placement == "anchor" || p.placement == "relative" || p.placement == "projected", "请选择有效的摆放方式。");
                    Require(Positive(p.spawnScale) && ValidOffset(), "位置偏移或缩放倍数无效。");
                    if (p.placement == "projected") Require(!Missing(p.orientation), "请选择朝向参照。");
                    if (resolve != null && !Missing(guid))
                    {
                        var prefab = resolve(guid) as GameObject;
                        Require(prefab != null, "所选物件不可用，请重新选择。");
                        CheckPoint(prefab, p.sourcePoint, "原摆放位置");
                    }
                    CheckPoint(Prefab(p.destination), p.destinationPoint, "放置位置");
                    break;
                case "transfer":
                    Require(!Missing(p.item), "请选择物件。");
                    Require(!Missing(p.destination), "请选择目标位置。");
                    Require(Missing(p.item) || p.item != p.destination, "目标位置不能与物件自身重合。");
                    Require(!p.customLocation || ValidOffset(), "目标位置的偏移无效。");
                    CheckPoint(Prefab(p.destination), p.destinationPoint, "目标位置");
                    break;
                case "run_action":
                    Require(!Missing(p.actionGuid), "请选择玩法。");
                    if (resolve != null && !Missing(p.actionGuid))
                    {
                        var action = resolve(p.actionGuid) as FlowAction;
                        Require(action != null, "所选玩法不可用，请重新选择。");
                        if (action == null) break;
                        if (!string.IsNullOrEmpty(action.SubjectLabel))
                        {
                            Require(!Missing(p.item), "请选择「" + action.SubjectLabel + "」。");
                            var prefab = Prefab(p.item);
                            if (prefab != null) Require(action.AcceptsSubject(prefab), "所选物件无法执行此玩法。");
                        }
                        if (!string.IsNullOrEmpty(action.TargetLabel))
                        {
                            Require(!Missing(p.destination), "请选择「" + action.TargetLabel + "」。");
                            var prefab = Prefab(p.destination);
                            if (prefab != null) Require(action.AcceptsTarget(prefab), "所选位置无法执行此玩法。");
                        }
                    }
                    break;
            }
            return errors;
        }
    }
}
