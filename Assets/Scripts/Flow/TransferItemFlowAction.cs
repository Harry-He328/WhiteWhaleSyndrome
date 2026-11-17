using System;
using UnityEngine;
using WhiteWhale.FlowStudio;

namespace WhiteWhale.Flow
{
    [CreateAssetMenu(menuName = "White Whale/Flow Actions/Transfer Item")]
    public sealed class TransferItemFlowAction : FlowAction
    {
        public override string DisplayName => "转移至房间";
        public override string Description => "将物件移入房间，并使用房间内的尺寸和交互设置。";
        public override string SubjectLabel => "物件";
        public override string TargetLabel => "目标位置";
        public override bool AcceptsSubject(GameObject subject) => subject != null && subject.GetComponent<FlowTransferable>() != null;

        public override FlowActionRun Begin(FlowActionContext context, GameObject subject, GameObject target)
        {
            if (target == subject || target.transform.IsChildOf(subject.transform))
                throw new InvalidOperationException("请选择物件自身以外的目标位置。");
            var item = subject.GetComponent<FlowTransferable>();
            var restore = item.CaptureRestore();
            item.MoveToRoom(target.transform);
            return new Run(restore);
        }

        private sealed class Run : FlowActionRun
        {
            private readonly Action restore;
            public Run(Action restore) => this.restore = restore;
            public override bool? Poll() => true;
            public override void Stop() => restore();
        }
    }
}
