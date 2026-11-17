using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WhiteWhale.FlowStudio
{
    /// <summary>Game integrations implement this boundary; the editor uses SimulationActions.</summary>
    public interface IFlowActions
    {
        void Enter(StudioNode node, Dictionary<string, GameObject> spawned);
        // null = waiting, otherwise an output port ID. Throw to report an execution error.
        string Poll(StudioNode node, float elapsed, Dictionary<string, GameObject> spawned);
        bool MatchesObject(string reference, GameObject item, Dictionary<string, GameObject> spawned) => true;
        void Stop();
    }

    public enum SessionStatus { Running, Completed, Failed, Stopped }

    /// <summary>Executes a snapshot of the graph and its referenced events.</summary>
    public sealed class FlowSession
    {
        private sealed class Frame
        {
            public FlowDocument document;
            public StudioNode node;
            public float elapsed;
            public bool entered;
            public bool receivedSignal;
            public readonly Dictionary<string, GameObject> spawned = new Dictionary<string, GameObject>();
        }
        private readonly List<Frame> stack = new List<Frame>();
        private readonly Dictionary<string, FlowDocument> events = new Dictionary<string, FlowDocument>();
        private readonly IFlowActions actions;
        public SessionStatus Status { get; private set; } = SessionStatus.Running;
        public string ExecutionError { get; private set; }
        public string Message { get; private set; } = "流程已开始";
        public List<string> Log { get; } = new List<string>();
        public HashSet<string> Completed { get; } = new HashSet<string>();
        private Frame Current => stack.Count == 0 ? null : stack[stack.Count - 1];
        public FlowDocument CurrentDocument => Current?.document;
        public StudioNode CurrentNode => Current?.node;
        public string Breadcrumb => string.Join(" / ", stack.Select(f => f.document.title));
        public event Action Changed;

        public bool ReceiveSignal(string name, GameObject subject = null)
        {
            var frame = Current;
            if (Status != SessionStatus.Running || frame == null || !frame.entered || frame.receivedSignal ||
                frame.node.type != "wait_signal" || string.IsNullOrWhiteSpace(name) ||
                !string.Equals(frame.node.parameters.signal?.Trim(), name.Trim(), StringComparison.Ordinal))
                return false;
            string item = frame.node.parameters.item;
            if (!string.IsNullOrEmpty(item) && !actions.MatchesObject(item, subject, frame.spawned)) return false;
            frame.spawned[NodeParameters.SignalObject] = subject;
            frame.receivedSignal = true;
            return true;
        }

        public FlowSession(FlowDocument root, Func<string, FlowDocument> load, IFlowActions actions)
        {
            this.actions = actions;
            FlowDocument CachedLoad(string name)
            {
                string key = FlowFileStore.FileName(name);
                if (!events.TryGetValue(key, out FlowDocument doc))
                {
                    doc = load(key).Clone();
                    events[key] = doc;
                }
                return doc;
            }
            root = root.Clone();
            List<string> errors = FlowValidation.Check(root, CachedLoad);
            if (errors.Count > 0) { Fail(string.Join("\n", errors)); return; }
            var frame = new Frame { document = root, node = root.Find(root.entry) };
            stack.Add(frame);
        }

        public void Advance(float deltaTime, int transitionBudget = 128)
        {
            if (Status != SessionStatus.Running || Current == null) return;
            Current.elapsed += Math.Max(0, deltaTime);
            try
            {
                for (int i = 0; i < transitionBudget; i++)
                {
                    Frame frame = Current;
                    StudioNode node = frame.node;
                    if (!frame.entered)
                    {
                        frame.entered = true;
                        Log.Add(frame.document.title + " › " + node.Name);
                        actions.Enter(node, frame.spawned);
                        Message = node.Name;
                        Changed?.Invoke();
                    }
                    if (node.type == "end")
                    {
                        Completed.Add(frame.document.id + "/" + node.id);
                        if (stack.Count == 1)
                        {
                            Status = node.parameters.success ? SessionStatus.Completed : SessionStatus.Failed;
                            Message = node.parameters.success ? "流程完成" : "流程失败";
                            Log.Add(Message); Changed?.Invoke(); return;
                        }
                        stack.RemoveAt(stack.Count - 1);
                        Move(node.parameters.success ? "success" : "failure");
                        continue;
                    }
                    if (node.type == "call_event")
                    {
                        FlowDocument child = events[FlowFileStore.FileName(node.parameters.eventFile)];
                        var childFrame = new Frame { document = child, node = child.Find(child.entry) };
                        if (frame.spawned.TryGetValue(NodeParameters.SignalObject, out var subject))
                            childFrame.spawned[NodeParameters.SignalObject] = subject;
                        stack.Add(childFrame);
                        Changed?.Invoke(); continue;
                    }
                    string port = node.type == "wait_signal" ? (frame.receivedSignal ? "next" : null) :
                        node.type == "start" ? "next" : actions.Poll(node, frame.elapsed, frame.spawned);
                    if (port == null) return;
                    Move(port);
                }
                // A bounded call deliberately supports single-stepping; unfinished work resumes next frame.
            }
            catch (Exception exception) { Fail(exception.Message); }
        }
        private void Move(string port)
        {
            Frame frame = Current;
            StudioEdge edge = frame.document.Output(frame.node.id, port);
            if (edge == null) throw new InvalidOperationException(frame.node.Name + "：未设置下一步。");
            Completed.Add(frame.document.id + "/" + frame.node.id);
            frame.node = frame.document.Find(edge.to) ?? throw new InvalidOperationException("目标节点不存在。");
            frame.elapsed = 0; frame.entered = false; frame.receivedSignal = false;
            Changed?.Invoke();
        }
        private void Fail(string message)
        { ExecutionError = message; Status = SessionStatus.Failed; Message = message; Log.Add("错误：" + message); Changed?.Invoke(); }
        public void Stop()
        { actions.Stop(); Status = SessionStatus.Stopped; Message = "流程已停止"; Changed?.Invoke(); }
    }

    /// <summary>No scene objects are touched. UI buttons supply otherwise external conditions.</summary>
    public sealed class SimulationActions : IFlowActions
    {
        public readonly Dictionary<string, bool> Flags = new Dictionary<string, bool>();
        public readonly List<string> Effects = new List<string>();
        private string outcome;
        public void Resolve(string port) => outcome = port;
        public void Enter(StudioNode node, Dictionary<string, GameObject> spawned)
        {
            outcome = null;
            NodeParameters p = node.parameters;
            switch (node.type)
            {
                case "set_flag":
                    Flags[NodeParameters.ConditionOutput(node.id)] = p.value;
                    if (!string.IsNullOrEmpty(p.flag)) Flags[p.flag] = p.value;
                    break;
                case "spawn": Effects.Add("生成：" + node.Name); break;
                case "transfer": Effects.Add("移动：" + node.Name); break;
                case "run_action": Effects.Add("执行：" + node.Name); break;
            }
        }
        public string Poll(StudioNode node, float elapsed, Dictionary<string, GameObject> spawned)
        {
            if (outcome != null) return outcome;
            switch (node.type)
            {
                case "wait_near": case "wait_region": case "wait_travel": case "wait_confirmation": case "run_action": case "choice": return null;
                case "delay": return elapsed >= node.parameters.seconds ? "next" : null;
                case "branch" when node.parameters.checkDistance: return null;
                case "branch":
                    if (!Flags.TryGetValue(node.parameters.flag, out bool value))
                        throw new InvalidOperationException("未找到条件的记录结果。请检查结果来源。");
                    return value ? "yes" : "no";
                default: return "next";
            }
        }
        public void Stop() { outcome = null; Flags.Clear(); Effects.Clear(); }
    }

    /// <summary>JSON snapshots keep Undo independent of UnityEditor and support a standalone player.</summary>
    public sealed class DocumentHistory
    {
        private readonly List<string> undo = new List<string>();
        private readonly Stack<string> redo = new Stack<string>();
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public void Record(string before, FlowDocument after)
        {
            if (before == after.ToJson()) return;
            undo.Add(before); if (undo.Count > 80) undo.RemoveAt(0); redo.Clear();
        }
        public FlowDocument Undo(FlowDocument current)
        {
            if (!CanUndo) return current;
            redo.Push(current.ToJson()); string json = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1);
            return FlowDocument.FromJson(json);
        }
        public FlowDocument Redo(FlowDocument current)
        {
            if (!CanRedo) return current;
            undo.Add(current.ToJson()); return FlowDocument.FromJson(redo.Pop());
        }
        public void Clear() { undo.Clear(); redo.Clear(); }
    }
}
