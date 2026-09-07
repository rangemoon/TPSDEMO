using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using LightDev;
using Mirror;

namespace TPSShooter
{
    // This class controlls state of the game.
    // It can stop/resume/finish the game 
    //        (when the game is stopped/resumed/finished this class notifies other GameObjects that subscribes to events).
    // It can also download Menu scene and Play scene.
    public class GameManager : MonoBehaviour
    {
        public static GameManager ActiveInstance { get; private set; }
        public static bool IsGamePaused { get; private set; }
        public static bool IsGameFinished { get; private set; }
        public static bool IsGameWon { get; private set; }

        [Header("- Pool Warm Up (drag prefabs here) -")]
        public GameObject[] playerBulletPrefabs = new GameObject[0];
        public GameObject enemyBulletPrefabs;
        public GameObject hitMarkerPrefabs;
        public GameObject[] BloodEffectPrefabs = new GameObject[0];
        public int bulletWarmUpCount = 30;
        public int hitMarkerWarmUpCount = 5;
        public int bloodEffectWarmUpCount = 200;

        private bool evaluateScheduled;
        private Coroutine evaluateCoroutine;
        private bool hasHadCountablePlayer;
        private bool hasHadAliveEnemy;

        private void Awake()
        {
            ActiveInstance = this;
            IsGamePaused = false;
            IsGameFinished = false;
            IsGameWon = false;
            evaluateScheduled = false;
            hasHadCountablePlayer = false;
            hasHadAliveEnemy = false;

            // Pre-populate object pools to avoid first-frame hitches during combat
            WarmUpPools();

            Events.GamePauseRequested += OnGamePauseRequested;
            Events.GameResumeRequested += OnGameResumeRequested;
            Events.GameReplayRequested += OnGameReplayRequested;
            Events.GameLoadHomeSceneRequested += OnGameLoadHomeSceneRequested;
            Events.AnyPlayerDied += OnAnyPlayerDied;
            Events.EnemyCreated += OnEnemyCreated;
            Events.EnemyKilled += OnEnemyKilled;
            Events.ZombieCreated += OnZombieCreated;
            Events.ZobmieKilled += OnZombieKilled;
        }

        private void WarmUpPools()
        {
            for (int i = 0; i < playerBulletPrefabs.Length; i++)
                GamePool.WarmUp(playerBulletPrefabs[i], bulletWarmUpCount);
            GamePool.WarmUp(enemyBulletPrefabs, bulletWarmUpCount);
            GamePool.WarmUp(hitMarkerPrefabs, hitMarkerWarmUpCount);
            for (int i = 0; i < BloodEffectPrefabs.Length; i++)
                GamePool.WarmUp(BloodEffectPrefabs[i], bloodEffectWarmUpCount);
        }

        private void OnDestroy()
        {
            if (ActiveInstance == this)
                ActiveInstance = null;

            Events.GamePauseRequested -= OnGamePauseRequested;
            Events.GameResumeRequested -= OnGameResumeRequested;
            Events.GameReplayRequested -= OnGameReplayRequested;
            Events.GameLoadHomeSceneRequested -= OnGameLoadHomeSceneRequested;
            Events.AnyPlayerDied -= OnAnyPlayerDied;
            Events.EnemyCreated -= OnEnemyCreated;
            Events.EnemyKilled -= OnEnemyKilled;
            Events.ZombieCreated -= OnZombieCreated;
            Events.ZobmieKilled -= OnZombieKilled;
        }

        private void OnGamePauseRequested()
        {
            if (GameNetwork.IsClientOnly)
            {
                NetworkClient.Send(new GamePauseRequestMessage());
                return;
            }

            if (!IsGamePaused)
            {
                PauseGame();
            }
        }

        private void OnGameResumeRequested()
        {
            if (GameNetwork.IsClientOnly)
            {
                NetworkClient.Send(new GameResumeRequestMessage());
                return;
            }

            if (IsGamePaused)
            {
                ResumeGame();
            }
        }

        private void OnGameReplayRequested()
        {
            Replay();
        }

        private void OnGameLoadHomeSceneRequested()
        {
            LoadHomeScene();
        }

        /// <summary>
        /// 任意玩家死亡后延迟结算；联机仅 Host 裁决。
        /// </summary>
        /// <param name="player">刚刚死亡的玩家。</param>
        private void OnAnyPlayerDied(PlayerBehaviour player)
        {
            ScheduleEvaluate();
        }

        private void OnEnemyCreated(EnemyBehaviour enemy)
        {
            ScheduleEvaluate();
        }

        private void OnEnemyKilled(EnemyBehaviour enemy)
        {
            ScheduleEvaluate();
        }

        private void OnZombieCreated(ZombieBehaviour zombie)
        {
            ScheduleEvaluate();
        }

        private void OnZombieKilled(ZombieBehaviour zombie)
        {
            ScheduleEvaluate();
        }

        /// <summary>
        /// 同一帧只排一次结算，等死亡/下一波生成处理完后再判胜负。
        /// </summary>
        public void ScheduleEvaluate()
        {
            if (IsGameFinished) return;
            if (GameNetwork.IsClientOnly) return;
            if (evaluateScheduled) return;

            evaluateScheduled = true;
            evaluateCoroutine = StartCoroutine(EvaluateNextFrame());
        }

        private IEnumerator EvaluateNextFrame()
        {
            yield return null;
            evaluateScheduled = false;
            evaluateCoroutine = null;
            EvaluateMatch();
        }

        /// <summary>
        /// Host/单机权威结算：无存活玩家判负；曾出现过敌人且现已清空且无剩余波次则判胜。
        /// 必须先见过可计入玩家，避免开局关占位时误判负；必须先见过活着的敌人，避免刷怪前误判胜。
        /// </summary>
        private void EvaluateMatch()
        {
            if (IsGameFinished) return;
            if (GameNetwork.IsClientOnly) return;

            if (PlayerRegistry.HasAlivePlayer())
                hasHadCountablePlayer = true;
            if (EnemyRegistry.HasAliveEnemy())
                hasHadAliveEnemy = true;

            if (!hasHadCountablePlayer)
                return;

            if (!PlayerRegistry.HasAlivePlayer())
            {
                FinishGame(false);
                return;
            }

            if (hasHadAliveEnemy && !EnemyRegistry.HasAliveEnemy() && !EnemyGenerator.HasPendingWavesAnywhere())
                FinishGame(true);
        }

        private void PauseGame()
        {
            if (IsGameFinished) return;

            ApplyPaused(true);
            if (GameNetwork.IsServer)
                GameNetworkManager.BroadcastSessionState();
        }

        private void ResumeGame()
        {
            ApplyPaused(false);
            if (GameNetwork.IsServer)
                GameNetworkManager.BroadcastSessionState();
        }
        
        private void FinishGame(bool isWin)
        {
            if (IsGameFinished) return;

            ApplyFinished(isWin);
            if (GameNetwork.IsServer)
                GameNetworkManager.BroadcastSessionState();
        }

        /// <summary>
        /// 应用由 Host 同步过来的暂停、结束和胜负状态。
        /// </summary>
        /// <param name="paused">是否暂停。</param>
        /// <param name="finished">是否已结束。</param>
        /// <param name="isWin">是否胜利。</param>
        public void ApplyNetworkSessionState(bool paused, bool finished, bool isWin)
        {
            if (finished)
            {
                ApplyFinished(isWin);
                return;
            }

            if (paused != IsGamePaused)
                ApplyPaused(paused);
        }

        /// <summary>
        /// 应用暂停或恢复；联机不用 Time.timeScale，避免冻住所有端的网络与动画。
        /// </summary>
        /// <param name="paused">true 为暂停，false 为恢复。</param>
        private void ApplyPaused(bool paused)
        {
            if (IsGameFinished) return;
            if (IsGamePaused == paused) return;

            IsGamePaused = paused;
            if (!GameNetwork.IsActive)
                Time.timeScale = paused ? 0 : 1;

            if (paused)
                Events.GamePaused.Call();
            else
                Events.GameResumed.Call();
        }

        /// <summary>
        /// 应用对局结束状态。
        /// </summary>
        /// <param name="isWin">是否胜利。</param>
        private void ApplyFinished(bool isWin)
        {
            if (IsGameFinished) return;

            IsGameFinished = true;
            IsGameWon = isWin;
            if (!GameNetwork.IsActive)
                Time.timeScale = 0;

            Events.GameFinished.Call();
            Events.GameFinishedResult.Call(isWin);
        }

        private void Replay()
        {
            Time.timeScale = 1;
            Events.GameReplay.Call();

            if (GameNetwork.IsClientOnly)
            {
                NetworkClient.Send(new GameReplayRequestMessage());
                return;
            }

            if (GameNetwork.IsServer)
            {
                GameNetworkManager.ReplayCurrentScene();
                return;
            }

            StartCoroutine(LoadScene(SceneManager.GetActiveScene().buildIndex));
        }

        private void LoadHomeScene()
        {
            Time.timeScale = 1f;
            Events.GameLoadHomeScene.Call();

            if (GameNetwork.IsClientOnly)
            {
                NetworkClient.Send(new GameLoadHomeRequestMessage());
                return;
            }

            if (GameNetwork.IsActive)
            {
                GameNetworkManager.ReturnToMenu();
                return;
            }

            StartCoroutine(LoadScene(0));
        }

        private IEnumerator LoadScene(int sceneIndex)
        {
            GamePool.ClearAll();
            Events.SceneUnload.Call();
            yield return new WaitForSeconds(0.1f);

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneIndex);
            operation.allowSceneActivation = false;

            while (!operation.isDone)
            {
                if (operation.progress >= 0.9f)
                {
                    operation.allowSceneActivation = true;
                }

                yield return null;
            }
        }
    }
}
