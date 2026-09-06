using UnityEngine;
using LightDev;
using XH;

namespace TPSShooter
{
    public class MenuSoundManager : MonoBehaviour
    {
        public AudioSource clickSource;

        // first: 进入游戏场景(主菜单)时调用一次
        private void Start()
        {
            xh.api.Ad.ShowInsert("first");
        }

        private void Awake()
        {
            Events.MenuClickSound += OnMenuClickSound;
        }

        private void OnDestroy()
        {
            Events.MenuClickSound -= OnMenuClickSound;
        }

        private void OnMenuClickSound()
        {
            clickSource.Play();
        }
    }
}
