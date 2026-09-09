using UnityEngine;
using UnityEngine.UI;

using LightDev;
using LightDev.UI;

namespace TPSShooter.UI
{
    /// <summary>
    /// 显示当前手榴弹数量。原先只有耗尽弹窗，开局看不出还剩几枚。
    /// </summary>
    public class PlayerGrenadeCount : CanvasElement
    {
        [Header("References")]
        public Text grenadeCountText;
        public GrenadeOutOfStockUI outOfStockHint;

        public override void Subscribe()
        {
            Events.SceneLoaded += Show;
            Events.GamePaused += OnHideHud;
            Events.GameResumed += Show;
            Events.PlayerDied += OnHideHud;
            Events.PlayerGetInVehicle += Hide;
            Events.PlayerGetOutVehicle += Show;
            Events.PlayerGrenadeCountChanged += UpdateGrenadeCountText;
            Events.PlayerGrenadeDepleted += ShowOutOfStockHint;
        }

        public override void Unsubscribe()
        {
            Events.SceneLoaded -= Show;
            Events.GamePaused -= OnHideHud;
            Events.GameResumed -= Show;
            Events.PlayerDied -= OnHideHud;
            Events.PlayerGetInVehicle -= Hide;
            Events.PlayerGetOutVehicle -= Show;
            Events.PlayerGrenadeCountChanged -= UpdateGrenadeCountText;
            Events.PlayerGrenadeDepleted -= ShowOutOfStockHint;
        }

        public override void ShowForLateSpawn()
        {
            Show();
        }

        protected override void OnStartShowing()
        {
            UpdateGrenadeCountText();
        }

        private void UpdateGrenadeCountText()
        {
            if (grenadeCountText == null)
                return;

            PlayerBehaviour player = PlayerBehaviour.GetInstance();
            if (player == null)
                return;

            grenadeCountText.text = "x" + player.GrenadeCount;
        }

        private void OnHideHud()
        {
            Hide();
            if (outOfStockHint != null)
                outOfStockHint.HideHint();
        }

        private void ShowOutOfStockHint()
        {
            GrenadeOutOfStockUI hint = outOfStockHint;
            if (hint == null)
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                if (canvas != null)
                    hint = canvas.GetComponentInChildren<GrenadeOutOfStockUI>(true);
            }

            if (hint != null)
                hint.ShowHint();
        }
    }
}
