using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WhiteWhale.FlowStudio
{
    [Serializable]
    public sealed class PublishedFlowParameter
    {
        public string key;
        public string label;
    }

    public sealed class FlowExposedParameter
    {
        private readonly FieldInfo field;
        public string Key => field.Name;
        public Type ValueType => field.FieldType;
        public string Label { get; }
        public bool IsPrefab => Key == nameof(NodeParameters.prefabGuid);

        private FlowExposedParameter(string key, string label)
        {
            field = typeof(NodeParameters).GetField(key);
            Label = label;
        }

        public object Read(StudioNode node) => field.GetValue(node.parameters);
        public void Write(StudioNode node, object value) => field.SetValue(node.parameters, value);

        private static FlowExposedParameter Param(string key, string label) => new FlowExposedParameter(key, label);
        private static readonly Dictionary<string, FlowExposedParameter[]> fields = new Dictionary<string, FlowExposedParameter[]>
        {
            ["spawn"] = new[] { Param(nameof(NodeParameters.prefabGuid), "物件"),
                Param(nameof(NodeParameters.placementOffset), "位置偏移（米）"), Param(nameof(NodeParameters.spawnScale), "缩放倍数") },
            ["wait_travel"] = new[] { Param(nameof(NodeParameters.travelDistance), "累计移动距离（米）") },
            ["wait_near"] = new[] { Param(nameof(NodeParameters.radius), "距离上限（米）") },
            ["delay"] = new[] { Param(nameof(NodeParameters.seconds), "时长（秒）") },
            ["wait_confirmation"] = new[] { Param(nameof(NodeParameters.note), "提示内容") },
            ["set_flag"] = new[] { Param(nameof(NodeParameters.value), "结果") }
        };

        public static IEnumerable<FlowExposedParameter> Publishable(StudioNode node)
        {
            if (node?.type == null || node.parameters == null || !fields.TryGetValue(node.type, out var available)) yield break;
            foreach (var field in available)
            {
                if (field.IsPrefab && !string.IsNullOrEmpty(node.parameters.prefabInput)) continue;
                yield return field;
            }
        }

        public static IEnumerable<(PublishedFlowParameter setting, FlowExposedParameter field)> Published(StudioNode node)
        {
            var candidates = Publishable(node).ToDictionary(field => field.Key);
            foreach (var setting in node?.published ?? Enumerable.Empty<PublishedFlowParameter>())
                if (setting?.key != null && candidates.TryGetValue(setting.key, out var field))
                    yield return (setting, field);
        }
    }
}
