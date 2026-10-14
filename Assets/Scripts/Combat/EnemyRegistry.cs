using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 场上所有敌人和总击杀数。刷新点靠它判断该不该刷，结算靠它判断打够了没有。
///
/// 做成静态的，是因为「一共打死几个」本来就是全局的一件事，不该由某一个刷新点拿着——
/// 场景里有几个刷新点，就该看到同一个数。
///
/// 到击杀目标之后：先标记成已停止（所有刷新点看到这个就不再刷），再清场，把场上剩下的
/// 敌人一起沉掉。清场过程里死掉的不算玩家击杀，不然数字会越过目标继续涨。
/// </summary>
public static class EnemyRegistry
{
    private static readonly List<EnemyShip> Alive = new List<EnemyShip>();

    // 正在清场。这期间死掉的敌人是「被系统收掉的」，不是玩家打死的，不该计入击杀数。
    private static bool clearing;

    public static int AliveCount => Alive.Count;
    public static int TotalKills { get; private set; }

    /// <summary>打死多少算这一局结束。由刷新点在开局时写进来。</summary>
    public static int KillTarget { get; private set; }

    /// <summary>已经打够了，刷新点都该关了。</summary>
    public static bool Stopped { get; private set; }

    /// <summary>击杀数变化时广播，参数是新的总数。给 UI 用。</summary>
    public static event Action<int> KillsChanged;

    /// <summary>
    /// Project Settings 里关掉「域重载」之后，静态字段在两次 Play 之间是留着的——
    /// 上一局的击杀数会原样带到下一局。这里在每次进 Play 时清一次，别省这一步。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Alive.Clear();
        TotalKills = 0;
        KillTarget = 0;
        Stopped = false;
        clearing = false;
        KillsChanged = null;
    }

    public static void SetKillTarget(int target)
    {
        KillTarget = Mathf.Max(0, target);
    }

    public static void Register(EnemyShip enemy)
    {
        if (enemy != null && !Alive.Contains(enemy))
        {
            Alive.Add(enemy);
        }
    }

    public static void Unregister(EnemyShip enemy)
    {
        Alive.Remove(enemy);
    }

    /// <summary>敌人被玩家打死了。加一分，顺手看看是不是打够了。</summary>
    public static void ReportKill(EnemyShip enemy)
    {
        Alive.Remove(enemy);

        if (clearing)
        {
            return;
        }

        TotalKills++;
        KillsChanged?.Invoke(TotalKills);

        if (KillTarget > 0 && TotalKills >= KillTarget)
        {
            StopAndClear();
        }
    }

    /// <summary>关掉所有刷新点，并把场上还剩的敌人一起沉掉。</summary>
    private static void StopAndClear()
    {
        Stopped = true;
        clearing = true;

        // 先拷一份再清空。一边遍历一边删会漏掉元素。
        EnemyShip[] remaining = Alive.ToArray();
        Alive.Clear();

        for (int i = 0; i < remaining.Length; i++)
        {
            if (remaining[i] != null)
            {
                remaining[i].Kill();
            }
        }

        clearing = false;
    }
}
