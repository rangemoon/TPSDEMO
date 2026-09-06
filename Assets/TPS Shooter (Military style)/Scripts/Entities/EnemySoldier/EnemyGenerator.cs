using UnityEngine;

using LightDev;

namespace TPSShooter
{

    public class EnemyGenerator : MonoBehaviour
    {
        public GenerationWave[] generationWaves;

        private void Start()
        {
            SpawnEnemies();

            Events.EnemyKilled += OnEnemyKilled;
            Events.ZobmieKilled += OnZobmieKilled;
        }

        private void OnDestroy()
        {
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
                Instantiate(generationWaves[_currentWaveIndex].enemyPrefab, spawnPoint.position, spawnPoint.rotation);
                _aliveEnemies++;
            }
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