using UnityEngine;
using WhiteWhale.FlowStudio;

namespace WhiteWhale.Combat
{
    [CreateAssetMenu(menuName = "White Whale/Flow Actions/Battle")]
    public sealed class BattleFlowAction : FlowAction
    {
        [SerializeField] private BattleDefinition definition;
        [SerializeField] private bool winWhenSpawningFinishedAndAllEnemiesDefeated = true;

        public override FlowActionRun Begin(FlowActionContext context, GameObject subject, GameObject target)
        {
            var controller = context.Host.GetComponent<BattleController>();
            if (controller == null || definition == null)
                throw new System.InvalidOperationException("战斗动作缺少战斗系统或配置。");
            controller.BeginConfiguredBattle(definition);
            return new Run(controller, winWhenSpawningFinishedAndAllEnemiesDefeated);
        }

        private sealed class Run : FlowActionRun
        {
            private readonly BattleController controller;
            private readonly bool clearToWin;
            private bool? result;

            public Run(BattleController controller, bool clearToWin)
            {
                this.controller = controller;
                this.clearToWin = clearToWin;
            }

            public override bool? Poll()
            {
                if (result.HasValue) return result;
                if (controller == null || controller.State == BattleStatus.Idle) return result = false;
                if (controller.State == BattleStatus.Running && clearToWin &&
                    controller.Flow.IsFinished && controller.Enemies.Count == 0)
                    controller.CompleteEventBattle(true);
                if (controller.State == BattleStatus.Won) return result = true;
                if (controller.State == BattleStatus.Lost) return result = false;
                return null;
            }

            public override void Stop()
            {
                if (controller != null && controller.Definition != null && controller.State != BattleStatus.Idle)
                    controller.StopBattle();
            }
        }
    }
}
