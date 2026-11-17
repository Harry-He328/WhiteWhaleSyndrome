using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using WhiteWhale.FlowStudio;

[ScriptedImporter(2, "event")]
public sealed class FlowEventImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        string json = File.ReadAllText(ctx.assetPath);
        var package = FlowEventPackage.FromJson(json);
        var references = package.AssetGuids().Select(guid =>
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                ctx.LogImportWarning("找不到事件引用的资源：" + guid);
                return new FlowEventAsset.AssetReference { guid = guid };
            }
            // A known asset can still be importing. Its artifact change will reimport this event.
            ctx.DependsOnArtifact(path);
            var resource = AssetDatabase.LoadMainAssetAtPath(path);
            return new FlowEventAsset.AssetReference { guid = guid, asset = resource };
        }).ToArray();
        var asset = ScriptableObject.CreateInstance<FlowEventAsset>();
        asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
        asset.Initialize(json, references);
        ctx.AddObjectToAsset("event", asset);
        ctx.SetMainObject(asset);
    }
}

[CustomEditor(typeof(FlowEventImporter))]
internal sealed class FlowEventAssetInspector : ScriptedImporterEditor
{
    public override bool showImportedObject => false;
    protected override bool needsApplyRevert => false;

    private string source, savedDraft, error;
    private FlowEventPackage draft;

    private bool HasChanges => draft != null && JsonUtility.ToJson(draft) != savedDraft;

    public override void OnInspectorGUI()
    {
        string path = ((FlowEventImporter)target).assetPath;
        try
        {
            string disk = File.ReadAllText(path);
            if (draft == null || (disk != source && !HasChanges)) ResetDraft(disk);
            var graph = draft.graph;
            EditorGUILayout.LabelField(graph.title, EditorStyles.boldLabel);
            var inputs = graph.prefabInputs.Where(input => input?.published == true).ToArray();
            var fields = graph.nodes.SelectMany(node => FlowExposedParameter.Published(node)
                .Select(entry => (node, entry.setting, entry.field))).ToArray();
            if (inputs.Length == 0 && fields.Length == 0)
            {
                EditorGUILayout.HelpBox("暂无公开设置。可在事件编辑器中选择需要公开的设置。", MessageType.Info);
                return;
            }

            foreach (var input in inputs) DrawPrefab(input.label, input.guid, guid => input.guid = guid);
            foreach (var (node, setting, field) in fields)
                DrawField(node, field, string.IsNullOrWhiteSpace(setting.label) ? field.Label : setting.label);

            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
            bool changes = HasChanges;
            if (changes && disk != source)
                EditorGUILayout.HelpBox("事件文件已在别处修改。请还原以重新载入。", MessageType.Warning);

            bool apply;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!changes))
                    if (GUILayout.Button("还原")) ResetDraft(disk);
                using (new EditorGUI.DisabledScope(!HasChanges || disk != source))
                    apply = GUILayout.Button("应用修改");
            }
            if (apply) Apply(path);
        }
        catch (ExitGUIException) { throw; }
        catch (Exception exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Error); }
    }

    private void ResetDraft(string json)
    {
        draft = FlowEventPackage.FromJson(json);
        savedDraft = JsonUtility.ToJson(draft);
        source = json;
        error = null;
    }

    private void Apply(string path)
    {
        var files = new FlowFileStore(FlowFileStore.DefaultRoot);
        var errors = FlowValidation.Check(draft.graph, files.Load, files.ResolveAsset);
        if (errors.Count != 0) { error = string.Join("\n", errors); return; }
        if (File.ReadAllText(path) != source) { error = "事件文件已在别处修改。"; return; }
        FlowFileStore.WriteEventPackage(path, draft);
        ResetDraft(File.ReadAllText(path));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        GUIUtility.ExitGUI();
    }

    private void DrawPrefab(string label, string guid, Action<string> assign)
    {
        var prefab = string.IsNullOrEmpty(guid) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
        EditorGUI.BeginChangeCheck();
        var selected = (GameObject)EditorGUILayout.ObjectField(label, prefab, typeof(GameObject), false);
        if (!EditorGUI.EndChangeCheck()) return;
        string path = selected != null ? AssetDatabase.GetAssetPath(selected) : "";
        if (selected != null && (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
            AssetDatabase.LoadMainAssetAtPath(path) != selected))
        {
            error = "请选择项目中的物件资源。";
            return;
        }
        assign(selected == null ? "" : AssetDatabase.AssetPathToGUID(path));
        error = null;
    }

    private void DrawField(StudioNode node, FlowExposedParameter field, string label)
    {
        object value = field.Read(node);
        if (field.IsPrefab) { DrawPrefab(label, (string)value, guid => field.Write(node, guid)); return; }
        EditorGUI.BeginChangeCheck();
        if (value is float number) value = EditorGUILayout.DelayedFloatField(label, number);
        else if (value is bool state) value = EditorGUILayout.Toggle(label, state);
        else if (value is Vector3 vector) value = EditorGUILayout.Vector3Field(label, vector);
        else if (field.ValueType == typeof(string)) value = EditorGUILayout.DelayedTextField(label, (string)value ?? "");
        if (!EditorGUI.EndChangeCheck()) return;
        field.Write(node, value);
        error = null;
    }
}
