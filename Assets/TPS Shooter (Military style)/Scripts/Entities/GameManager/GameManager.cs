using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using LightDev;

namespace TPSShooter
{
    // This class controlls state of the game.
    // It can stop/resume/finish the game 
    //        (when the game is stopped/resumed/finished this class notifies other GameObjects that subscribes to events).
    // It can also download Menu scene and Play scene.
    public class GameManager : MonoBehaviour
    {
        public static bool IsGamePaused { get; private set; }
        public static bool IsGameFinished { get; private set; }

        [Header("- Pool Warm Up (drag prefabs here) -")]
        public GameObject[] playerBulletPrefabs = new GameObject[0];
        public GameObject enemyBulletPrefabs;
        public GameObject hitMarkerPrefabs;
        public GameObject[] BloodEffectPrefabs = new GameObject[0];
        public int bulletWarmUpCount = 30;
        public int hitMarkerWarmUpCount = 5;
        public int bloodEffectWarmUpCount = 200;

        private void Awake()
        {
            IsGamePaused = false;
            IsGameFinished = false;

            // Pre-populate object pools to avoid first-frame hitches during combat
            WarmUpPools();

            Events.GamePauseRequested += OnGamePauseRequested;
            Events.GameResumeRequested += OnGameResumeRequested;
            Events.GameReplayRequested += OnGameReplayRequested;
            Events.GameLoadHomeSceneRequested += OnGameLoadHomeSceneRequested;
            Events.PlayerDied += OnPlayerDied;
            Events.GameWon += OnGameWon;
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
            Events.GamePauseRequested -= OnGamePauseRequested;
            Events.GameResumeRequested -= OnGameResumeRequested;
            Events.GameReplayRequested -= OnGameReplayRequested;
            Events.GameLoadHomeSceneRequested -= OnGameLoadHomeSceneRequested;
            Events.PlayerDied -= OnPlayerDied;
            Events.GameWon -= OnGameWon;
        }

        private void OnGamePauseRequested()
        {
            if (!IsGamePaused)
            {
                PauseGame();
            }
        }

        private void OnGameResumeRequested()
        {
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

        private void OnPlayerDied()
        {
            if (IsGameFinished) return;

            FinishGame(false);
        }

        private void OnGameWon()
        {
            if (IsGameFinished) return;

            FinishGame(true);
        }

        private void PauseGame()
        {
            if (IsGameFinished) return;

            Time.timeScale = 0;

            IsGamePaused = true;
            Events.GamePaused.Call();
        }

        private void ResumeGame()
        {
            Time.timeScale = 1;

            IsGamePaused = false;
            Events.GameResumed.Call();
        }
        
        private void FinishGame(bool isWin)
        {
            IsGameFinished = true;
            Events.GameFinished.Call();
            Events.GameFinishedResult.Call(isWin);
        }

        private void Replay()
        {
            Time.timeScale = 1;
            Events.GameReplay.Call();
            StartCoroutine(LoadScene(SceneManager.GetActiveScene().buildIndex));
        }

        private void LoadHomeScene()
        {
            Time.timeScale = 1f;
            Events.GameLoadHomeScene.Call();
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
