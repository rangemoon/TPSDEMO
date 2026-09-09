using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

using LightDev;
using LightDev.UI;

namespace TPSShooter.UI
{
    /// <summary>
    /// 手榴弹用尽时的短提示，无按钮、不看广告。物体放在 PlayerCanvas 预制体上。
    /// </summary>
    public class GrenadeOutOfStockUI : CanvasElement
    {
        private const string HintMessage = "手榴弹不足";
        private const float HintDuration = 1.5f;
        private const float FadeInDuration = 0.12f;
        private const float FadeOutDuration = 0.18f;

        [Header("UI References")]
        public CanvasGroup canvasGroup;
        public TMP_Text hintLabel;

        private Coroutine hintRoutine;

        public override void Subscribe()
        {
            base.Subscribe();

            Events.PlayerGrenadeDepleted += ShowHint;
            Events.GamePaused += Hide;
            Events.GameResumed += Hide;
            Events.GameFinished += Hide;
            Events.GameReplay += Hide;
            Events.PlayerDied += Hide;
        }

        public override void ShowForLateSpawn()
        {
        }

        public override void Unsubscribe()
        {
            base.Unsubscribe();

            Events.PlayerGrenadeDepleted -= ShowHint;
            Events.GamePaused -= Hide;
            Events.GameResumed -= Hide;
            Events.GameFinished -= Hide;
            Events.GameReplay -= Hide;
            Events.PlayerDied -= Hide;
        }

        public void ShowHint()
        {
            PrepareVisual();
            Show();
        }

        public void HideHint()
        {
            if (gameObject.activeInHierarchy)
                InstantHide();
        }

        protected override void OnStartShowing()
        {
            PrepareVisual();

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }

            StopHintHide();
            hintRoutine = StartCoroutine(HideHintAfterDelay());
        }

        protected override void OnStartHiding()
        {
            StopHintHide();
        }

        private void PrepareVisual()
        {
            Button[] buttons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null)
                    buttons[i].gameObject.SetActive(false);
            }

            if (hintLabel != null)
                hintLabel.text = HintMessage;
        }

        private IEnumerator HideHintAfterDelay()
        {
            yield return FadeTo(1f, FadeInDuration);
            yield return new WaitForSecondsRealtime(HintDuration);
            yield return FadeTo(0f, FadeOutDuration);
            hintRoutine = null;
            Hide();
        }

        private IEnumerator FadeTo(float target, float duration)
        {
            if (canvasGroup == null || duration <= 0f)
            {
                if (canvasGroup != null)
                    canvasGroup.alpha = target;
                yield break;
            }

            float start = canvasGroup.alpha;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, target, elapsed / duration);
                yield return null;
            }

            canvasGroup.alpha = target;
        }

        private void StopHintHide()
        {
            if (hintRoutine == null)
                return;

            StopCoroutine(hintRoutine);
            hintRoutine = null;
        }
    }
}
