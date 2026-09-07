using System.Collections.Generic;
using UnityEngine;

using LightDev;
using Mirror;

namespace TPSShooter
{

    public class EnemyGenerator : MonoBehaviour
    {
        public GenerationWave[] generationWaves;

        private static readonly List<EnemyGenerator> instances = new List<EnemyGenerator>();

        private bool subscribed;
        private int _currentWaveIndex;
        private readonly HashSet<GameObject> _waveSpawns = new HashSet<GameObject>();

        private void OnEnable()
        {
            if (!instances.Contains(this))
                instances.Add(this);
        }

        private void OnDisable()
        {
            instances.Remove(this);
        }

        private void Start()
        {
            if (GameNetwork.IsClientOnly)
                return;

            SpawnEnemies();

            Events.EnemyKilled += OnEnemyKilled;
            Events.ZobmieKilled += OnZobmieKilled;
            subscribed = true;
        }

        private void OnDestroy()
        {
            instances.Remove(this);

            if (!subscribed)
                return;

            Events.EnemyKilled -= OnEnemyKilled;
            Events.ZobmieKilled -= OnZobmieKilled;
        }

        /// <summary>
        /// 是否还有未清完的本波刷出物，或后面仍有可实际刷出的波次。
        /// 空波次不挡胜利。
        /// </summary>
        public bool HasPendingWaves
        {
            get
            {
                PruneMissingSpawns();
                if (_waveSpawns.Count > 0)
                    return true;

                return HasSpawnableWaveFrom(_currentWaveIndex);
            }
        }

        /// <summary>
        /// 是否存在仍会刷怪或仍有本波存活刷出物的生成器。
        /// </summary>
        public static bool HasPendingWavesAnywhere()
        {
            for (int i = instances.Count - 1; i >= 0; i--)
            {
                if (instances[i] == null)
                {
                    instances.RemoveAt(i);
                    continue;
                }

                if (instances[i].HasPendingWaves)
                    return true;
            }

            return false;
        }

        private void SpawnEnemies()
        {
            // 空波次直接跳过，否则 HasPendingWaves 会永远为 true
            while (_currentWaveIndex < GetWaveCount())
            {
                if (TrySpawnCurrentWave() > 0)
                    return;

                _currentWaveIndex++;
            }
        }

        private int GetWaveCount()
        {
            return generationWaves == null ? 0 : generationWaves.Length;
        }

        private bool HasSpawnableWaveFrom(int startIndex)
        {
            for (int i = startIndex; i < GetWaveCount(); i++)
            {
                if (CountValidSpawnPoints(generationWaves[i]) > 0)
                    return true;
            }

            return false;
        }

        private static int CountValidSpawnPoints(GenerationWave wave)
        {
            if (wave == null || wave.enemyPrefab == null || wave.spawnPoints == null)
                return 0;

            int count = 0;
            for (int i = 0; i < wave.spawnPoints.Length; i++)
            {
                if (wave.spawnPoints[i] != null)
                    count++;
            }

            return count;
        }

        private int TrySpawnCurrentWave()
        {
            if (_currentWaveIndex >= GetWaveCount())
                return 0;

            GenerationWave wave = generationWaves[_currentWaveIndex];
            if (CountValidSpawnPoints(wave) == 0)
                return 0;

            int spawned = 0;
            foreach (Transform spawnPoint in wave.spawnPoints)
            {
                if (spawnPoint == null || wave.enemyPrefab == null)
                    continue;

                GameObject enemy = Instantiate(wave.enemyPrefab, spawnPoint.position, spawnPoint.rotation);
                _waveSpawns.Add(enemy);
                if (GameNetwork.IsServer)
                    SpawnEnemyOnNetwork(enemy);

                spawned++;
            }

            return spawned;
        }

        /// <summary>
        /// Host 将敌人登记到 Mirror，客户端才能看到并同步位置。
        /// </summary>
        /// <param name="enemy">刚刚实例化的敌人对象。</param>
        private void SpawnEnemyOnNetwork(GameObject enemy)
        {
            NetworkIdentity identity = enemy.GetComponent<NetworkIdentity>();
            if (identity == null)
            {
                Debug.LogError("EnemyGenerator: 联机敌人 Prefab 缺少 NetworkIdentity，客户端将看不到该敌人。", enemy);
                return;
            }

            NetworkServer.Spawn(enemy);
        }

        private void OnEnemyKilled(EnemyBehaviour enemy)
        {
            if (enemy == null)
                return;

            OnWaveSpawnKilled(enemy.gameObject);
        }

        /// <summary>
        /// 僵尸死亡时的回调；仅处理本生成器刷出的实例。
        /// </summary>
        /// <param name="zombie">死亡的僵尸实例。</param>
        private void OnZobmieKilled(ZombieBehaviour zombie)
        {
            if (zombie == null)
                return;

            OnWaveSpawnKilled(zombie.gameObject);
        }

        /// <summary>
        /// 本生成器刷出的怪清完后推进波次；胜利由 GameManager 根据注册表统一判定。
        /// </summary>
        /// <param name="spawn">死亡敌人的 GameObject。</param>
        private void OnWaveSpawnKilled(GameObject spawn)
        {
            if (!_waveSpawns.Remove(spawn))
                return;

            PruneMissingSpawns();
            if (_waveSpawns.Count > 0)
                return;

            _currentWaveIndex++;
            SpawnEnemies();
        }

        private void PruneMissingSpawns()
        {
            _waveSpawns.RemoveWhere(spawn => spawn == null);
        }

        [System.Serializable]
        public class GenerationWave
        {
            public GameObject enemyPrefab;
            public Transform[] spawnPoints;
        }

    }

}
