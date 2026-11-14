using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WhiteWhale.FlowStudio;

public static class FlowStudioSetup
{
    public const string ScenePath = "Assets/Scenes/FlowStudio.unity";

    [MenuItem("White Whale/Flow Studio/打开纯 UI 工作台")]
    public static void Open()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(ScenePath);
        EditorApplication.ExecuteMenuItem("Window/General/Game");
    }

    [MenuItem("White Whale/Flow Studio/运行文件与流程检查")]
    public static void VerifyMenu() => Debug.Log(Verify());

    public static string Verify()
    {
        var report = new List<string>();
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("FAIL " + label);
            report.Add("PASS " + label);
        }

        var project = new FlowFileStore(FlowFileStore.DefaultRoot);
        string startupFile = project.StartupFile ?? throw new InvalidOperationException("未设置游戏入口。");
        var main = project.Load(startupFile);
        var errors = FlowValidation.Check(main, project.Load, project.ResolveAsset);
        Check(errors.Count == 0, "startup flow and referenced events validate: " + string.Join(" / ", errors));

        var root = new FlowFileStore(Path.Combine(Path.GetTempPath(), "WhiteWhaleFlowStudioVerification"));
        foreach (string file in project.List().Where(FlowFileStore.IsEvent))
        {
            var package = project.ReadEvent(file);
            root.Save(file, package.graph);
            Check(root.Load(file).ToJson() == package.graph.ToJson(), "event round trip: " + file);
            var asset = AssetDatabase.LoadAssetAtPath<FlowEventAsset>("Assets/Resources/FlowEvents/" + file);
            var imported = asset?.Assets.Where(reference => reference.asset != null).Select(reference => reference.guid).ToHashSet();
            Check(imported != null && imported.SetEquals(package.AssetGuids()), "event dependencies import: " + file);
        }

        var timed = FlowDocument.Empty("event", "等待");
        var delay = timed.Add("delay", new Vector2(150, 70));
        delay.parameters.seconds = 2;
        timed.Connect(timed.entry, "next", delay.id);
        timed.Connect(delay.id, "next", timed.nodes.Single(node => node.type == "end").id);
        var actions = new SimulationActions();
        var session = new FlowSession(timed, root.Load, actions);
        session.Advance(0);
        timed.Find(delay.id).parameters.seconds = 0;
        session.Advance(1);
        Check(session.Status == SessionStatus.Running, "delay runs with the original snapshot");
        session.Advance(1);
        Check(session.Status == SessionStatus.Completed, "delay completes");
        session.Stop();
        Check(session.Status == SessionStatus.Stopped, "stop clears the session");

        var history = new DocumentHistory();
        string before = timed.ToJson();
        var added = timed.Add("set_flag", new Vector2(120, 240));
        history.Record(before, timed);
        timed = history.Undo(timed);
        Check(timed.Find(added.id) == null, "undo restores document snapshot");
        timed = history.Redo(timed);
        Check(timed.Find(added.id) != null, "redo restores node identity");
        Check(FlowValidation.Check(timed).Count > 0, "unconnected node is reported");

        string result = string.Join("\n", report);
        Directory.CreateDirectory("Captures");
        File.WriteAllText("Captures/FlowStudioVerification.txt", result);
        return result;
    }
}
