using System.Collections;
using UnityEngine;

namespace WhiteWhale.Combat
{
    /// <summary>沿用 Dicy 的写法：AI 调度战斗序列，序列按顺序组合基础动作。</summary>
    public sealed class EnemyShipAI : MonoBehaviour
    {
        public enum EnemyState { Idle, Preparing, Approaching, Turning, Attacking, Waiting, Stopped }
        public EnemyState CurrentState => currentState;
        public int SequenceCount { get; private set; }

        [Header("运行状态（仅供观察）")]
        [SerializeField] private EnemyState currentState;

        private EnemyActor actor;
        private EnemyShipActions actions;
        private Coroutine currentActionCoroutine;

        public void Initialize(EnemyActor owner, EnemyShipActions controller)
        {
            StopAI();
            actor = owner;
            actions = controller;
            SequenceCount = 0;
            actor.Battle.StateChanged += OnBattleStateChanged;
            actor.Health.Died += StopAI;
            SetState(EnemyState.Idle);
        }

        private void Update()
        {
            if (actor == null || !actor.CanAct || currentActionCoroutine != null || CurrentState != EnemyState.Idle) return;
            currentActionCoroutine = StartCoroutine(CombatLoop());
        }

        #region Combat Sequences

        private IEnumerator CombatLoop()
        {
            SetState(EnemyState.Preparing);
            yield return actions.Wait(actor.Battle.Definition.firstShotDelay);
            while (actor.CanAct)
            {
                SequenceCount++;
                yield return CombatSequence1();
                // 即使所有动作立即完成，也让下一轮在下一帧重新调度。
                yield return null;
            }
            StopAI();
        }

        private IEnumerator CombatSequence1() // 接近 → 侧舷对准 → 开炮 → 等待
        {
            SetState(EnemyState.Approaching);
            yield return actions.MoveToPlayer();
            if (!actor.CanAct || !actions.IsInRange) yield break;

            SetState(EnemyState.Turning);
            yield return actions.TurnBroadsideToPlayer();
            if (!actor.CanAct || !actions.IsInRange) yield break;

            SetState(EnemyState.Attacking);
            actions.FireBroadside();

            SetState(EnemyState.Waiting);
            yield return actions.Wait(actor.Battle.Definition.shotInterval, true);
        }

        #endregion

        #region Lifecycle

        public void StopAI()
        {
            if (currentActionCoroutine != null)
            {
                StopCoroutine(currentActionCoroutine);
                currentActionCoroutine = null;
            }
            if (actions != null) actions.StopAction();
            if (actor != null)
            {
                if (actor.Battle != null) actor.Battle.StateChanged -= OnBattleStateChanged;
                actor.Health.Died -= StopAI;
            }
            SetState(EnemyState.Stopped);
        }

        private void OnBattleStateChanged()
        {
            if (actor.Battle.State != BattleStatus.Running) StopAI();
        }

        private void SetState(EnemyState state) => currentState = state;
        private void OnDisable() => StopAI();

        #endregion
    }
}
