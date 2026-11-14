using System;
using UnityEngine;
using WhiteWhale.FlowStudio;

namespace WhiteWhale.Flow
{
    [CreateAssetMenu(menuName = "White Whale/Flow Actions/Transfer Item")]
    public sealed class TransferItemFlowAction : FlowAction
    {
        public override string SubjectLabel => "可转移物件";
        public override string TargetLabel => "目的地";
        public override bool AcceptsSubject(GameObject subject) => subject != null && subject.GetComponent<FlowTransferable>() != null;

        public override FlowActionRun Begin(FlowActionContext context, GameObject subject, GameObject target)
        {
            if (target == subject || target.transform.IsChildOf(subject.transform))
                throw new InvalidOperationException("物件不能转移到自身或子对象。");
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
