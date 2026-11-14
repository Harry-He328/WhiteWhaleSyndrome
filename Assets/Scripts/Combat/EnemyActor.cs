using UnityEngine;

namespace WhiteWhale.Combat
{
    /// <summary>敌人实体：提供战斗引用，统一推进物理；行为编排交给 EnemyShipAI。</summary>
    [RequireComponent(typeof(Health), typeof(ShipArtillery), typeof(Rigidbody))]
    [RequireComponent(typeof(EnemyShipActions), typeof(EnemyShipAI))]
    public sealed class EnemyActor : MonoBehaviour
    {
        private BattleController battle;
        private OceanSurface ocean;
        private EnemyShipActions actions;
        public Health Health => GetComponent<Health>();
        public ShipArtillery Artillery => GetComponent<ShipArtillery>();
        public BattleController Battle => battle;
        public bool CanAct => isActiveAndEnabled && battle != null && battle.State == BattleStatus.Running &&
            !Health.IsDead && battle.Player != null && !battle.PlayerHealth.IsDead;

        public void Begin(BattleController owner, OceanSurface surface, BattleDefinition definition)
        {
            battle = owner;
            ocean = surface;
            actions = GetComponent<EnemyShipActions>();
            actions.Initialize(this);
            Health.Died += OnDied;
            GetComponent<EnemyShipAI>().Initialize(this, actions);
        }

        private void FixedUpdate()
        {
            if (CanAct && actions != null) actions.StepPhysics(ocean);
        }

        private void OnDied() { if (battle != null) battle.OnEnemyDied(this); }
        private void OnDisable() => Health.Died -= OnDied;
    }
}
