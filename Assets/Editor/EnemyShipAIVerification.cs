using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WhiteWhale.Combat;

/// <summary>在现有场景 Play 中验证 AI，测试位置只存在于本次运行。</summary>
public static class EnemyShipAIVerification
{
    public static string Status { get; private set; } = "Not started";
    private static readonly List<string> results = new List<string>();
    private static BattleController battle;
    private static EnemyShipAI ai;
    private static EnemyShipActions actions;
    private static MovementSpeedModifier speed;
    private static Vector3 sample;
    private static float next, deadline, slowDistance;
    private static int phase;

    public static void Begin()
    {
        if (!EditorApplication.isPlaying || Status == "Running") throw new Exception("Enter Play; run only once at a time.");
        results.Clear(); phase = 0; deadline = Time.time + 40f; Status = "Running";
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); results.Add("PASS " + message); }

    private static void Move(Rigidbody body, Vector3 position, Quaternion rotation)
    {
        body.transform.SetPositionAndRotation(position, rotation);
        body.position = position; body.rotation = rotation;
        Physics.SyncTransforms();
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;

    private static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying || Time.time > deadline) throw new Exception("Interrupted or timed out in phase " + phase);
            if (phase == 0)
            {
                battle = UnityEngine.Object.FindFirstObjectByType<BattleController>();
                if (battle == null || battle.State != BattleStatus.Running) return;
                battle.RestartBattle(); battle.EnemyAutoFire = false;
                battle.Flow.StopFlow(); // 本验证只测单艘 AI，完整波次由 BattleFlowVerification 验证。
                battle.Player.enabled = false;
                ai = battle.Enemy.GetComponent<EnemyShipAI>(); actions = battle.Enemy.GetComponent<EnemyShipActions>();
                speed = battle.Enemy.GetComponent<MovementSpeedModifier>();
                ai.StopAI();
                Move(battle.Player.GetComponent<Rigidbody>(), new Vector3(900f, -0.15f, 0f), Quaternion.identity);
                Move(battle.Enemy.GetComponent<Rigidbody>(), new Vector3(940f, -0.15f, 0f), Quaternion.Euler(0f, 270f, 0f));
                speed.Apply(0.5f, 30f);
                ai.Initialize(battle.Enemy, actions);
                phase = 1; return;
            }
            if (phase == 1)
            {
                if (ai.CurrentState != EnemyShipAI.EnemyState.Approaching) return;
                Check(actions.FiredCount == 0, "preparation and approach do not fire out of range");
                sample = battle.Enemy.transform.position; next = Time.time + 0.8f; phase = 2; return;
            }
            if (Time.time < next) return;
            if (phase == 2)
            {
                slowDistance = HorizontalDistance(sample, battle.Enemy.transform.position);
                float expectedDistance = actions.moveSpeed * 0.5f * 0.8f;
                Check(Mathf.Abs(slowDistance - expectedDistance) < expectedDistance * 0.25f && Mathf.Abs(actions.CurrentSpeed - actions.moveSpeed * 0.5f) < 0.01f, "net multiplier halves actual enemy pursuit speed");
                speed.Clear(); sample = battle.Enemy.transform.position; next = Time.time + 0.8f; phase = 3; return;
            }
            if (phase == 3)
            {
                float normalDistance = HorizontalDistance(sample, battle.Enemy.transform.position);
                Check(normalDistance > slowDistance * 1.7f && normalDistance < slowDistance * 2.3f, "clearing slow restores enemy movement speed");
                Move(battle.Enemy.GetComponent<Rigidbody>(), new Vector3(908f, -0.15f, 0f), Quaternion.Euler(0f, 270f, 0f));
                next = Time.time + 1.8f; phase = 4; return;
            }
            if (phase == 4)
            {
                Check(actions.CurrentSpeed == 0f && actions.FiredCount == 0, "enemy stops at range; disabled auto-fire permits movement and turning only");
                battle.EnemyAutoFire = true; next = Time.time; deadline = Time.time + 14f; phase = 5; return;
            }
            if (phase == 5)
            {
                if (battle.PlayerHealth.Current == battle.PlayerHealth.Maximum) return;
                Check(actions.FiredCount > 0, "broadside alignment fires installed cannon");
                Check(battle.PlayerHealth.Current < battle.PlayerHealth.Maximum, "AI-fired physical cannonball damages player");
                // 玩家逃出交战范围，等待阶段应提前结束并重新追赶。
                Move(battle.Player.GetComponent<Rigidbody>(), new Vector3(880f, -0.15f, 0f), Quaternion.identity);
                next = Time.time + 0.6f; phase = 6; return;
            }
            if (phase == 6)
            {
                Check(ai.CurrentState == EnemyShipAI.EnemyState.Approaching && actions.CurrentSpeed > 0f, "player leaving range interrupts waiting and resumes pursuit");
                battle.PlayerHealth.TakeDamage(10000f);
                Check(battle.State == BattleStatus.Lost && ai.CurrentState == EnemyShipAI.EnemyState.Stopped, "battle defeat cancels active AI sequence");
                sample = battle.Enemy.GetComponent<Rigidbody>().position; next = Time.time + 0.4f; phase = 7; return;
            }
            if (phase == 7)
            {
                Check(Vector3.Distance(sample, battle.Enemy.GetComponent<Rigidbody>().position) < 0.001f && actions.CurrentSpeed == 0f, "stopped AI leaves no movement running");
                battle.RestartBattle(); battle.EnemyAutoFire = false; next = Time.time + 0.2f; phase = 8; return;
            }
            if (phase == 8)
            {
                Check(UnityEngine.Object.FindObjectsByType<EnemyShipAI>(FindObjectsSortMode.None).Length == 1, "retry leaves exactly one AI");
                Check(battle.Enemy.GetComponent<EnemyShipAI>().CurrentState == EnemyShipAI.EnemyState.Preparing, "retry starts one fresh preparation sequence");
                var enemy = battle.Enemy; battle.Enemy.Health.TakeDamage(10000f);
                Check(battle.State == BattleStatus.Running && enemy.GetComponent<EnemyShipAI>().CurrentState == EnemyShipAI.EnemyState.Stopped, "enemy death cancels its AI without ending battle");
                Finish(null);
            }
        }
        catch (Exception exception) { Finish(exception.Message); }
    }

    private static void Finish(string error)
    {
        EditorApplication.update -= Tick;
        Status = error == null ? "PASS" : "FAIL " + error; results.Add(Status);
        File.WriteAllLines("Captures/EnemyShipAIVerification.txt", results);
        if (battle != null && EditorApplication.isPlaying) battle.RestartBattle();
        Debug.Log("Enemy AI verification: " + Status);
    }
}
