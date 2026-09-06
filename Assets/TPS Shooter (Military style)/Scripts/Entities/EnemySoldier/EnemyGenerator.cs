using UnityEngine;

using LightDev;
using Mirror;

namespace TPSShooter
{

    public class EnemyGenerator : MonoBehaviour
    {
        public GenerationWave[] generationWaves;

        private bool subscribed;

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
            if (!subscribed)
                return;

            Events.EnemyKilled -= OnEnemyKilled;
            Events.ZobmieKilled -= OnZobmieKilled;
        }

        private int _currentWaveIndex;
        private int _aliveEnemies;

        private void SpawnEnemies()
        {
            if (_currentWaveIndex >= generationWaves.Length)
                return;

            foreach (Transform spawnPoint in generationWaves[_currentWaveIndex].spawnPoints)
            {
                GameObject enemy = Instantiate(generationWaves[_currentWaveIndex].enemyPrefab, spawnPoint.position, spawnPoint.rotation);
                if (GameNetwork.IsServer)
                    SpawnEnemyOnNetwork(enemy);

                _aliveEnemies++;
            }
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
            _aliveEnemies--;

            if (_aliveEnemies != 0) return;

            _currentWaveIndex++;

            if (_currentWaveIndex >= generationWaves.Length)
            {
                Events.GameWon.Call();
                return;
            }

            SpawnEnemies();
        }

        /// <summary>
        /// 僵尸死亡时的回调，减少存活敌人计数并检查波次推进。
        /// </summary>
        /// <param name="zombie">死亡的僵尸实例。</param>
        private void OnZobmieKilled(ZombieBehaviour zombie)
        {
            _aliveEnemies--;

            if (_aliveEnemies != 0) return;

            _currentWaveIndex++;

            if (_currentWaveIndex >= generationWaves.Length)
            {
                Events.GameWon.Call();
                return;
            }

            SpawnEnemies();
        }

        [System.Serializable]
        public class GenerationWave
        {
            public GameObject enemyPrefab;
            public Transform[] spawnPoints;
        }

    }

}