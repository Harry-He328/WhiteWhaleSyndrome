using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace WhiteWhale.Combat
{
    /// <summary>沿用 Dicy 的关卡生成事件：读取时间配置，协程按顺序刷新，不承担胜负判定。</summary>
    public sealed class BattleFlow : MonoBehaviour
    {
        public int CurrentWave { get; private set; }
        public int TotalWaves => events.Length;
        public int SpawnedCount { get; private set; }
        public float Elapsed => running ? Mathf.Min(Time.time - startedAt, duration) : elapsed;
        public bool IsFinished { get; private set; }

        private BattleController battle;
        private BattleSpawnEvent[] events = Array.Empty<BattleSpawnEvent>();
        private Coroutine currentSequence;
        private float duration, startedAt, elapsed;
        private bool running;

        public void Begin(BattleController owner)
        {
            StopFlow();
            battle = owner;
            duration = owner.Definition.duration;
            if (duration <= 0f || owner.Definition.spawnEvents == null) throw new InvalidOperationException("战斗流程时长或生成事件无效。");
            events = owner.Definition.spawnEvents.Select(e => e == null ? null : new BattleSpawnEvent(e.spawnTime, e.spawnOffset) { enemyPrefab = e.enemyPrefab })
                .OrderBy(e => e == null ? float.MaxValue : e.spawnTime).ToArray();
            if (events.Any(e => e == null || e.spawnTime < 0f || e.spawnTime >= duration))
                throw new InvalidOperationException("生成事件时间必须在战斗流程时长以内。");
            CurrentWave = 0; SpawnedCount = 0; elapsed = 0f; IsFinished = false;
            startedAt = Time.time; running = true;
            currentSequence = StartCoroutine(CombatSequence1());
        }

        private IEnumerator CombatSequence1() // 时间推进 → 刷新一波 → 等待下一波
        {
            foreach (BattleSpawnEvent spawnEvent in events)
            {
                while (battle.State == BattleStatus.Running && Time.time - startedAt < spawnEvent.spawnTime)
                    yield return null;
                if (battle.State != BattleStatus.Running) yield break;
                battle.SpawnEnemy(spawnEvent);
                CurrentWave++;
                SpawnedCount++;
            }
            while (battle.State == BattleStatus.Running && Time.time - startedAt < duration)
                yield return null;
            if (battle.State == BattleStatus.Running)
            {
                elapsed = duration;
                IsFinished = true; // 仅结束生成序列，现存敌人继续行动。
            }
            running = false;
            currentSequence = null;
        }

        public void StopFlow()
        {
            if (running) elapsed = Elapsed;
            running = false;
            if (currentSequence != null) { StopCoroutine(currentSequence); currentSequence = null; }
        }

        private void OnDisable() => StopFlow();
    }
}
