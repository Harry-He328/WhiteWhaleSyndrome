using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WhiteWhale.FlowStudio
{
    [Serializable]
    public sealed class FlowDocument
    {
        public int version = 1;
        public string id = Guid.NewGuid().ToString("N");
        public string title = "新流程";
        public string kind = "event";
        public string entry;
        public List<StudioNode> nodes = new List<StudioNode>();
        public List<StudioEdge> edges = new List<StudioEdge>();
        public List<FlowPrefabInput> prefabInputs = new List<FlowPrefabInput>();
        public float panX = 55, panY = 95, zoom = 1;

        public StudioNode Find(string nodeId) => nodes.Find(node => node != null && node.id == nodeId);
        public StudioEdge Output(string nodeId, string port) => edges.Find(edge => edge?.from == nodeId && edge.port == port);
        public string ToJson() => JsonUtility.ToJson(this, true);
        public FlowDocument Clone() => FromJson(ToJson());
        public FlowPrefabInput FindPrefabInput(string inputId) => prefabInputs?.Find(input => input?.id == inputId);

        public string PrefabGuid(StudioNode node) => string.IsNullOrEmpty(node.parameters.prefabInput)
            ? node.parameters.prefabGuid : FindPrefabInput(node.parameters.prefabInput)?.guid;

        public static FlowDocument FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{"))
                throw new FormatException("无法读取流程文件，文件格式有误。");
            var document = JsonUtility.FromJson<FlowDocument>(json);
            if (document == null || document.version != 1) throw new FormatException("不支持此流程文件版本。");
            if (document.kind != "main" && document.kind != "event")
                throw new FormatException("无法识别此流程类型。");
            if (string.IsNullOrWhiteSpace(document.id) || document.nodes == null || document.edges == null)
                throw new FormatException("流程文件缺少必要数据。");
            document.prefabInputs ??= new List<FlowPrefabInput>();
            document.RemoveRetiredStageNodes();
            foreach (var node in document.nodes)
            {
                if (node == null) continue;
                node.MigrateLegacyTitle();
                if (node.parameters == null) continue;
                node.parameters.prefabGuid = document.PrefabGuid(node) ?? "";
                if (node.type == "on_signal" && !string.IsNullOrEmpty(node.parameters.signal))
                {
                    if (string.IsNullOrEmpty(node.parameters.triggerDescription))
                        node.parameters.triggerDescription = FlowSignals.Label(node.parameters.signal) ?? node.parameters.signal;
                    node.parameters.signal = null;
                }
            }
            return document;
        }

        private void RemoveRetiredStageNodes()
        {
            foreach (var node in nodes.Where(node => node?.type == "stage").ToArray())
            {
                nodes.Remove(node);
                if (string.IsNullOrEmpty(node.id)) continue;
                string next = Output(node.id, "next")?.to;
                edges.RemoveAll(edge => edge?.from == node.id ||
                    (string.IsNullOrEmpty(next) && edge?.to == node.id));
                foreach (var edge in edges.Where(edge => edge?.to == node.id)) edge.to = next;
            }
        }

        public static FlowDocument Empty(string kind, string title)
        {
            var document = new FlowDocument { kind = kind, title = title };
            var start = document.Add("start", new Vector2(30, 70));
            var end = document.Add("end", new Vector2(360, 70));
            document.Connect(start.id, "next", end.id);
            return document;
        }

        public StudioNode Add(string type, Vector2 position)
        {
            var spec = NodeCatalog.Get(type);
            if (spec == null || !spec.Allowed(kind)) throw new InvalidOperationException("当前流程不支持此节点类型。");
            var node = new StudioNode { type = type, Position = position };
            if (type == "wait_region")
            {
                node.parameters.placeRadius = true;
                node.parameters.radius = 3f;
                node.parameters.planarDistance = true;
            }
            if (type == "branch")
            {
                node.parameters.checkDistance = true;
                node.parameters.radius = 3f;
                node.parameters.planarDistance = true;
            }
            if (type == "wait_signal") node.parameters.signal = FlowSignals.ItemPickedUp;
            if (type == "wait_confirmation") node.parameters.note = "准备好后继续。";
            nodes.Add(node);
            if (type == "start" && string.IsNullOrEmpty(entry)) entry = node.id;
            return node;
        }

        public void Connect(string from, string port, string to)
        {
            var source = Find(from);
            var destination = Find(to);
            if (source == null || destination == null || from == to || destination.type == "start" || destination.type == "on_signal")
                throw new InvalidOperationException("无效的连接目标。");
            if (NodeCatalog.Get(source.type)?.ports.Contains(port) != true)
                throw new InvalidOperationException("所选节点没有此分支。");
            if (CanReach(to, from, new HashSet<string>()))
                throw new InvalidOperationException("当前流程不支持循环连线。");
            edges.RemoveAll(edge => edge.from == from && edge.port == port);
            edges.Add(new StudioEdge { from = from, port = port, to = to });
        }

        private bool CanReach(string from, string target, HashSet<string> visited)
        {
            if (from == target) return true;
            if (!visited.Add(from)) return false;
            return edges.Where(edge => edge?.from == from).Any(edge => CanReach(edge.to, target, visited));
        }

        public bool CanReferenceOutput(string source, string consumer) =>
            source != consumer && CanReach(entry, source, new HashSet<string>()) &&
            CanReach(source, consumer, new HashSet<string>()) &&
            !CanReach(entry, consumer, new HashSet<string> { source });

        public bool CanConnect(string source, StudioNode target) => target != null && source != target.id &&
            target.type != "start" && target.type != "on_signal" && !CanReach(target.id, source, new HashSet<string>());

        public void ConnectRegionCenter(string sourceId, string targetId)
        {
            if (Find(sourceId)?.SpawnsObject != true || Find(targetId) is not StudioNode target || target.type != "wait_region")
                throw new InvalidOperationException("请将「生成结果」连接到「范围中心」。");
            var parameters = target.parameters;
            string reference = NodeParameters.SpawnOutput(sourceId);
            parameters.destinationPoint = "";
            parameters.target = reference;
            parameters.placeRadius = true;
        }
    }

    [Serializable]
    public sealed class FlowPrefabInput
    {
        public string id = Guid.NewGuid().ToString("N");
        public string label = "新共享物件";
        public string guid;
        public bool published;
    }

    [Serializable]
    public sealed class StudioNode
    {
        public string id = Guid.NewGuid().ToString("N");
        public string type;
        [SerializeField] private string title;
        public string remarks = "";
        public float x, y;
        public NodeParameters parameters = new NodeParameters();
        public List<PublishedFlowParameter> published = new List<PublishedFlowParameter>();
        public Vector2 Position { get => new Vector2(x, y); set { x = value.x; y = value.y; } }
        public string Name => NodeCatalog.Get(type)?.label ?? "未知节点";
        public bool SpawnsObject => type == "spawn";

        internal void MigrateLegacyTitle()
        {
            if (!string.IsNullOrWhiteSpace(title) && title != Name && title != remarks)
                remarks = string.IsNullOrEmpty(remarks) ? title : title + "\n" + remarks;
            title = null;
        }
    }

    [Serializable]
    public sealed class NodeParameters
    {
        public const string SpawnOutputPrefix = "spawn:";
        public const string SignalObject = "signal:object";
        public const string PlayerObject = "game:player";
        public const string PlayerShip = "player_ship";
        public const string ConditionPrefix = "condition:";
        public static string ConditionOutput(string id) => ConditionPrefix + id;
        public static string SpawnOutput(string id) => SpawnOutputPrefix + id;
        public static bool IsSpawnOutput(string reference) => reference?.StartsWith(SpawnOutputPrefix, StringComparison.Ordinal) == true;
        public static string SpawnNodeId(string reference) => reference.Substring(SpawnOutputPrefix.Length);

        public string target, item, destination;
        public string destinationPoint, sourcePoint, orientation;
        public string prefabGuid, prefabInput;
        public string placement = "anchor";
        public Vector3 placementOffset;
        public Vector3 destinationAngles;
        public Vector3 projectedAnchorOffset;
        public Vector3 spawnOriginOffset;
        public float spawnScale = 1f;
        public bool parentToTargetRoot;
        public bool planarDistance;
        public float radius = 1f;
        public float travelDistance = 1f;
        public float seconds = 1f;
        public string flag, eventFile, signal, triggerDescription, note, actionGuid;
        public string firstChoice, secondChoice, placeLabel;
        public bool placeRadius, checkDistance, customLocation;
        public bool value = true;
        public bool success = true;

        public IEnumerable<string> ObjectReferences(string type)
        {
            if (type == "wait_near" || type == "wait_travel" || type == "wait_region" || type == "branch" && checkDistance)
                yield return PlayerShip;
            if (type == "wait_near" || type == "wait_region") yield return target;
            if (type == "branch" && checkDistance) yield return target;
            if (type == "transfer" || type == "run_action" || type == "wait_signal") yield return item;
            if (type == "transfer" || type == "spawn" || type == "run_action") yield return destination;
            if (type == "spawn" && placement == "projected") yield return orientation;
        }

        public void RemapObjects(string type, Func<string, string> remap)
        {
            if (type == "wait_near" || type == "wait_region") target = remap(target);
            if (type == "branch" && checkDistance) target = remap(target);
            if (type == "transfer" || type == "run_action" || type == "wait_signal") item = remap(item);
            if (type == "transfer" || type == "spawn" || type == "run_action") destination = remap(destination);
            if (type == "spawn" && placement == "projected") orientation = remap(orientation);
        }
    }

    public static class FlowSignals
    {
        public const string ItemPickedUp = "item_picked_up";
        public static string Label(string id) => id == ItemPickedUp ? "玩家拾取物件" : null;
    }

    [Serializable]
    public sealed class StudioEdge
    {
        public string id = Guid.NewGuid().ToString("N");
        public string from, port, to;
    }

    public sealed class NodeSpec
    {
        public readonly string type, label, group, description;
        public readonly string[] ports, portLabels;
        public readonly Color color;

        public NodeSpec(string type, string label, string group, string description, string color,
            string[] ports = null, string[] portLabels = null)
        {
            this.type = type; this.label = label; this.group = group; this.description = description;
            ColorUtility.TryParseHtmlString(color, out this.color);
            this.ports = ports ?? new[] { "next" };
            this.portLabels = portLabels ?? new[] { "继续" };
        }

        public bool Allowed(string kind) =>
            (type != "on_signal" || kind == "main") &&
            (kind != "main" || (type != "spawn" && type != "run_action" && type != "wait_near"));
    }

    public static class NodeCatalog
    {
        public static readonly NodeSpec[] All = {
            new NodeSpec("start", "开始", "流程", "流程起点。", "#68C5A4"),
            new NodeSpec("call_event", "执行事件", "流程", "执行指定事件，按成功或失败继续。", "#B29BEA", new[]{"success","failure"}, new[]{"成功","失败"}),
            new NodeSpec("on_signal", "触发事件", "流程", "记录计划中的触发条件和对应事件。", "#B29BEA", Array.Empty<string>(), Array.Empty<string>()),
            new NodeSpec("end", "结束", "流程", "以选定结果结束当前流程。被其他流程执行时，结果决定后续分支。", "#68C5A4", Array.Empty<string>(), Array.Empty<string>()),
            new NodeSpec("wait_near", "等待靠近", "等待", "等待玩家船靠近前面生成的物件。已在指定距离内时直接继续。", "#E1B96A"),
            new NodeSpec("wait_travel", "等待移动距离", "等待", "从此节点开始累计玩家船的移动距离，达标后继续。往返移动也计入。", "#E1B96A"),
            new NodeSpec("wait_region", "等待进入范围", "等待", "玩家船进入范围后继续。开始时已在范围内则直接继续。", "#E1B96A"),
            new NodeSpec("delay", "等待时间", "等待", "经过设定时长后继续。设为 0 秒时直接继续。", "#E1B96A"),
            new NodeSpec("wait_signal", "等待玩家操作", "等待", "玩家完成指定操作后继续。", "#E1B96A"),
            new NodeSpec("wait_confirmation", "显示提示", "玩家界面", "显示提示，玩家按 Enter 后继续。", "#E1B96A"),
            new NodeSpec("choice", "玩家选择", "玩家界面", "在游戏中显示提示和两个选项。玩家按 1 或 2 进入对应分支。", "#E1B96A", new[]{"first","second"}, new[]{"选项一","选项二"}),
            new NodeSpec("branch", "条件分支", "分支", "到达节点时判断一次条件，立即进入「满足」或「不满足」分支。", "#E1B96A", new[]{"yes","no"}, new[]{"满足","不满足"}),
            new NodeSpec("set_flag", "记录结果", "分支", "记录「是」或「否」，供后续条件分支使用。", "#E1B96A"),
            new NodeSpec("spawn", "生成物件", "场景操作", "在指定位置生成物件。", "#B29BEA"),
            new NodeSpec("transfer", "移动物件", "场景操作", "将已有物件移到目标位置，并采用该位置的朝向。", "#B29BEA"),
            new NodeSpec("run_action", "执行玩法", "场景操作", "执行指定玩法，按成功或失败继续。", "#E78E83", new[]{"success","failure"}, new[]{"成功","失败"})
        };

        public static NodeSpec Get(string type) => All.FirstOrDefault(spec => spec.type == type);

        public static string Summary(FlowDocument document, StudioNode node, Func<string, string> reference, Func<string, string> assetName)
        {
            var p = node.parameters;
            string At(string value) => string.IsNullOrEmpty(value) ? "未选择" : reference(value);
            switch (node.type)
            {
                case "call_event": return p.eventFile;
                case "on_signal": return string.Join(" → ", new[] { p.triggerDescription, p.eventFile }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
                case "wait_near": case "wait_region":
                    var source = NodeParameters.IsSpawnOutput(p.target) ? document.Find(NodeParameters.SpawnNodeId(p.target)) : null;
                    string target = source?.parameters != null ? assetName(document.PrefabGuid(source)) : null;
                    if (node.type == "wait_region" && !p.placeRadius) return target ?? At(p.target);
                    return (target ?? At(p.target)) + " · " + p.radius.ToString("0.##") + " 米";
                case "wait_travel": return p.travelDistance.ToString("0.##") + " 米";
                case "spawn":
                    string prefab = string.IsNullOrEmpty(p.prefabInput) ? assetName(p.prefabGuid) : document.FindPrefabInput(p.prefabInput)?.label;
                    return (prefab ?? "未选物件") + " → " + (string.IsNullOrWhiteSpace(p.placeLabel) ? At(p.destination) : p.placeLabel);
                case "transfer": return At(p.item) + " → " + (string.IsNullOrWhiteSpace(p.placeLabel) ? At(p.destination) : p.placeLabel);
                case "delay": return p.seconds.ToString("0.##") + " 秒";
                case "wait_signal": return FlowSignals.Label(p.signal) ?? "未选玩家操作";
                case "wait_confirmation": case "choice": return p.note;
                case "set_flag": return p.value ? "是" : "否";
                case "branch" when p.checkDistance: return At(p.target) + " · " + p.radius.ToString("0.##") + " 米";
                case "branch": return p.flag?.StartsWith(NodeParameters.ConditionPrefix, StringComparison.Ordinal) == true
                    ? document.Find(p.flag.Substring(NodeParameters.ConditionPrefix.Length))?.Name : p.flag;
                case "run_action": return assetName(p.actionGuid) ?? "未选玩法";
                case "end": return p.success ? "成功" : "失败";
                default: return NodeCatalog.Get(node.type)?.description;
            }
        }
    }
}
