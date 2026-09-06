using UnityEngine;
using UnityEngine.UI;

using LightDev;
using LightDev.UI;

using XH;

namespace TPSShooter.UI.Menu
{
    public class WeaponChoose : CanvasElement
    {
        [Header("References")]
        public Text infoText;

        [Header("Weapons")]
        public GameObject[] weapons;

        [Header("Unlock")]
        public GameObject lockOverlay;
        public Button unlockButton;
        public Button playButton;

        private int weaponIndex;
        private bool isShowingAd;

        public override void Subscribe()
        {
            Events.RequestMenuWeapon += Show;
        }

        public override void Unsubscribe()
        {
            Events.RequestMenuWeapon -= Show;
        }

        protected override void OnStartShowing()
        {
            unlockButton.onClick.AddListener(OnUnlockWeapon);
            UpdateInfo();
        }

        // common: 切换界面时调用一次（武器选择 -> 主菜单）
        public void OnBack()
        {
            Events.MenuClickSound.Call();
            Events.RequestMenu.Call();
            Hide();
        }

        // common: 切换界面时调用一次（武器选择 -> 地图选择）
        public void OnPlay()
        {
            string currentTag = weapons[weaponIndex].tag;
            if (!UnlockManager.IsWeaponUnlocked(currentTag))
            {
                return;
            }
            Events.MenuClickSound.Call();
            Events.RequestMenuLocation.Call();
            Hide();
        }

        public void OnNext()
        {
            weaponIndex++;
            weaponIndex = (weaponIndex == weapons.Length) ? 0 : weaponIndex;
            UpdateInfo();
            Events.MenuClickSound.Call();
        }

        public void OnPrevious()
        {
            weaponIndex--;
            weaponIndex = (weaponIndex == -1) ? weapons.Length - 1 : weaponIndex;
            UpdateInfo();
            Events.MenuClickSound.Call();
        }

        private void UpdateInfo()
        {
            string weaponTag = weapons[weaponIndex].tag;
            bool isUnlocked = UnlockManager.IsWeaponUnlocked(weaponTag);

            infoText.text = weaponTag;

            if (isUnlocked)
            {
                SaveLoad.WeaponTag = weaponTag;
                lockOverlay.SetActive(false);
                playButton.gameObject.SetActive(true);
            }
            else
            {
                lockOverlay.SetActive(true);
                playButton.gameObject.SetActive(false);
            }

            for (int i = 0; i < weapons.Length; i++)
            {
                weapons[i].SetActive((i == weaponIndex) ? true : false);
            }
        }

        // common_box: 带有激励视频的宝箱，看完视频解锁武器
        public void OnUnlockWeapon()
        {
            if (isShowingAd) return;

            string weaponTag = weapons[weaponIndex].tag;
            if (UnlockManager.IsWeaponUnlocked(weaponTag)) return;

            isShowingAd = true;
            // ** 谨慎使用 **
            xh.api.Ad.ShowTrickBoxOrVideo(
              "common_box",
              () =>
              {
                  UnlockManager.UnlockWeapon(weaponTag);
                  isShowingAd = false;
                  UpdateInfo();
              },
              () =>
              {
                  isShowingAd = false;
              }
            );
        }
    }
}
