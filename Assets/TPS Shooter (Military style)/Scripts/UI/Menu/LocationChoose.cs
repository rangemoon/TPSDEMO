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

        private int locationIndex;

        public override void Subscribe()
        {
            Events.RequestMenuLocation += OnRequestShow;
        }

        public override void Unsubscribe()
        {
            Events.RequestMenuLocation -= OnRequestShow;
        }

        /// <summary>
        /// 正式大厅 RoomLobby 在场时由它接手选图开房，旧选关页不再弹出。
        /// </summary>
        private void OnRequestShow()
        {
            if (RoomLobby.IsPresent)
                return;

            Show();
        }

        protected override void OnStartShowing()
        {
            UpdateLocationInfo();
        }

        /// <summary>
        /// 旧选关页的 Play 也改为创建房间，避免再走单机 Download 进关。
        /// </summary>
        public void OnPlay()
        {
            OnHostPlay();
        }

        /// <summary>
        /// 从选关界面创建局域网房间，进入当前选中的关卡。
        /// </summary>
        public void OnHostPlay()
        {
            Events.MenuClickSound.Call();
            Hide();
            TPSShooter.GameNetworkManager.StartHostAtScene(locations[locationIndex].sceneIndex);
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
