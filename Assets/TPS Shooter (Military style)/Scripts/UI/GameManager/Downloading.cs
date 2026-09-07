using UnityEngine;
using UnityEngine.UI;

using System.Collections;

using LightDev;
using LightDev.Core;
using LightDev.UI;

namespace TPSShooter.UI
{
    public class Downloading : CanvasElement
    {
        [Header("References")]
        public float progressWidth = 550;
        public Image foreground;
        public Base progressTextHolder;
        public BaseText progressText;

        public override void Subscribe()
        {
            base.Subscribe();

            Events.GameLoadHomeScene += Show;
            Events.GameReplay += Show;
            Events.SceneLoaded += Hide;
        }

        public override void Unsubscribe()
        {
            base.Unsubscribe();

            Events.GameLoadHomeScene -= Show;
            Events.GameReplay -= Show;
            Events.SceneLoaded -= Hide;
        }

        /// <summary>
        /// 加载条只在切关时显示。联机补 HUD 若把它 Show，SceneLoaded 已过，会永远停在 99%。
        /// </summary>
        public override void ShowForLateSpawn()
        {
        }

        protected override void OnStartShowing()
        {
            base.OnStartShowing();

            FillProgress();
        }

        protected virtual void FillProgress()
        {
            StartCoroutine(FillProgressCoroutine());
        }

        protected virtual IEnumerator FillProgressCoroutine()
        {
            const float fullTime = 1;
            float currentTime = 0;

            while (currentTime < fullTime)
            {
                float progress = currentTime / fullTime;

                foreground.fillAmount = progress;
                progressText.SetText((int)(progress * 100) + "%");
                progressTextHolder.SetPositionX(progress * progressWidth);

                currentTime += Time.deltaTime;
                yield return null;
            }
        }
    }
}
