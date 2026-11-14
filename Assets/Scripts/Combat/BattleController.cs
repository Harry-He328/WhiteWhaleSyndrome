using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WhiteWhale.Combat
{
    public enum BattleStatus { Idle, Running, Won, Lost }

    /// <summary>管理一场战斗的玩家、敌船集合和炮弹，生成顺序交给 BattleFlow。</summary>
    [RequireComponent(typeof(BattleFlow))]
    public sealed class BattleController : MonoBehaviour
    {
        [SerializeField] private BattleDefinition definition;
        [SerializeField] private ShipMovement player;
        [SerializeField] private Health playerHealth;
        [SerializeField] private OceanSurface ocean;
        [SerializeField] private bool startAutomatically = true;
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private bool initialMovementEnabled;
        private bool capturedPose;
        private bool[] cannonStates;
        private Cannon[] playerCannons;
        public BattleDefinition Definition => definition;
        public ShipMovement Player => player;
        public Health PlayerHealth => playerHealth;
        private readonly List<EnemyActor> enemies = new List<EnemyActor>();
        private readonly List<EnemyActor> ownedEnemies = new List<EnemyActor>();
        public IReadOnlyList<EnemyActor> Enemies => enemies;
        public EnemyActor Enemy => enemies.Count > 0 ? enemies[0] : null;
        public int DefeatedCount { get; private set; }
        public BattleFlow Flow => GetComponent<BattleFlow>();
        public BattleStatus State { get; private set; }
        public bool EnemyAutoFire { get; set; }
        public event Action StateChanged;
        public event Action<bool> Finished;

        private void Start() { if (startAutomatically) BeginBattle(); }

        public void Initialize(BattleDefinition config, ShipMovement ship, OceanSurface surface, bool beginImmediately = true)
        {
            startAutomatically = false;
            definition = config; player = ship; ocean = surface;
            playerHealth = ship.GetComponent<Health>();
            if (beginImmediately) BeginBattle();
        }

        public void BeginBattle() => BeginBattle(false);

        public void BeginConfiguredBattle(BattleDefinition config)
        {
            if (State == BattleStatus.Running) throw new InvalidOperationException("战斗已经开始。");
            definition = config;
            BeginBattle(true);
        }

        private void BeginBattle(bool preservePosition)
        {
            if (definition == null || definition.enemyPrefab == null || player == null || playerHealth == null || ocean == null)
                throw new InvalidOperationException("战斗缺少配置、敌船、玩家船、生命值或海洋绑定。");
            if (State == BattleStatus.Running) throw new InvalidOperationException("战斗已经开始。");
            if (!capturedPose)
            {
                initialPosition = player.transform.position;
                initialRotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
                initialMovementEnabled = player.enabled;
                playerCannons = player.GetComponentsInChildren<Cannon>(true);
                cannonStates = new bool[playerCannons.Length];
                for (int i = 0; i < playerCannons.Length; i++) cannonStates[i] = playerCannons[i].enabled;
                capturedPose = true;
            }
            StopBattle();
            if (!preservePosition) RestorePlayerPose();
            playerHealth.ResetHealth(definition.playerHealth);
            playerHealth.Died += OnPlayerDied;
            EnemyAutoFire = definition.enemyAutoFire;
            DefeatedCount = 0;
            State = BattleStatus.Running;
            playerHealth.AcceptsDamage = true;
            try { Flow.Begin(this); }
            catch { StopBattle(); throw; }
            StateChanged?.Invoke();
        }

        public void CompleteEventBattle(bool won) => Complete(won);

        public EnemyActor SpawnEnemy(BattleSpawnEvent spawnEvent)
        {
            if (State != BattleStatus.Running) throw new InvalidOperationException("战斗未运行，不能生成敌人。");
            EnemyActor prefab = spawnEvent.enemyPrefab != null ? spawnEvent.enemyPrefab : definition.enemyPrefab;
            Quaternion playerYaw = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
            Vector3 offset = playerYaw * spawnEvent.spawnOffset;
            Vector3 position = default;
            Quaternion rotation = Quaternion.identity;
            bool found = false;
            var box = prefab.GetComponent<BoxCollider>();
            for (int attempt = 0; attempt < 48; attempt++)
            {
                Vector3 candidateOffset = Quaternion.Euler(0f, (attempt % 8) * 45f, 0f) * offset;
                if (candidateOffset.sqrMagnitude < 1f) candidateOffset = Vector3.right * 12f;
                candidateOffset += candidateOffset.normalized * ((attempt / 8) * 6f);
                position = player.transform.position + candidateOffset;
                position.y = ocean.GetHeight(position) - 0.15f;
                Vector3 towardPlayer = Vector3.ProjectOnPlane(player.transform.position - position, Vector3.up);
                rotation = Quaternion.LookRotation(towardPlayer.normalized, Vector3.up);
                Vector3 half = box != null ? Vector3.Scale(box.size * 0.5f, prefab.transform.localScale) : new Vector3(1f, 2.5f, 3.7f);
                Vector3 center = position + (box != null ? rotation * Vector3.Scale(box.center, prefab.transform.localScale) : Vector3.up * 2.5f);
                if (Physics.CheckBox(center, half + Vector3.one * 0.3f, rotation, ~0, QueryTriggerInteraction.Ignore)) continue;
                found = true; break;
            }
            if (!found) throw new InvalidOperationException("未找到无碰撞的敌船生成位置。");
            EnemyActor enemy = Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(enemy.gameObject, player.gameObject.scene);
            enemy.transform.SetParent(player.transform.parent, true);
            enemy.transform.SetPositionAndRotation(position, rotation);
            var body = enemy.GetComponent<Rigidbody>(); body.position = position; body.rotation = rotation;
            enemy.Health.ResetHealth(definition.enemyHealth);
            ownedEnemies.Add(enemy); enemies.Add(enemy);
            enemy.Begin(this, ocean, definition);
            enemy.Health.AcceptsDamage = true;
            OceanVisualEvents.Notify(player.gameObject.scene);
            return enemy;
        }

        public void RestartBattle() { StopBattle(); BeginBattle(); }

        private void RestorePlayerPose()
        {
            player.GetComponent<MovementSpeedModifier>()?.Clear();
            player.enabled = false;
            var body = player.GetComponent<Rigidbody>();
            body.position = initialPosition; body.rotation = initialRotation;
            player.transform.SetPositionAndRotation(initialPosition, initialRotation);
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            // 帆量和舵量由房间装置控制，重置战斗不能覆盖它们的实际状态。
            player.enabled = initialMovementEnabled;
            for (int i = 0; i < playerCannons.Length; i++)
            {
                playerCannons[i].enabled = cannonStates[i];
                playerCannons[i].ResetCooldown();
            }
            Physics.SyncTransforms();
        }

        private void OnPlayerDied() => Complete(false);
        public void OnEnemyDied(EnemyActor enemy)
        {
            if (!enemies.Remove(enemy)) return;
            DefeatedCount++;
            ClearEnemyShots(enemy);
            enemy.gameObject.SetActive(false);
            OceanVisualEvents.Notify(player.gameObject.scene);
            // 单个敌人死亡、清空敌人和时间结束，都不在本阶段自动判胜。
        }

        private void Complete(bool won)
        {
            if (State != BattleStatus.Running) return;
            State = won ? BattleStatus.Won : BattleStatus.Lost;
            Flow.StopFlow();
            playerHealth.AcceptsDamage = false;
            foreach (EnemyActor enemy in enemies)
            {
                if (enemy == null) continue;
                enemy.Health.AcceptsDamage = false;
                foreach (Cannon cannon in enemy.GetComponentsInChildren<Cannon>()) cannon.enabled = false;
            }
            if (!won)
            {
                player.enabled = false;
                foreach (Cannon cannon in playerCannons) cannon.enabled = false;
            }
            ClearShots();
            OceanVisualEvents.Notify(player.gameObject.scene);
            StateChanged?.Invoke();
            Finished?.Invoke(won);
        }

        private void ClearShots()
        {
            if (player == null) return;
            Rigidbody playerBody = player.GetComponent<Rigidbody>();
            var owners = new HashSet<Rigidbody> { playerBody };
            foreach (EnemyActor enemy in ownedEnemies)
                if (enemy != null) owners.Add(enemy.GetComponent<Rigidbody>());
            foreach (CannonProjectile shot in FindObjectsByType<CannonProjectile>(FindObjectsSortMode.None))
                if (shot.Owner != null && owners.Contains(shot.Owner))
                { shot.gameObject.SetActive(false); Destroy(shot.gameObject); }
        }

        private void ClearEnemyShots(EnemyActor enemy)
        {
            var body = enemy.GetComponent<Rigidbody>();
            foreach (CannonProjectile shot in FindObjectsByType<CannonProjectile>(FindObjectsSortMode.None))
                if (shot.Owner == body) { shot.gameObject.SetActive(false); Destroy(shot.gameObject); }
        }

        public void StopBattle()
        {
            State = BattleStatus.Idle;
            Flow.StopFlow();
            if (playerHealth != null) { playerHealth.Died -= OnPlayerDied; playerHealth.AcceptsDamage = false; }
            ClearShots();
            foreach (EnemyActor enemy in ownedEnemies)
            {
                if (enemy == null) continue;
                enemy.gameObject.SetActive(false);
                Destroy(enemy.gameObject);
            }
            enemies.Clear(); ownedEnemies.Clear();
            if (capturedPose && player != null)
            {
                player.enabled = initialMovementEnabled;
                for (int i = 0; i < playerCannons.Length; i++)
                    if (playerCannons[i] != null) playerCannons[i].enabled = cannonStates[i];
            }
            StateChanged?.Invoke();
        }

        private void OnDisable() { if (Application.isPlaying) StopBattle(); }
    }
}
