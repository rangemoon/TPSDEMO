using UnityEngine;
using UnityEngine.UI;

using DG.Tweening;

namespace LightDev.UI
{
    [RequireComponent(typeof(Image))]
    public class ButtonScale : BaseButton
    {
        private Vector3 normalScale;

        protected override void Awake()
        {
            base.Awake();
            normalScale = target.transform.localScale;
        }

        protected override void AnimatePress()
        {
            KillSequences();
            Sequence(target.transform.DOScale(normalScale * 0.8f, 0.1f));
        }

        protected override void AnimateUnpress()
        {
            KillSequences();
            Sequence(target.transform.DOScale(normalScale, 0.1f));
        }

        public override void ResetButton()
        {
            KillSequences();
            target.transform.localScale = normalScale;
        }
    }
}
