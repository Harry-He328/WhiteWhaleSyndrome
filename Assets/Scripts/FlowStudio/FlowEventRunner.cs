using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using WhiteWhale.Flow;

namespace WhiteWhale.FlowStudio
{
    [DisallowMultipleComponent, AddComponentMenu("")]
    public sealed class FlowEventRunner : MonoBehaviour
    {
        private FlowFileStore files;
        private FlowDocument main;
        private FlowSession session;
        private SceneFlowActions actions;
        private Scene[] scenes;
        private readonly List<(FlowSession session, SceneFlowActions actions)> triggered =
            new List<(FlowSession session, SceneFlowActions actions)>();

        public SessionStatus? Status => session?.Status;
        public string Stage => actions?.Stage;
        public string EventStatus { get; private set; }
        public string CurrentEventMessage => EventStatus ?? session?.Message;
        private FlowSession ConfirmationSession => IsConfirming(session) ? session : triggered.Select(run => run.session).FirstOrDefault(IsConfirming);
        private static bool IsConfirming(FlowSession run) => run?.Status == SessionStatus.Running && run.CurrentNode?.type == "wait_confirmation";
        public string ConfirmationText => ConfirmationSession?.CurrentNode.parameters.note;

        public void StartFlow(params Scene[] bindingScenes)
        {
            scenes = bindingScenes;
            RestartEvent();
        }

        private void OnEnable()
        {
            if (scenes != null) RestartEvent();
        }

        private void Update()
        {
            foreach (var run in triggered) Advance(run.session);
            Advance(session);
        }

        private void Advance(FlowSession run)
        {
            if (run?.Status != SessionStatus.Running) return;
            run.Advance(Time.deltaTime);
            if (run.ExecutionError == null) return;
            EventStatus = run.ExecutionError;
            Debug.LogError(EventStatus, this);
            run.Stop();
        }

        public void Confirm()
        {
            var current = ConfirmationSession;
            if (current == null) return;
            if (current == session) actions.Confirm();
            else triggered.Find(run => run.session == current).actions.Confirm();
        }

        public bool PublishSignal(string name, GameObject subject = null)
        {
            if (!isActiveAndEnabled || main == null || string.IsNullOrWhiteSpace(name)) return false;
            name = name.Trim();
            bool handled = session?.ReceiveSignal(name, subject) == true;
            foreach (var run in triggered) handled |= run.session.ReceiveSignal(name, subject);
            foreach (var trigger in main.nodes.Where(node => node.type == "on_signal" &&
                         string.Equals(node.parameters.signal?.Trim(), name, StringComparison.Ordinal)))
            {
                SceneFlowActions handler = null;
                try
                {
                    var document = files.Load(trigger.parameters.eventFile);
                    handler = CreateActions();
                    var run = CreateSession(document, handler, subject);
                    triggered.Add((run, handler));
                    handled = true;
                }
                catch (Exception error)
                {
                    handler?.Stop();
                    Debug.LogError(trigger.title + "：" + error.Message, this);
                }
            }
            return handled;
        }

        private SceneFlowActions CreateActions()
        {
            var bindings = new Dictionary<string, GameObject>();
            foreach (var scene in (scenes ?? new[] { gameObject.scene }).Distinct())
            {
                if (!scene.isLoaded) continue;
                foreach (var binding in scene.GetRootGameObjects()
                             .SelectMany(root => root.GetComponentsInChildren<FlowSceneBindings>(true))
                             .SelectMany(component => component.Objects))
                {
                    if (binding?.target == null || string.IsNullOrWhiteSpace(binding.key)) continue;
                    if (!bindings.TryAdd(binding.key, binding.target))
                        throw new InvalidOperationException("重复的场景绑定：" + binding.key);
                }
            }
            return new SceneFlowActions(new FlowActionContext(gameObject), files.ResolveAsset, bindings);
        }

        private FlowSession CreateSession(FlowDocument document, SceneFlowActions handler, GameObject subject = null)
        {
            var errors = FlowValidation.Check(document, files.Load, files.ResolveAsset);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            FlowDocument Load(string file)
            {
                var child = files.Load(file);
                handler.ValidateBindings(child);
                return child;
            }
            handler.ValidateBindings(document);
            var run = new FlowSession(document, Load, handler, subject);
            if (run.ExecutionError != null) throw new InvalidOperationException(run.ExecutionError);
            return run;
        }

        public void RestartEvent()
        {
            StopRuns();
            EventStatus = null;
            try
            {
                files = new FlowFileStore(FlowFileStore.DefaultRoot);
                FlowFiles.CopyBundledFiles(files);
                if (files.StartupFile == null) return;
                var document = files.Load(files.StartupFile);
                if (document.kind != "main") throw new InvalidOperationException("入口必须是整体流程。");
                actions = CreateActions();
                session = CreateSession(document, actions);
                main = document;
            }
            catch (Exception error)
            {
                actions?.Stop();
                actions = null;
                EventStatus = "流程无法启动：" + error.Message;
                Debug.LogError(EventStatus, this);
            }
        }

        private void OnDisable() => StopRuns();

        private void StopRuns()
        {
            for (int i = triggered.Count - 1; i >= 0; i--) triggered[i].session.Stop();
            triggered.Clear();
            session?.Stop();
            session = null;
            actions = null;
            main = null;
        }
    }
}
