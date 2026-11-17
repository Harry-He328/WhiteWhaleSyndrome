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
        private FlowSession session;
        private SceneFlowActions actions;
        private Scene[] scenes;
        private Font promptFont;
        private GUIStyle promptStyle;
        private GUIStyle keyStyle;
        public SessionStatus? Status => session?.Status;
        public string EventStatus { get; private set; }
        public string CurrentEventMessage => EventStatus ?? session?.Message;
        private StudioNode PromptNode => session?.Status == SessionStatus.Running &&
            (session.CurrentNode?.type == "wait_confirmation" || session.CurrentNode?.type == "choice")
            ? session.CurrentNode : null;
        public string ConfirmationText => PromptNode?.parameters.note;

        public void StartFlow(params Scene[] bindingScenes)
        {
            scenes = bindingScenes;
            RestartEvent();
        }

        private void OnEnable()
        {
            PlayerPickup.ItemPickedUp += OnItemPickedUp;
            if (scenes != null) RestartEvent();
        }

        private void OnItemPickedUp(PlayerPickup player, GameObject item)
        {
            if (scenes != null && scenes.Contains(player.gameObject.scene))
                PublishSignal(FlowSignals.ItemPickedUp, item);
        }

        private void Update()
        {
            if (session?.Status != SessionStatus.Running) return;
            session.Advance(Time.deltaTime);
            if (session.ExecutionError == null) return;
            EventStatus = session.ExecutionError;
            Debug.LogError(EventStatus, this);
            session.Stop();
        }

        public void Confirm()
        {
            if (PromptNode?.type == "wait_confirmation") actions.Confirm();
        }

        public void Choose(bool first)
        {
            if (PromptNode?.type == "choice") actions.Choose(first ? "first" : "second");
        }

        private void OnGUI()
        {
            var node = PromptNode;
            if (node == null || Event.current.type != EventType.Repaint) return;
            if (promptStyle == null)
            {
                promptFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 20);
                promptStyle = new GUIStyle(GUI.skin.label) { font = promptFont, fontSize = 21, wordWrap = true };
                promptStyle.normal.textColor = Color.white;
                keyStyle = new GUIStyle(promptStyle) { fontSize = 16 };
                keyStyle.normal.textColor = new Color(.77f, .89f, .91f);
            }
            var p = node.parameters;
            string text = p.note ?? "";
            string keys = node.type == "choice" ? "1  " + p.firstChoice + "\n2  " + p.secondChoice : "Enter  继续";
            float width = Mathf.Min(Screen.width - 32, 620);
            float textHeight = string.IsNullOrWhiteSpace(text) ? 0 : promptStyle.CalcHeight(new GUIContent(text), width - 40) + 10;
            float keyHeight = keyStyle.CalcHeight(new GUIContent(keys), width - 40);
            var panel = new Rect((Screen.width - width) / 2, Mathf.Max(12, Screen.height - textHeight - keyHeight - 76),
                width, textHeight + keyHeight + 32);
            var color = GUI.color;
            GUI.color = new Color(.05f, .09f, .12f, .94f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = color;
            if (textHeight > 0) GUI.Label(new Rect(panel.x + 20, panel.y + 16, width - 40, textHeight), text, promptStyle);
            GUI.Label(new Rect(panel.x + 20, panel.y + 16 + textHeight, width - 40, keyHeight), keys, keyStyle);
        }

        public bool PublishSignal(string name, GameObject subject = null) =>
            isActiveAndEnabled && session?.ReceiveSignal(name, subject) == true;

        private SceneFlowActions CreateActions()
        {
            var bindings = new Dictionary<string, GameObject>();
            var players = new List<PlayerPickup>();
            foreach (var scene in (scenes ?? new[] { gameObject.scene }).Distinct())
            {
                if (!scene.isLoaded) continue;
                var roots = scene.GetRootGameObjects();
                players.AddRange(roots.SelectMany(root => root.GetComponentsInChildren<PlayerPickup>(true)));
                foreach (var binding in roots
                             .SelectMany(root => root.GetComponentsInChildren<FlowSceneBindings>(true))
                             .SelectMany(component => component.Objects))
                {
                    if (binding?.target == null || string.IsNullOrWhiteSpace(binding.key)) continue;
                    if (!bindings.TryAdd(binding.key, binding.target))
                        throw new InvalidOperationException("重复的场景绑定：" + binding.key);
                }
            }
            if (players.Count == 1) bindings[NodeParameters.PlayerObject] = players[0].gameObject;
            return new SceneFlowActions(new FlowActionContext(gameObject), files.ResolveAsset, bindings);
        }

        private FlowSession CreateSession(FlowDocument document, SceneFlowActions handler)
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
            var run = new FlowSession(document, Load, handler);
            if (run.ExecutionError != null) throw new InvalidOperationException(run.ExecutionError);
            return run;
        }

        public void RestartEvent()
        {
            StopSession();
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
            }
            catch (Exception error)
            {
                actions?.Stop();
                actions = null;
                EventStatus = "流程无法启动：" + error.Message;
                Debug.LogError(EventStatus, this);
            }
        }

        private void OnDisable()
        {
            PlayerPickup.ItemPickedUp -= OnItemPickedUp;
            StopSession();
            if (promptFont != null) Destroy(promptFont);
            promptStyle = null;
            keyStyle = null;
        }

        private void StopSession()
        {
            session?.Stop();
            session = null;
            actions = null;
        }
    }
}
