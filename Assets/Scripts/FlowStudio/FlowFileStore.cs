using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WhiteWhale.FlowStudio
{
    public sealed class FlowFileStore
    {
        private const string EventFolder = "FlowEvents";
        private const string LaunchFile = "startup.json";
        [Serializable]
        private sealed class LaunchSettings { public string mainFile; }
        private readonly Dictionary<string, UnityEngine.Object> assets = new Dictionary<string, UnityEngine.Object>();
        private readonly bool projectStore;
#if !UNITY_EDITOR
        private bool loadedCatalog;
#endif

        public readonly string Root;
        public FlowFileStore(string root)
        {
            Root = Path.GetFullPath(root);
            projectStore = string.Equals(Root, Path.GetFullPath(DefaultRoot), StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Root);
        }

        public static string DefaultRoot => Application.isEditor ? Path.Combine(Application.streamingAssetsPath, "Flows") :
            Path.Combine(Application.persistentDataPath, "Flows");
        public string EventRoot => Application.isEditor && projectStore ? Path.Combine(Application.dataPath, "Resources", EventFolder) : Root;

        public string StartupFile
        {
            get
            {
                string path = Path.Combine(Root, LaunchFile);
                return File.Exists(path) ? FileName(JsonUtility.FromJson<LaunchSettings>(File.ReadAllText(path))?.mainFile) : null;
            }
        }

        public void SetStartupFile(string file)
        {
            file = FileName(file);
            var document = Load(file);
            if (document.kind != "main") throw new ArgumentException("请选择整体流程作为入口。");
            var errors = FlowValidation.Check(document, Load, ResolveAsset);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            Write(Path.Combine(Root, LaunchFile), JsonUtility.ToJson(new LaunchSettings { mainFile = file }, true));
        }

        public void CopyStartupSettings(string sourceDirectory)
        {
            string source = Path.Combine(sourceDirectory, LaunchFile);
            string target = Path.Combine(Root, LaunchFile);
            if (!File.Exists(target) && File.Exists(source)) File.Copy(source, target);
        }

        public static bool IsEvent(string name) => name?.EndsWith(".event", StringComparison.OrdinalIgnoreCase) == true;

        public static string FileName(string name, bool eventFile = false)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.Contains("/") || name.Contains("\\") || name == "." || name == "..")
                throw new ArgumentException("请输入文件名，不包含目录或特殊字符。");
            const string flowSuffix = ".flow.json";
            if (eventFile && !IsEvent(name))
                return (name.EndsWith(flowSuffix, StringComparison.OrdinalIgnoreCase)
                    ? name.Substring(0, name.Length - flowSuffix.Length) : name) + ".event";
            return IsEvent(name) || name.EndsWith(flowSuffix, StringComparison.OrdinalIgnoreCase) ? name : name + flowSuffix;
        }

        public string PathFor(string file) => Path.Combine(IsEvent(file) ? EventRoot : Root, FileName(file));

        public string[] List()
        {
            var events = Directory.Exists(EventRoot) ? Directory.GetFiles(EventRoot, "*.event").Select(Path.GetFileName) :
                Enumerable.Empty<string>();
            if (projectStore && !Application.isEditor)
                events = events.Concat(Resources.LoadAll<FlowEventAsset>(EventFolder).Select(asset => asset.name + ".event"));
            return Directory.GetFiles(Root, "*.flow.json").Select(Path.GetFileName).Concat(events)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToArray();
        }

        public FlowDocument Load(string file)
        {
            file = FileName(file);
            if (IsEvent(file)) return ReadEvent(file).graph;
            string path = PathFor(file);
            if (!File.Exists(path)) throw new FileNotFoundException("找不到流程文件：" + file);
            return FlowDocument.FromJson(File.ReadAllText(path));
        }

        public FlowEventPackage ReadEvent(string file)
        {
            file = FileName(file);
            if (!IsEvent(file)) throw new ArgumentException("请选择 .event 文件。");
            string path = PathFor(file);
            if (File.Exists(path)) return FlowEventPackage.FromJson(File.ReadAllText(path));
            var asset = projectStore ? Resources.Load<FlowEventAsset>(EventFolder + "/" + Path.GetFileNameWithoutExtension(file)) : null;
            if (asset == null) throw new FileNotFoundException("找不到事件文件：" + file);
            Register(asset);
            return asset.Read();
        }

        private void Register(FlowEventAsset asset)
        {
            foreach (var entry in asset.Assets)
                if (entry.asset != null) assets[entry.guid] = entry.asset;
        }

        public UnityEngine.Object ResolveAsset(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            if (assets.TryGetValue(guid, out var asset) && asset != null) return asset;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadMainAssetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
#else
            if (projectStore && !loadedCatalog)
            {
                loadedCatalog = true;
                foreach (var entry in Resources.LoadAll<FlowEventAsset>(EventFolder)) Register(entry);
            }
            return assets.GetValueOrDefault(guid);
#endif
        }

        public void Save(string file, FlowDocument document)
        {
            if (!IsEvent(file)) { Write(PathFor(file), document.ToJson()); return; }
            if (document.kind != "event") throw new ArgumentException("只有事件流程可保存为 .event 文件。");
            WriteEventPackage(PathFor(file), new FlowEventPackage { graph = document });
#if UNITY_EDITOR
            if (projectStore)
                UnityEditor.AssetDatabase.ImportAsset("Assets/Resources/" + EventFolder + "/" + FileName(file),
                    UnityEditor.ImportAssetOptions.ForceSynchronousImport);
#endif
        }

        public void MergePublishedValues(string file, FlowDocument document, FlowDocument baseline)
        {
            if (baseline == null || !IsEvent(file) || !File.Exists(PathFor(file))) return;
            var latest = ReadEvent(file).graph;
            if (latest.id != baseline.id) throw new InvalidOperationException("文件已被替换，请重新打开后编辑。");
            foreach (var input in document.prefabInputs.Where(input => input?.published == true))
            {
                var previous = baseline.FindPrefabInput(input.id);
                var current = latest.FindPrefabInput(input.id);
                if (previous != null && current?.published == true && input.guid == previous.guid) input.guid = current.guid;
            }
            foreach (var node in document.nodes)
            {
                var before = baseline.Find(node.id);
                var after = latest.Find(node.id);
                if (before?.type != node.type || after?.type != node.type) continue;
                var keys = FlowExposedParameter.Published(after).Select(entry => entry.field.Key).ToHashSet();
                foreach (var (_, field) in FlowExposedParameter.Published(node))
                    if (keys.Contains(field.Key) && Equals(field.Read(node), field.Read(before)))
                        field.Write(node, field.Read(after));
            }
        }

        public static void WriteEventPackage(string path, FlowEventPackage package) =>
            Write(path, JsonUtility.ToJson(package, true));

        private static void Write(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
