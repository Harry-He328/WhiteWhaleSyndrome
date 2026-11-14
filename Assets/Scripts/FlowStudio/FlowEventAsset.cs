using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WhiteWhale.FlowStudio
{
    [Serializable]
    public sealed class FlowEventPackage
    {
        public int version = 1;
        public FlowDocument graph;

        public static FlowEventPackage FromJson(string json)
        {
            var package = JsonUtility.FromJson<FlowEventPackage>(json);
            if (package == null || package.version != 1 || package.graph?.kind != "event")
                throw new FormatException("事件文件版本或内容无效。");
            package.graph = FlowDocument.FromJson(package.graph.ToJson());
            return package;
        }

        public IEnumerable<string> AssetGuids() => graph.nodes.Where(node => node?.parameters != null)
            .SelectMany(node => new[] { graph.PrefabGuid(node), node.parameters.actionGuid })
            .Concat(graph.prefabInputs.Where(input => input != null).Select(input => input.guid))
            .Where(guid => !string.IsNullOrEmpty(guid)).Distinct();
    }

    public sealed class FlowEventAsset : ScriptableObject
    {
        [Serializable]
        public struct AssetReference
        {
            public string guid;
            public UnityEngine.Object asset;
        }

        [SerializeField, HideInInspector] private string source;
        [SerializeField] private AssetReference[] assets;

        public FlowEventPackage Read() => FlowEventPackage.FromJson(source);
        public IEnumerable<AssetReference> Assets => assets ?? Array.Empty<AssetReference>();

#if UNITY_EDITOR
        public void Initialize(string json, AssetReference[] references)
        {
            source = json;
            assets = references;
        }
#endif
    }
}
