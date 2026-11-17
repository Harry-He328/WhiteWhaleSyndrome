using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using WhiteWhale.Flow;

namespace WhiteWhale.FlowStudio
{
    internal sealed class SceneFlowActions : IFlowActions
    {
        private readonly FlowActionContext context;
        private readonly Func<string, UnityEngine.Object> resolveAsset;
        private readonly Dictionary<string, GameObject> bindings;
        private readonly List<GameObject> generated = new List<GameObject>();
        private readonly Dictionary<GameObject, Action> restore = new Dictionary<GameObject, Action>();
        private readonly Dictionary<string, bool> flags = new Dictionary<string, bool>();
        private readonly List<FlowActionRun> actions = new List<FlowActionRun>();
        private FlowActionRun currentAction;
        private Vector3 previousPosition;
        private float traveled;
        private bool confirmed;
        private string choice;

        public SceneFlowActions(FlowActionContext context, Func<string, UnityEngine.Object> resolveAsset,
            Dictionary<string, GameObject> bindings)
        {
            this.context = context;
            this.resolveAsset = resolveAsset;
            this.bindings = bindings;
        }

        public void ValidateBindings(FlowDocument document)
        {
            foreach (var node in document.nodes)
                foreach (string reference in node.parameters.ObjectReferences(node.type))
                    if (!string.IsNullOrEmpty(reference) && reference != NodeParameters.SignalObject && !NodeParameters.IsSpawnOutput(reference))
                        Require(bindings.TryGetValue(reference, out var target) && target != null, node,
                            reference == NodeParameters.PlayerShip ? "当前场景未配置玩家船。" : "场景对象未绑定：" + reference);
        }

        public void Confirm() => confirmed = true;
        public void Choose(string option) => choice = option;

        public bool MatchesObject(string reference, GameObject item, Dictionary<string, GameObject> spawned) =>
            item != null && ObjectByReference(reference, spawned) == item;

        private GameObject ObjectByReference(string reference, Dictionary<string, GameObject> spawned) =>
            NodeParameters.IsSpawnOutput(reference) ? spawned.GetValueOrDefault(NodeParameters.SpawnNodeId(reference)) :
            reference == NodeParameters.SignalObject ? spawned.GetValueOrDefault(reference) : bindings.GetValueOrDefault(reference ?? "");

        private Transform Resolve(StudioNode node, string reference, Dictionary<string, GameObject> spawned)
        {
            GameObject result = ObjectByReference(reference, spawned);
            Require(result != null, node, reference == NodeParameters.PlayerShip ?
                "当前场景中找不到玩家船。" : "找不到对象：" + reference);
            return result.transform;
        }

        private static Transform Point(StudioNode node, Transform owner, string id)
        {
            Transform point = FlowAnchor.Find(owner, id);
            Require(point != null, node, "所选定位点不存在或编号重复。");
            return point;
        }

        private void Spawn(StudioNode node, Dictionary<string, GameObject> spawned)
        {
            var p = node.parameters;
            var prefab = resolveAsset(p.prefabGuid) as GameObject;
            Require(prefab != null, node, "预制体不可用。");
            Transform anchor = Point(node, Resolve(node, p.destination, spawned), p.destinationPoint);
            Vector3 position = anchor.position;
            Quaternion rotation = anchor.rotation * Quaternion.Euler(p.destinationAngles);
            if (p.placement == "relative") position += anchor.rotation * p.placementOffset;
            if (p.placement == "projected")
            {
                Transform reference = Resolve(node, p.orientation, spawned);
                Vector3 forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up).normalized;
                Require(forward.sqrMagnitude > 0, node, "朝向参考没有有效的水平朝向。");
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                float height = (anchor.position + anchor.rotation * p.projectedAnchorOffset).y;
                position = new Vector3(reference.position.x, height, reference.position.z) +
                    right * p.placementOffset.x + Vector3.up * p.placementOffset.y + forward * p.placementOffset.z;
                rotation = prefab.transform.rotation;
            }
            var instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
            generated.Add(instance);
            SceneManager.MoveGameObjectToScene(instance, anchor.gameObject.scene);
            instance.transform.localScale *= p.spawnScale;
            if (string.IsNullOrEmpty(p.sourcePoint))
                instance.transform.position -= rotation * p.spawnOriginOffset * p.spawnScale;
            else
                instance.transform.position += position - Point(node, instance.transform, p.sourcePoint).position;
            if (p.parentToTargetRoot) instance.transform.SetParent(anchor.root, true);
            instance.SetActive(true);
            spawned.Add(node.id, instance);
            FlowSceneChanges.Notify(instance.scene);
        }

        private void Move(StudioNode node, Transform item, Transform target)
        {
            Require(item != target && !target.IsChildOf(item), node, "不能移动到自身或子对象。");
            Scene previous = item.gameObject.scene;
            if (!generated.Contains(item.gameObject) && !restore.ContainsKey(item.gameObject))
            {
                Transform parent = item.parent;
                Vector3 position = item.localPosition;
                Quaternion rotation = item.localRotation;
                Vector3 scale = item.localScale;
                restore.Add(item.gameObject, () =>
                {
                    if (item == null || !previous.isLoaded) return;
                    Scene current = item.gameObject.scene;
                    item.SetParent(null, true);
                    if (current != previous) SceneManager.MoveGameObjectToScene(item.gameObject, previous);
                    item.SetParent(parent, false);
                    item.SetLocalPositionAndRotation(position, rotation);
                    item.localScale = scale;
                    FlowSceneChanges.Notify(current);
                    if (current != previous) FlowSceneChanges.Notify(previous);
                });
            }
            Scene destination = target.gameObject.scene;
            if (destination != previous)
            {
                item.SetParent(null, true);
                SceneManager.MoveGameObjectToScene(item.gameObject, destination);
            }
            var p = node.parameters;
            Vector3 positionAtTarget = target.position + (p.customLocation ? target.rotation * p.placementOffset : Vector3.zero);
            item.SetPositionAndRotation(positionAtTarget, target.rotation * Quaternion.Euler(p.destinationAngles));
            FlowSceneChanges.Notify(previous);
            if (destination != previous) FlowSceneChanges.Notify(destination);
        }

        public void Enter(StudioNode node, Dictionary<string, GameObject> spawned)
        {
            var p = node.parameters;
            switch (node.type)
            {
                case "wait_travel":
                    previousPosition = Resolve(node, NodeParameters.PlayerShip, spawned).position;
                    traveled = 0;
                    break;
                case "wait_confirmation": confirmed = false; break;
                case "choice": choice = null; break;
                case "set_flag":
                    flags[NodeParameters.ConditionOutput(node.id)] = p.value;
                    if (!string.IsNullOrEmpty(p.flag)) flags[p.flag] = p.value;
                    break;
                case "spawn": Spawn(node, spawned); break;
                case "transfer":
                    Move(node, Resolve(node, p.item, spawned), Point(node, Resolve(node, p.destination, spawned), p.destinationPoint));
                    break;
                case "run_action":
                    var action = resolveAsset(p.actionGuid) as FlowAction;
                    Require(action != null, node, "动作资源不可用。");
                    var subject = string.IsNullOrEmpty(action.SubjectLabel) ? null : Resolve(node, p.item, spawned).gameObject;
                    var target = string.IsNullOrEmpty(action.TargetLabel) ? null : Resolve(node, p.destination, spawned).gameObject;
                    Require(subject == null || action.AcceptsSubject(subject), node, "对象不符合动作要求。");
                    Require(target == null || action.AcceptsTarget(target), node, "目标不符合动作要求。");
                    currentAction = action.Begin(context, subject, target);
                    Require(currentAction != null, node, "动作没有返回执行结果。");
                    actions.Add(currentAction);
                    break;
            }
        }

        public string Poll(StudioNode node, float elapsed, Dictionary<string, GameObject> spawned)
        {
            var p = node.parameters;
            switch (node.type)
            {
                case "wait_travel":
                    Vector3 position = Resolve(node, NodeParameters.PlayerShip, spawned).position;
                    Vector3 step = position - previousPosition;
                    if (p.planarDistance) step.y = 0;
                    traveled += step.magnitude;
                    previousPosition = position;
                    return traveled >= p.travelDistance ? "next" : null;
                case "wait_near":
                    return InRange(Resolve(node, NodeParameters.PlayerShip, spawned), Resolve(node, p.target, spawned).position, p) ? "next" : null;
                case "wait_region":
                    var center = Point(node, Resolve(node, p.target, spawned), p.destinationPoint);
                    var ship = Resolve(node, NodeParameters.PlayerShip, spawned);
                    if (p.placeRadius)
                    {
                        var regionCenter = center.position + (p.customLocation ? center.rotation * p.placementOffset : Vector3.zero);
                        return InRange(ship, regionCenter, p) ? "next" : null;
                    }
                    var region = center.GetComponent<FlowRegion>();
                    Require(region != null, node, "所选对象上没有对应的区域。");
                    return region.Contains(ship.position) ? "next" : null;
                case "wait_confirmation": return confirmed ? "next" : null;
                case "choice": return choice;
                case "delay": return elapsed >= p.seconds ? "next" : null;
                case "branch" when p.checkDistance:
                    return InRange(Resolve(node, NodeParameters.PlayerShip, spawned), Resolve(node, p.target, spawned).position, p) ? "yes" : "no";
                case "branch":
                    Require(flags.TryGetValue(p.flag, out bool value), node, "条件尚未设置。");
                    return value ? "yes" : "no";
                case "run_action":
                    bool? result = currentAction.Poll();
                    return result.HasValue ? (result.Value ? "success" : "failure") : null;
                default: return "next";
            }
        }

        private static bool InRange(Transform actor, Vector3 center, NodeParameters parameters)
        {
            Vector3 distance = actor.position - center;
            if (parameters.planarDistance) distance.y = 0;
            return distance.sqrMagnitude <= parameters.radius * parameters.radius;
        }

        public void Stop()
        {
            for (int i = actions.Count - 1; i >= 0; i--) actions[i].Stop();
            actions.Clear();
            currentAction = null;
            foreach (var apply in restore.Values) apply();
            restore.Clear();
            foreach (var instance in generated)
            {
                if (instance == null) continue;
                var scene = instance.scene;
                instance.SetActive(false);
                FlowSceneChanges.Removing(instance);
                UnityEngine.Object.Destroy(instance);
                FlowSceneChanges.Notify(scene);
            }
            generated.Clear();
        }

        private static void Require(bool condition, StudioNode node, string message)
        {
            if (!condition) throw new InvalidOperationException(node.Name + "：" + message);
        }
    }
}
