using UnityEngine;
using UnityEngine.UI;

using LightDev;
using LightDev.UI;
using XH;

namespace TPSShooter.UI
{
    public class GrenadeOutOfStockUI : CanvasElement
    {
        [Header("UI References")]
        public Button watchVideoButton;
        public Button closeButton;

        [Header("Input Panels")]
        public GameObject[] inputPanels;

        private bool isShowingAd;
        private bool _inputPanelsHidden;

        public override void Subscribe()
        {
            base.Subscribe();

            Events.PlayerGrenadeDepleted += Show;
            Events.GamePaused += Hide;
            Events.GameResumed += Hide;
            Events.GameFinished += Hide;
            Events.GameReplay += Hide;
            Events.PlayerDied += Hide;

            if (watchVideoButton != null)
                watchVideoButton.onClick.AddListener(OnWatchVideo);

            if (closeButton != null)
                closeButton.onClick.AddListener(OnClose);
        }

        public override void Unsubscribe()
        {
            base.Unsubscribe();

            Events.PlayerGrenadeDepleted -= Show;
            Events.GamePaused -= Hide;
            Events.GameResumed -= Hide;
            Events.GameFinished -= Hide;
            Events.GameReplay -= Hide;
            Events.PlayerDied -= Hide;

            if (watchVideoButton != null)
                watchVideoButton.onClick.RemoveListener(OnWatchVideo);

            if (closeButton != null)
                closeButton.onClick.RemoveListener(OnClose);
        }

        public void OnWatchVideo()
        {
            if (isShowingAd) return;

            isShowingAd = true;
            xh.api.Ad.ShowTrickBoxOrVideo("common_box", () => {
                PlayerBehaviour.GetInstance().AddGrenades(
                    PlayerBehaviour.GetInstance().grenadeSettings.grenadesPerVideo);
                isShowingAd = false;
                Hide();
            }, () => {
                isShowingAd = false;
                Hide();
            });
        }

        public void OnClose()
        {
            Hide();
        }

        /// <summary>
        /// 面板显示时隐藏 Input 面板，防止玩家误触操作。
        /// </summary>
        protected override void OnStartShowing()
        {
            _inputPanelsHidden = false;

            foreach (GameObject panel in inputPanels)
            {
                if (panel != null && panel.activeSelf)
                {
                    panel.SetActive(false);
                    _inputPanelsHidden = true;
                }
            }
        }

        /// <summary>
        /// 面板隐藏时恢复 Input 面板显示。
        /// </summary>
        protected override void OnFinishHiding()
        {
            if (!_inputPanelsHidden) return;

            foreach (GameObject panel in inputPanels)
            {
                if (panel != null)
                {
                    panel.SetActive(true);
                }
            }
        }
    }
}
