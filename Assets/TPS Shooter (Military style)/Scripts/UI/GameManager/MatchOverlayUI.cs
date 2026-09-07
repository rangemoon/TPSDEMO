using LightDev;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TPSShooter.UI
{
    /// <summary>
    /// 场景级结算层：不依赖玩家 HUD。Finish 若在角色 Canvas 下，死亡或远端关闭 Canvas 后不会显示。
    /// 界面在 MatchOverlay prefab 上绑好，运行时不再拼 UI。
    /// </summary>
    public class MatchOverlayUI : MonoBehaviour
    {
        private const string WaitMessage = "等待其他玩家结束游戏";
        private const float ResultButtonDelay = 2f;

        [Header("Wait")]
        public GameObject waitRoot;
        public TextMeshProUGUI waitLabel;

        [Header("Result")]
        public GameObject resultRoot;
        public TextMeshProUGUI resultLabel;
        public GameObject continueRoot;
        public Button replayButton;
        public Button homeButton;

        private bool subscribed;

        private void Awake()
        {
            if (waitLabel != null && string.IsNullOrEmpty(waitLabel.text))
                waitLabel.text = WaitMessage;

            HideAll();
        }

        private void OnEnable()
        {
            if (subscribed)
                return;

            Events.PlayerDied += OnLocalPlayerDied;
            Events.GameFinishedResult += OnGameFinished;
            Events.GameReplay += HideAll;
            Events.GameLoadHomeScene += HideAll;

            if (replayButton != null)
                replayButton.onClick.AddListener(OnReplay);
            if (homeButton != null)
                homeButton.onClick.AddListener(OnHome);

            subscribed = true;
        }

        private void OnDisable()
        {
            if (!subscribed)
                return;

            Events.PlayerDied -= OnLocalPlayerDied;
            Events.GameFinishedResult -= OnGameFinished;
            Events.GameReplay -= HideAll;
            Events.GameLoadHomeScene -= HideAll;

            if (replayButton != null)
                replayButton.onClick.RemoveListener(OnReplay);
            if (homeButton != null)
                homeButton.onClick.RemoveListener(OnHome);

            subscribed = false;
        }

        /// <summary>
        /// 本机死亡后先出等待层。加入端注册表可能暂时看不到房主，不能因此不显示。
        /// 对局结束后由 OnGameFinished 换成胜负。
        /// </summary>
        private void OnLocalPlayerDied()
        {
            if (GameManager.IsGameFinished)
                return;

            if (resultRoot != null)
                resultRoot.SetActive(false);
            if (waitRoot != null)
                waitRoot.SetActive(true);
        }

        private void OnGameFinished(bool isWin)
        {
            if (waitRoot != null)
                waitRoot.SetActive(false);

            if (resultLabel != null)
                resultLabel.text = isWin ? "胜利" : "失败";

            if (resultRoot != null)
                resultRoot.SetActive(true);
            if (continueRoot != null)
                continueRoot.SetActive(false);

            CancelInvoke(nameof(ShowContinueButtons));
            Invoke(nameof(ShowContinueButtons), ResultButtonDelay);
        }

        private void ShowContinueButtons()
        {
            if (continueRoot != null)
                continueRoot.SetActive(true);
        }

        private void HideAll()
        {
            CancelInvoke(nameof(ShowContinueButtons));
            if (waitRoot != null)
                waitRoot.SetActive(false);
            if (resultRoot != null)
                resultRoot.SetActive(false);
        }

        private void OnReplay()
        {
            Events.GameReplayRequested.Call();
        }

        private void OnHome()
        {
            Events.GameLoadHomeSceneRequested.Call();
        }
    }
}
