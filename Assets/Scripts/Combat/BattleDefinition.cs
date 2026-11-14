using System;
using System.Collections.Generic;
using UnityEngine;

namespace WhiteWhale.Combat
{
    [CreateAssetMenu(menuName = "White Whale/Battle Definition")]
    public sealed class BattleDefinition : ScriptableObject
    {
        public EnemyActor enemyPrefab;
        [Min(1f)] public float playerHealth = 100f;
        [Min(1f)] public float enemyHealth = 60f;
        [Header("战斗流程")]
        [Min(0.1f)] public float duration = 120f;
        public List<BattleSpawnEvent> spawnEvents = new List<BattleSpawnEvent>
        {
            new BattleSpawnEvent(0f, new Vector3(20f, 0f, 0f)),
            new BattleSpawnEvent(30f, new Vector3(-20f, 0f, 0f)),
            new BattleSpawnEvent(60f, new Vector3(0f, 0f, 20f)),
            new BattleSpawnEvent(90f, new Vector3(0f, 0f, -20f))
        };

        [Header("敌船攻击")]
        public bool enemyAutoFire = true;
        [Min(0.1f)] public float firstShotDelay = 4f;
        [Min(0.1f)] public float shotInterval = 3f;
    }

    [Serializable]
    public sealed class BattleSpawnEvent
    {
        [Min(0f)] public float spawnTime;
        [Tooltip("留空沿用本场敌船预制体。")]
        public EnemyActor enemyPrefab;
        [Tooltip("相对生成时玩家位置和水平朝向的海洋单位。")]
        public Vector3 spawnOffset;
        public BattleSpawnEvent(float time, Vector3 offset) { spawnTime = time; spawnOffset = offset; }
    }
}
