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
                throw new FormatException("流程文件必须是 JSON 对象。");
            var document = JsonUtility.FromJson<FlowDocument>(json);
            if (document == null || document.version != 1) throw new FormatException("不支持此流程文件版本。");
            if (document.kind != "main" && document.kind != "event")
                throw new FormatException("流程类型必须是 main 或 event。");
            if (string.IsNullOrWhiteSpace(document.id) || document.nodes == null || document.edges == null)
                throw new FormatException("流程文件缺少必要数据。");
            document.prefabInputs ??= new List<FlowPrefabInput>();
            foreach (var node in document.nodes.Where(node => node?.parameters != null))
                node.parameters.prefabGuid = document.PrefabGuid(node) ?? "";
            return document;
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
            if (spec == null || !spec.Allowed(kind)) throw new InvalidOperationException("当前流程不支持此节点。");
            var node = new StudioNode { type = type, title = spec.label, Position = position };
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
                throw new InvalidOperationException("节点没有此出口。");
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
            source != consumer && !CanReach(consumer, source, new HashSet<string>()) &&
            !CanReach(entry, consumer, new HashSet<string> { source });
    }

    [Serializable]
    public sealed class FlowPrefabInput
    {
        public string id = Guid.NewGuid().ToString("N");
        public string label = "新预制体";
        public string guid;
        public bool published;
    }

    [Serializable]
    public sealed class StudioNode
    {
        public string id = Guid.NewGuid().ToString("N");
        public string type;
        public string title;
        public float x, y;
        public NodeParameters parameters = new NodeParameters();
        public List<PublishedFlowParameter> published = new List<PublishedFlowParameter>();
        public Vector2 Position { get => new Vector2(x, y); set { x = value.x; y = value.y; } }
        public bool SpawnsObject => type == "spawn";
    }

    [Serializable]
    public sealed class NodeParameters
    {
        public const string SpawnOutputPrefix = "spawn:";
        public const string SignalObject = "signal:object";
        public const string ConditionPrefix = "condition:";
        public static string ConditionOutput(string id) => ConditionPrefix + id;
        public static string SpawnOutput(string id) => SpawnOutputPrefix + id;
        public static bool IsSpawnOutput(string reference) => reference?.StartsWith(SpawnOutputPrefix, StringComparison.Ordinal) == true;
        public static string SpawnNodeId(string reference) => reference.Substring(SpawnOutputPrefix.Length);

        public string actor, target, item, destination;
        public string destinationPoint, sourcePoint, orientation;
        public string prefabGuid, prefabInput;
        public string placement = "anchor";
        public Vector3 placementOffset;
        public float spawnScale = 1f;
        public bool parentToTargetRoot;
        public bool planarDistance;
        public float radius = 1f;
        public float travelDistance = 1f;
        public float seconds = 1f;
        public string stage, flag, eventFile, signal, note, actionGuid;
        public bool value = true;
        public bool success = true;

        public IEnumerable<string> ObjectReferences(string type)
        {
            if (type == "wait_near" || type == "wait_travel" || type == "wait_region") yield return actor;
            if (type == "wait_near" || type == "wait_region") yield return target;
            if (type == "transfer" || type == "run_action") yield return item;
            if (type == "transfer" || type == "spawn" || type == "run_action") yield return destination;
            if (type == "spawn" && placement == "projected") yield return orientation;
        }

        public void RemapObjects(string type, Func<string, string> remap)
        {
            if (type == "wait_near" || type == "wait_travel" || type == "wait_region") actor = remap(actor);
            if (type == "wait_near" || type == "wait_region") target = remap(target);
            if (type == "transfer" || type == "run_action") item = remap(item);
            if (type == "transfer" || type == "spawn" || type == "run_action") destination = remap(destination);
            if (type == "spawn" && placement == "projected") orientation = remap(orientation);
        }
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
            (type != "on_signal" || kind == "main") && (kind != "main" || (type != "spawn" && type != "run_action"));
    }

    public static class NodeCatalog
    {
        public static readonly NodeSpec[] All = {
            new NodeSpec("start", "开始", "流程", "流程的执行入口。", "#68C5A4"),
            new NodeSpec("call_event", "调用事件", "流程", "运行事件并接收结果。", "#B29BEA", new[]{"success","failure"}, new[]{"成功","失败"}),
            new NodeSpec("on_signal", "信号触发事件", "流程", "收到指定信号时独立启动事件。", "#B29BEA", Array.Empty<string>(), Array.Empty<string>()),
            new NodeSpec("end", "结束", "流程", "返回流程结果。", "#68C5A4", Array.Empty<string>(), Array.Empty<string>()),
            new NodeSpec("stage", "标记阶段", "流程", "记录当前阶段名称。", "#79AEEF"),
            new NodeSpec("wait_near", "等待距离", "等待", "等待两个对象的距离达到阈值。", "#E1B96A"),
            new NodeSpec("wait_travel", "等待累计位移", "等待", "累计对象自进入节点后的移动距离。", "#E1B96A"),
            new NodeSpec("wait_region", "等待进入区域", "等待", "等待指定对象进入所选区域。", "#E1B96A"),
            new NodeSpec("wait_confirmation", "等待确认", "等待", "显示提示内容并等待外部确认。", "#E1B96A"),
            new NodeSpec("delay", "等待时间", "等待", "等待指定秒数。", "#E1B96A"),
            new NodeSpec("wait_signal", "等待信号", "等待", "等待指定信号，可引用信号携带的对象。", "#E1B96A"),
            new NodeSpec("set_flag", "设置条件", "条件", "设置布尔值供后续分支读取。", "#E1B96A"),
            new NodeSpec("branch", "条件分支", "条件", "按所选条件的值选择出口。", "#E1B96A", new[]{"yes","no"}, new[]{"是","否"}),
            new NodeSpec("spawn", "生成对象", "动作", "用预制体在目标位置生成一个对象。", "#B29BEA"),
            new NodeSpec("transfer", "移动对象", "动作", "将同一个对象移动到目标位置。", "#B29BEA"),
            new NodeSpec("run_action", "执行动作", "动作", "运行所选动作资源并等待结果。", "#E78E83", new[]{"success","failure"}, new[]{"成功","失败"})
        };

        public static NodeSpec Get(string type) => All.FirstOrDefault(spec => spec.type == type);

        public static string Summary(FlowDocument document, StudioNode node, Func<string, string> reference, Func<string, string> assetName)
        {
            var p = node.parameters;
            string At(string value) => string.IsNullOrEmpty(value) ? "未选择" : reference(value);
            switch (node.type)
            {
                case "call_event": return p.eventFile;
                case "on_signal": return p.signal + " → " + p.eventFile;
                case "wait_near": return At(p.target) + " · " + p.radius.ToString("0.##") + " 米";
                case "wait_region": return At(p.actor) + " → " + At(p.target);
                case "wait_travel": return At(p.actor) + " · " + p.travelDistance.ToString("0.##") + " 米";
                case "stage": return p.stage;
                case "spawn":
                    string prefab = string.IsNullOrEmpty(p.prefabInput) ? assetName(p.prefabGuid) : document.FindPrefabInput(p.prefabInput)?.label;
                    return (prefab ?? "未选择预制体") + " → " + At(p.destination);
                case "transfer": return At(p.item) + " → " + At(p.destination);
                case "delay": return p.seconds.ToString("0.##") + " 秒";
                case "wait_signal": return p.signal;
                case "set_flag": return p.value ? "是" : "否";
                case "branch": return p.flag?.StartsWith(NodeParameters.ConditionPrefix, StringComparison.Ordinal) == true
                    ? document.Find(p.flag.Substring(NodeParameters.ConditionPrefix.Length))?.title : p.flag;
                case "run_action": return assetName(p.actionGuid) ?? "未选择动作";
                case "end": return p.success ? "返回成功" : "返回失败";
                default: return NodeCatalog.Get(node.type)?.description;
            }
        }
    }
}
