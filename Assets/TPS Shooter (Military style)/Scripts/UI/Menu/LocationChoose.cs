using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LightDev;
using LightDev.UI;
using DG.Tweening;

namespace TPSShooter.UI.Menu
{
    public class LocationChoose : CanvasElement
    {
        [Header("References")]
        public Text locationInfoText;
        public Image locationImage;

        [Header("Locations")]
        public LocationInfo[] locations;

        [Header("Unlock")]
        public GameObject lockOverlay;
        public Button unlockButton;
        public Button playButton;

        private int locationIndex;
        private bool isShowingAd;

        public override void Subscribe()
        {
            Events.RequestMenuLocation += Show;
        }

        public override void Unsubscribe()
        {
            Events.RequestMenuLocation -= Show;
        }

        protected override void OnStartShowing()
        {
            unlockButton.onClick.AddListener(OnUnlockLocation);
            UpdateLocationInfo();
        }

        public void OnPlay()
        {
            int sceneIdx = locations[locationIndex].sceneIndex;
            if (!UnlockManager.IsLocationUnlocked(sceneIdx))
            {
                return;
            }
            Events.MenuClickSound.Call();
            Events.RequestMenuDownloading.Call(locations[locationIndex].sceneIndex);
            Hide();
        }

        public void OnBack()
        {
            Events.MenuClickSound.Call();
            Events.RequestMenuWeapon.Call();
            Hide();
        }

        public void OnNext()
        {
            locationIndex++;
            locationIndex = (locationIndex == locations.Length) ? 0 : locationIndex;
            UpdateLocationInfo();
            Events.MenuClickSound.Call();
        }

        public void OnPrevious()
        {
            locationIndex--;
            locationIndex = (locationIndex == -1) ? locations.Length - 1 : locationIndex;
            UpdateLocationInfo();
            Events.MenuClickSound.Call();
        }

        private void UpdateLocationInfo()
        {
            locationInfoText.text = locations[locationIndex].info;
            locationImage.sprite = locations[locationIndex].image;

            int sceneIdx = locations[locationIndex].sceneIndex;
            bool isUnlocked = UnlockManager.IsLocationUnlocked(sceneIdx);
            if (isUnlocked)
            {
                // SaveLoad.SceneIndex = sceneIdx;
                lockOverlay.SetActive(false);
                playButton.gameObject.SetActive(true);
            }
            else
            {
                lockOverlay.SetActive(true);
                playButton.gameObject.SetActive(false);
            }

        }

        // common_box: 带有激励视频的宝箱，看完视频解锁地图
        public void OnUnlockLocation()
        {
            if (isShowingAd) return;

            int sceneIdx = locations[locationIndex].sceneIndex;
            if (UnlockManager.IsLocationUnlocked(sceneIdx)) return;

            // isShowingAd = true;
            // // ** 谨慎使用 **
            // xh.api.Ad.ShowTrickBoxOrVideo(
            //   "common_box",
            //   () =>
            //   {
            //       UnlockManager.UnlockLocation(sceneIdx);
            //       isShowingAd = false;
            //       UpdateLocationInfo();
            //   },
            //   () =>
            //   {
            //       isShowingAd = false;
            //   }
            // );
            UnlockManager.UnlockLocation(sceneIdx);
            UpdateLocationInfo();
        }

        [System.Serializable]
        public class LocationInfo
        {
            public Sprite image;
            public int sceneIndex;
            public string info;
        }
    }
}
