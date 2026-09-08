using System;
using Mirror.Discovery;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TPSShooter.UI.Menu
{
    /// <summary>
    /// 局域网房间列表中的一行，点击后加入对应房间。
    /// </summary>
    public class RoomListEntry : MonoBehaviour
    {
        [SerializeField] private Button joinButton;
        [SerializeField] private TextMeshProUGUI label;

        private Action<ServerResponse> onJoin;

        /// <summary>
        /// 用发现到的房间数据刷新这一行。
        /// </summary>
        /// <param name="response">局域网发现结果。</param>
        /// <param name="joinCallback">点击加入时回调。</param>
        public void Bind(ServerResponse response, Action<ServerResponse> joinCallback)
        {
            onJoin = joinCallback;

            string address = response.EndPoint != null ? response.EndPoint.Address.ToString() : "未知地址";
            string mapName = string.IsNullOrEmpty(response.sceneName) ? "未知地图" : response.sceneName;
            if (label != null)
                label.text = mapName + "  ·  " + address;

            if (joinButton == null)
                return;

            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(() => onJoin?.Invoke(response));
        }
    }
}
