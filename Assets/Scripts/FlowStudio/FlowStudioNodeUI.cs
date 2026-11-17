using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using WhiteWhale.Flow;

namespace WhiteWhale.FlowStudio
{
    public sealed partial class FlowStudioUI
    {
        private void BuildNodeFields(VisualElement fields, StudioNode node)
        {
            var p = node.parameters;
            switch (node.type)
            {
                case "call_event": BuildEventPicker(fields, p); break;
                case "on_signal":
                    Field(fields, "玩家操作", p.triggerDescription ?? "", value => Edit(() => p.triggerDescription = value), true);
                    BuildEventPicker(fields, p);
                    break;
                case "wait_near":
                    BuildDistanceFields(fields, node);
                    break;
                case "wait_travel":
                    PositiveNumber(fields, "累计移动距离（米）", p.travelDistance, value => p.travelDistance = value);
                    Bool(fields, "仅计算水平距离", p.planarDistance, value => Edit(() => p.planarDistance = value));
                    break;
                case "wait_region": BuildRegionFields(fields, node); break;
                case "delay": PositiveNumber(fields, "时长（秒）", p.seconds, value => p.seconds = value, true); break;
                case "wait_signal": BuildSignalField(fields, node); break;
                case "wait_confirmation":
                    Field(fields, "提示内容", p.note, value => Edit(() => p.note = value), true);
                    break;
                case "choice":
                    Field(fields, "提示内容", p.note, value => Edit(() => p.note = value), true);
                    Field(fields, "选项一", p.firstChoice, value => Edit(() => p.firstChoice = value));
                    Field(fields, "选项二", p.secondChoice, value => Edit(() => p.secondChoice = value));
                    break;
                case "spawn": BuildSpawnFields(fields, node); break;
                case "transfer":
                    ObjectField(fields, "物件", node, p.item, value => p.item = value, other: p.destination);
                    LocationField(fields, "目标位置", node, p.destination, reference =>
                    {
                        p.destination = reference;
                        ResetLegacyDestination(p);
                    }, other: p.item);
                    BuildLocationAdjustment(fields, node, p.destination);
                    break;
                case "run_action": BuildActionFields(fields, node); break;
                case "set_flag":
                    ChoiceField(fields, "结果", p.value ? "yes" : "no",
                        new[] { ("yes", "是"), ("no", "否") }, value => p.value = value == "yes");
                    break;
                case "branch": BuildConditionFields(fields, node); break;
                case "end":
                    ChoiceField(fields, VisibleDocument.kind == "event" ? "事件结果" : "流程结果", p.success ? "success" : "failure",
                        new[] { ("success", "成功"), ("failure", "失败") }, value => p.success = value == "success");
                    break;
            }
        }

        private void BuildSignalField(VisualElement fields, StudioNode node)
        {
            var p = node.parameters;
            ChoiceField(fields, "玩家操作", p.signal,
                new[] { ("", "选择玩家操作"), (FlowSignals.ItemPickedUp, FlowSignals.Label(FlowSignals.ItemPickedUp)) },
                value => p.signal = value);
            if (p.signal == FlowSignals.ItemPickedUp)
            {
                ObjectField(fields, "拾取对象（可选）", node, p.item, value => p.item = value,
                    accepts: AcceptsPickup, placeholder: "任意物件");
                if (!string.IsNullOrEmpty(p.item)) fields.Add(Button("不限物件", () =>
                {
                    Edit(() => p.item = "");
                    BuildInspector();
                }, "quiet"));
                fields.Add(Text("后续节点可引用「玩家拾取的物件」。", "muted wrap"));
            }
        }

        private void BuildDistanceFields(VisualElement fields, StudioNode node)
        {
            var p = node.parameters;
            if (node.type == "wait_near")
                GeneratedTargetField(fields, node);
            else
                ObjectField(fields, "参照对象", node, p.target, value => p.target = value,
                    other: NodeParameters.PlayerShip, placeholder: "选择参照对象");
            PositiveNumber(fields, "距离上限（米）", p.radius, value => p.radius = value);
            Bool(fields, "仅计算水平距离", p.planarDistance, value => Edit(() => p.planarDistance = value));
        }

        private void BuildRegionFields(VisualElement fields, StudioNode node)
        {
            var p = node.parameters;
            if (!p.placeRadius)
                fields.Add(Text("旧范围无法转换，请重新设置范围中心和半径。", "muted wrap"));
            if (NodeParameters.IsSpawnOutput(p.target))
            {
                var source = VisibleDocument.Find(NodeParameters.SpawnNodeId(p.target));
                string name = source?.SpawnsObject == true ? AssetLabel(VisibleDocument.PrefabGuid(source)) ?? source.Name : "生成节点不可用";
                fields.Add(Text("范围中心", "picker-label"));
                fields.Add(Text(name, "wrap"));
                fields.Add(Button("断开范围中心", () =>
                {
                    Edit(() => ClearRegionCenter(node));
                    wires.MarkDirtyRepaint();
                    BuildInspector();
                }, "quiet"));
            }
            else
            {
                if (VisibleDocument.kind == "event")
                    fields.Add(Text("将「生成结果」拖到「范围中心」，即可使用生成物件的位置。", "muted wrap"));
                LocationField(fields, "中心位置", node, p.target, reference =>
                    {
                        p.target = reference;
                        p.placeRadius = true;
                        ResetLegacyDestination(p);
                    },
                    sceneOnly: true, other: NodeParameters.PlayerShip);
            }
            PositiveNumber(fields, "半径（米）", p.radius, value => { p.radius = value; p.placeRadius = true; });
            BuildLocationAdjustment(fields, node, p.target);
        }

        private void BuildConditionFields(VisualElement fields, StudioNode node)
        {
            var p = node.parameters;
            var recorded = VisibleDocument.nodes.Where(candidate => candidate.type == "set_flag" &&
                VisibleDocument.CanReferenceOutput(candidate.id, node.id))
                .Select(candidate => (NodeParameters.ConditionOutput(candidate.id), candidate.Name + " · " + NodeReferenceDetail(candidate))).ToArray();
            if (recorded.Length != 0 || !p.checkDistance)
                ChoiceField(fields, "条件类型", p.checkDistance ? "distance" : "recorded",
                    new[] { ("distance", "玩家船距离"), ("recorded", "已记录结果") },
                    value => p.checkDistance = value == "distance");
            if (p.checkDistance)
            {
                BuildDistanceFields(fields, node);
                fields.Add(Text("玩家船与参照对象的距离在上限以内时，进入「满足」分支。", "muted wrap"));
                return;
            }
            if (recorded.Length == 0)
            {
                fields.Add(Text("暂无结果可选。请在此节点之前添加「记录结果」。", "muted wrap"));
                return;
            }
            ChoiceField(fields, "结果来源", p.flag, recorded.Prepend(("", "选择记录节点")), value => p.flag = value);
            fields.Add(Text("结果为「是」时，进入「满足」分支。", "muted wrap"));
        }

        private void BuildSpawnFields(VisualElement fields, StudioNode node)
        {
            var p = node.parameters;
            string guid = VisibleDocument.PrefabGuid(node);
            ResourceField(fields, "物件", guid, false, value =>
            {
                if (value == guid && string.IsNullOrEmpty(p.sourcePoint)) return;
                p.prefabInput = "";
                p.prefabGuid = value;
                p.sourcePoint = "";
                p.spawnOriginOffset = Vector3.zero;
            });
            var shared = VisibleDocument.FindPrefabInput(p.prefabInput);
            if (shared != null) fields.Add(Text("共享物件「" + shared.label + "」。更换物件仅影响当前节点。", "muted wrap"));
            LocationField(fields, "放置位置", node, p.destination, reference =>
            {
                p.destination = reference;
                ResetLegacyDestination(p);
            });

            if (p.placement == "projected" || p.parentToTargetRoot || p.spawnScale != 1f)
                BuildExistingPlacement(fields, node);
            if (p.placement != "projected") BuildLocationAdjustment(fields, node, p.destination);
        }

        private void BuildLocationAdjustment(VisualElement fields, StudioNode node, string reference)
        {
            if (string.IsNullOrEmpty(reference)) return;
            var p = node.parameters;
            bool spawn = node.type == "spawn";
            bool range = node.type == "wait_region";
            bool adjusting = spawn ? p.placement == "relative" : p.customLocation;
            if (adjusting)
            {
                if (!range)
                    Field(fields, "位置名称（可选）", p.placeLabel, value =>
                    {
                        Edit(() => p.placeLabel = value.Trim());
                        BuildInspector();
                        inspector.Q<Foldout>("position-preview")?.SetValueWithoutNotify(true);
                    });
                BuildOffset(fields, p);
            }
            var adjust = Button(adjusting ? "重置位置" : "调整位置", () =>
            {
                Edit(() =>
                {
                    if (spawn) p.placement = adjusting ? "anchor" : "relative";
                    else p.customLocation = !adjusting;
                    p.placementOffset = Vector3.zero;
                    if (adjusting)
                    {
                        p.placeLabel = "";
                        ResetLegacyDestination(p);
                    }
                });
                BuildInspector();
                if (!adjusting && !range) inspector.Q<Foldout>("position-preview")?.SetValueWithoutNotify(true);
            }, "quiet");
            if (adjusting) adjust.tooltip = range ? "清除偏移，恢复范围中心。" : "清除偏移和自定义名称，恢复所选位置。";
            fields.Add(adjust);
        }

        private static void ResetLegacyDestination(NodeParameters parameters)
        {
            parameters.destinationPoint = "";
            parameters.destinationAngles = Vector3.zero;
            parameters.projectedAnchorOffset = Vector3.zero;
        }

        private void BuildExistingPlacement(VisualElement fields, StudioNode node)
        {
            var p = node.parameters;
            var options = new Foldout { text = "摆放设置" };
            fields.Add(options);
            if (p.placement == "projected")
            {
                options.Add(Text("水平位置随朝向参照移动，高度由「放置位置」决定。", "muted wrap"));
                ObjectField(options, "朝向参照", node, p.orientation, value => p.orientation = value, placeholder: "选择参照对象");
                BuildOffset(options, p);
                options.Add(Button("改为固定位置", () =>
                {
                    Edit(() =>
                    {
                        p.placement = "anchor";
                        p.orientation = "";
                        p.placementOffset = Vector3.zero;
                        ResetLegacyDestination(p);
                    });
                    BuildInspector();
                }));
            }
            if (p.spawnScale != 1f)
                PositiveNumber(options, "缩放倍数", p.spawnScale, value => p.spawnScale = value);
            if (p.parentToTargetRoot) options.Add(Text("物件归属沿用当前场景设置。", "muted wrap"));
        }

        private void BuildOffset(VisualElement fields, NodeParameters p)
        {
            var adjust = new Label();
            adjust.AddToClassList("muted");
            adjust.AddToClassList("wrap");
            adjust.text = p.placement == "projected" ?
                "偏移以朝向参照为准。正值表示向右、向上或向前。" :
                "偏移以所选位置的朝向为准。正值表示向右、向上或向前。";
            fields.Add(adjust);
            FloatField OffsetField(string name, float value, Func<float, Vector3> position)
            {
                var field = new FloatField(name) { value = value, isDelayed = true };
                field.AddToClassList("parameter");
                field.RegisterValueChangedCallback(evt =>
                {
                    if (!float.IsFinite(evt.newValue))
                    {
                        field.SetValueWithoutNotify(evt.previousValue);
                        return;
                    }
                    Edit(() => p.placementOffset = position(evt.newValue));
                    RefreshPlacementPreview();
                });
                fields.Add(field);
                return field;
            }
            var right = OffsetField("左右偏移（米）", p.placementOffset.x, value =>
                new Vector3(value, p.placementOffset.y, p.placementOffset.z));
            OffsetField("高度偏移（米）", p.placementOffset.y, value =>
                new Vector3(p.placementOffset.x, value, p.placementOffset.z));
            var forward = OffsetField("前后偏移（米）", p.placementOffset.z, value =>
                new Vector3(p.placementOffset.x, p.placementOffset.y, value));
            if (p.placement != "projected" && refreshPlacementPreview != null) placementPicked = offset =>
            {
                Edit(() => p.placementOffset = new Vector3(offset.x, p.placementOffset.y, offset.y));
                right.SetValueWithoutNotify(offset.x);
                forward.SetValueWithoutNotify(offset.y);
                RefreshPlacementPreview();
            };
        }

        private void BuildActionFields(VisualElement fields, StudioNode node)
        {
            var p = node.parameters;
            ResourceField(fields, "玩法", p.actionGuid, true, value =>
            {
                if (p.actionGuid == value) return;
                p.actionGuid = value;
                p.item = "";
                p.destination = "";
            });
            if (Files.ResolveAsset(p.actionGuid) is not FlowAction action) return;
            if (!string.IsNullOrWhiteSpace(action.Description)) fields.Add(Text(action.Description, "muted wrap"));
            if (!string.IsNullOrEmpty(action.SubjectLabel))
                ObjectField(fields, action.SubjectLabel, node, p.item, value => p.item = value,
                    accepts: (key, item) => AcceptsAction(key, item, p.actionGuid, action, false));
            if (!string.IsNullOrEmpty(action.TargetLabel))
            {
                ObjectField(fields, action.TargetLabel, node, p.destination, value => p.destination = value,
                    accepts: (key, item) => AcceptsAction(key, item, p.actionGuid, action, true), other: p.item,
                    placeholder: "选择" + action.TargetLabel, empty: "暂无符合该玩法要求的目标。");
            }
        }

        private void PositiveNumber(VisualElement fields, string label, float value, Action<float> assign, bool allowZero = false)
        {
            var field = new FloatField(label) { value = value, isDelayed = true };
            field.AddToClassList("parameter");
            fields.Add(field);
            var error = Text("", "inline-error wrap");
            error.style.display = DisplayStyle.None;
            fields.Add(error);
            field.RegisterValueChangedCallback(evt =>
            {
                float next = evt.newValue;
                if (!float.IsFinite(next) || (allowZero ? next < 0 : next <= 0))
                {
                    field.SetValueWithoutNotify(evt.previousValue);
                    error.text = allowZero ? "请输入大于或等于 0 的数值。" : "请输入大于 0 的数值。";
                    error.style.display = DisplayStyle.Flex;
                    return;
                }
                error.style.display = DisplayStyle.None;
                Edit(() => assign(next));
            });
        }

        private void BuildPrefabInputs()
        {
            var list = new Foldout { text = "共享物件", value = false };
            inspector.Add(list);
            foreach (var input in Document.prefabInputs)
            {
                Field(list, "名称", input.label, value => Edit(() => input.label = value));
                ResourceField(list, "物件", input.guid, false, value => input.guid = value);
                Bool(list, "允许在事件属性中编辑", input.published, value => Edit(() => input.published = value));
                if (!Document.nodes.Any(node => node.parameters.prefabInput == input.id))
                    list.Add(Button("移除「" + input.label + "」", () => { Edit(() => Document.prefabInputs.Remove(input)); BuildInspector(); }));
            }
            list.Add(Button("添加共享物件", () =>
            {
                Edit(() => Document.prefabInputs.Add(new FlowPrefabInput()));
                BuildInspector();
            }));
        }
    }
}
