using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WhiteWhale.Combat;

/// <summary>在原场景完整推进 120 秒的配置，临时加速验证并在结束后还原时间倍率。</summary>
public static class BattleFlowVerification
{
    public static string Status { get; private set; } = "Not started";
    private static readonly List<string> results = new List<string>();
    private static BattleController battle;
    private static int phase, observedWaves;
    private static float oldTimeScale, deadline, next;
    private static bool active;
    private static EnemyActor[] oldEnemies;

    public static void Begin()
    {
        if (!EditorApplication.isPlaying || active) throw new Exception("Enter Play; run only one verification.");
        results.Clear(); phase = 0; observedWaves = 0; active = true; Status = "Running";
        oldTimeScale = Time.timeScale; Time.timeScale = 4f;
        deadline = Time.time + 240f;
        EditorApplication.update += Tick;
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); results.Add("PASS " + message); }

    private static void Tick()
    {
        if (!active) return;
        try
        {
            if (!EditorApplication.isPlaying || Time.time > deadline) throw new Exception("Interrupted or timed out in phase " + phase);
            if (phase == 0)
            {
                battle = UnityEngine.Object.FindFirstObjectByType<BattleController>();
                if (battle == null || battle.State != BattleStatus.Running) return;
                battle.RestartBattle(); battle.EnemyAutoFire = false;
                Check(battle.Definition.duration == 120f && battle.Flow.TotalWaves == 4, "saved definition has 120-second duration and four waves");
                Check(battle.Flow.CurrentWave == 1 && battle.Enemies.Count == 1, "first wave immediately spawns exactly one enemy");
                observedWaves = 1; phase = 1; return;
            }
            if (phase == 1)
            {
                while (observedWaves < battle.Flow.CurrentWave)
                {
                    float expected = observedWaves * 30f;
                    Check(Mathf.Abs(battle.Flow.Elapsed - expected) < 0.75f, "wave " + (observedWaves + 1) + " spawns at " + expected + " seconds");
                    observedWaves++;
                }
                if (!battle.Flow.IsFinished) return;
                Check(battle.Flow.Elapsed == 120f && battle.Flow.SpawnedCount == 4 && battle.Enemies.Count == 4, "120-second sequence completes with four distinct live ships");
                Check(battle.Enemies.Select(e => e.GetInstanceID()).Distinct().Count() == 4, "enemy collection has no duplicated identities");
                Check(battle.State == BattleStatus.Running, "time limit does not report victory or stop enemy AI");
                var first = battle.Enemy; first.Health.TakeDamage(10000f);
                Check(battle.Enemies.Count == 3 && battle.DefeatedCount == 1 && !first.gameObject.activeSelf && battle.State == BattleStatus.Running, "one death removes only that enemy and keeps battle running");
                foreach (var enemy in battle.Enemies.ToArray()) enemy.Health.TakeDamage(10000f);
                Check(battle.Enemies.Count == 0 && battle.DefeatedCount == 4 && battle.State == BattleStatus.Running, "clearing every enemy does not automatically win");
                next = Time.time + 1f; phase = 2; return;
            }
            if (Time.time < next) return;
            if (phase == 2)
            {
                Check(battle.Flow.SpawnedCount == 4 && battle.Enemies.Count == 0, "completed sequence cannot spawn a fifth ship");
                battle.RestartBattle(); battle.EnemyAutoFire = false;
                Check(battle.Flow.CurrentWave == 1 && battle.Flow.SpawnedCount == 1 && battle.DefeatedCount == 0, "retry resets time, waves and counters");
                next = Time.time + 31f; phase = 3; return;
            }
            if (phase == 3)
            {
                Check(battle.Enemies.Count == 2 && battle.Flow.CurrentWave == 2, "retry uses only one active spawn sequence");
                foreach (var enemy in battle.Enemies) enemy.Artillery.FireAll();
                Check(UnityEngine.Object.FindObjectsByType<CannonProjectile>(FindObjectsSortMode.None).Length > 0, "multiple enemies can own independent projectiles");
                oldEnemies = battle.Enemies.ToArray(); battle.StopBattle();
                Check(battle.Enemies.Count == 0 && oldEnemies.All(e => !e.gameObject.activeSelf), "stop deactivates every owned enemy immediately");
                Check(UnityEngine.Object.FindObjectsByType<CannonProjectile>(FindObjectsSortMode.None).Length == 0, "stop clears projectiles from all enemy owners");
                next = Time.time + 31f; phase = 4; return;
            }
            if (phase == 4)
            {
                Check(battle.State == BattleStatus.Idle && battle.Enemies.Count == 0 && UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Length == 0, "stopped sequence never resumes spawning");
                battle.BeginBattle(); battle.EnemyAutoFire = false;
                battle.PlayerHealth.TakeDamage(10000f);
                Check(battle.State == BattleStatus.Lost && battle.Enemy.GetComponent<EnemyShipAI>().CurrentState == EnemyShipAI.EnemyState.Stopped, "existing player defeat stops every AI and spawn sequence");
                next = Time.time + 31f; phase = 5; return;
            }
            if (phase == 5)
            {
                Check(battle.Flow.CurrentWave == 1 && battle.Enemies.Count == 1, "defeat does not create later waves");
                Finish(null);
            }
        }
        catch (Exception exception) { Finish(exception.ToString()); }
    }
    private static void Finish(string error)
    {
        active = false; EditorApplication.update -= Tick; Time.timeScale = oldTimeScale;
        Status = error == null ? "PASS" : "FAIL " + error; results.Add(Status);
        File.WriteAllLines("Captures/BattleFlowVerification.txt", results);
        if (battle != null && EditorApplication.isPlaying) battle.RestartBattle();
        Debug.Log("Battle flow verification: " + Status);
    }
}
