using System;
using Mirror;
using Mirror.Discovery;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TPSShooter
{
    /// <summary>
    /// 局域网调试 HUD，默认关闭。正式进房走菜单 RoomLobby。
    /// </summary>
    [RequireComponent(typeof(GameNetworkManager))]
    [RequireComponent(typeof(NetworkDiscovery))]
    public class GameLanHud : MonoBehaviour
    {
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;
        [SerializeField] private bool enableDebugHud;

        private NetworkDiscovery networkDiscovery;
        private string address = "localhost";
        private Uri discoveredUri;
        private bool visible;

        private void Awake()
        {
            networkDiscovery = GetComponent<NetworkDiscovery>();
            networkDiscovery.OnServerFound.AddListener(OnServerFound);

            // 正式大厅已替换 F1 调试 HUD，默认关闭。
            if (!enableDebugHud)
                enabled = false;
            else
                visible = true;
        }

        private void OnDestroy()
        {
            if (networkDiscovery != null)
                networkDiscovery.OnServerFound.RemoveListener(OnServerFound);
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible)
                return;

            GUILayout.BeginArea(new Rect(10, 10, 340, 420), GUI.skin.box);
            GUILayout.Label("PVE 局域网");
            GUILayout.Label("场景: " + SceneManager.GetActiveScene().name);
            GUILayout.Label("从 Menu 启动，三个关卡都可开房");

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                GUILayout.Label("未找到 GameNetworkManager");
                GUILayout.EndArea();
                return;
            }

            if (!NetworkClient.isConnected && !NetworkServer.active)
            {
                DrawOffline(manager);
            }
            else
            {
                DrawOnline(manager);
            }

            GUILayout.Label("F1 显示/隐藏");
            GUILayout.EndArea();
        }

        /// <summary>
        /// 绘制未连接时的创建/加入控件。
        /// </summary>
        /// <param name="manager">当前 NetworkManager。</param>
        private void DrawOffline(NetworkManager manager)
        {
            DrawHostButtons(manager);

            GUILayout.BeginHorizontal();
            address = GUILayout.TextField(address);
            if (GUILayout.Button("加入 IP", GUILayout.Width(80)))
            {
                if (networkDiscovery != null)
                    networkDiscovery.StopDiscovery();
                manager.networkAddress = address;
                manager.StartClient();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("同电脑第二进程填 localhost");

            if (GUILayout.Button("搜索局域网房间"))
            {
                discoveredUri = null;
                networkDiscovery.StartDiscovery();
            }

            if (discoveredUri != null && GUILayout.Button("加入发现的房间"))
            {
                if (networkDiscovery != null)
                    networkDiscovery.StopDiscovery();
                manager.StartClient(discoveredUri);
            }
        }

        /// <summary>
        /// 菜单里按 Build 关卡逐个开房；已在关卡内则只开当前关。
        /// </summary>
        /// <param name="manager">当前 NetworkManager。</param>
        private void DrawHostButtons(NetworkManager manager)
        {
            if (SceneManager.GetActiveScene().buildIndex <= 0)
            {
                int sceneCount = SceneManager.sceneCountInBuildSettings;
                if (sceneCount <= 1)
                    GUILayout.Label("Build Settings 请把 Menu 放第 0，后面放 3 个关卡");

                for (int i = 1; i < sceneCount; i++)
                {
                    string path = SceneUtility.GetScenePathByBuildIndex(i);
                    string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (GUILayout.Button("创建房间: " + sceneName))
                        GameNetworkManager.StartHostAtScene(i);
                }

                return;
            }

            if (GUILayout.Button("创建房间 (Host)"))
                manager.StartHost();
        }

        /// <summary>
        /// 绘制已连接时的状态与断开按钮。
        /// </summary>
        /// <param name="manager">当前 NetworkManager。</param>
        private void DrawOnline(NetworkManager manager)
        {
            if (NetworkServer.active && NetworkClient.active)
                GUILayout.Label("模式: Host");
            else if (NetworkServer.active)
                GUILayout.Label("模式: Server");
            else
                GUILayout.Label("模式: Client");

            if (NetworkServer.active)
                GUILayout.Label("人数: " + NetworkServer.connections.Count);
            GUILayout.Label("看不到对方时：双方必须同一关卡，且 Player Prefab 是 Project 里的 FullPlayer");

            if (GUILayout.Button("断开"))
            {
                if (NetworkServer.active)
                    manager.StopHost();
                else
                    manager.StopClient();
            }
        }

        /// <summary>
        /// 收到局域网发现回复时记录房间地址。
        /// </summary>
        /// <param name="response">发现到的服务器响应。</param>
        private void OnServerFound(ServerResponse response)
        {
            discoveredUri = response.uri;
        }
    }
}
