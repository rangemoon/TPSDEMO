using System.IO;
using kcp2k;
using Mirror;
using Mirror.Discovery;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TPSShooter
{
    /// <summary>
    /// PVE 局域网 Listen Server：KCP 传输、最多 4 人、Host 权威会话状态。
    /// </summary>
    [RequireComponent(typeof(KcpTransport))]
    [RequireComponent(typeof(NetworkDiscovery))]
    [DefaultExecutionOrder(-200)]
    public class GameNetworkManager : NetworkManager
    {
        public static new GameNetworkManager singleton
        {
            get { return NetworkManager.singleton as GameNetworkManager; }
        }

        [Header("PVE LAN")]
        [SerializeField] private int pveMaxConnections = 4;
        [SerializeField] private float extraPlayerSpawnOffset = 2.5f;

        private NetworkDiscovery networkDiscovery;
        private Vector3 sceneSpawnPosition;
        private Quaternion sceneSpawnRotation = Quaternion.identity;
        private bool hasSceneSpawn;

        public override void Awake()
        {
            maxConnections = pveMaxConnections;
            if (transport == null)
                transport = GetComponent<Transport>();

            networkDiscovery = GetComponent<NetworkDiscovery>();
            if (networkDiscovery != null && networkDiscovery.transport == null)
                networkDiscovery.transport = transport;

            ValidatePlayerPrefab();
            base.Awake();
            if (NetworkManager.singleton != this)
                return;

            if (SceneManager.GetActiveScene().buildIndex == 0 && string.IsNullOrEmpty(offlineScene))
                offlineScene = SceneManager.GetActiveScene().name;

            SceneManager.sceneLoaded += HandleGameplaySceneLoaded;
            EnsureFallbackAudioListener();
            PlayerRegistry.CaptureScenePlayers();
            PlayerRegistry.RestoreScenePlayersForOffline();
            CacheSceneSpawnPose();
        }

        public override void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleGameplaySceneLoaded;
            base.OnDestroy();
        }

        /// <summary>
        /// 从菜单创建房间：把指定 Build 关卡设为 onlineScene 再 StartHost，客户端会跟进同一关。
        /// </summary>
        /// <param name="buildIndex">Build Settings 里的场景序号。</param>
        public static void StartHostAtScene(int buildIndex)
        {
            GameNetworkManager manager = singleton;
            if (manager == null)
            {
                Debug.LogError("GameNetworkManager: 菜单场景缺少 GameNetworkManager。请执行菜单 TPS Shooter/Setup Menu For LAN。");
                return;
            }

            string path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("GameNetworkManager: Build Settings 里没有序号为 " + buildIndex + " 的场景。");
                return;
            }

            if (string.IsNullOrEmpty(manager.offlineScene))
                manager.offlineScene = SceneManager.GetActiveScene().name;

            manager.onlineScene = Path.GetFileNameWithoutExtension(path);
            manager.StartHost();
        }

        /// <summary>
        /// 按 IP 加入已开房间，随后加载房主当前关卡。
        /// </summary>
        /// <param name="address">主机地址，本机第二进程填 localhost。</param>
        public static void JoinByAddress(string address)
        {
            GameNetworkManager manager = singleton;
            if (manager == null)
            {
                Debug.LogError("GameNetworkManager: 菜单场景缺少 GameNetworkManager。");
                return;
            }

            if (manager.networkDiscovery != null)
                manager.networkDiscovery.StopDiscovery();

            manager.networkAddress = address;
            manager.StartClient();
        }

        /// <summary>
        /// 加入局域网发现到的房间。
        /// </summary>
        /// <param name="uri">发现结果里的服务器地址。</param>
        public static void JoinByUri(System.Uri uri)
        {
            GameNetworkManager manager = singleton;
            if (manager == null)
            {
                Debug.LogError("GameNetworkManager: 菜单场景缺少 GameNetworkManager。");
                return;
            }

            if (uri == null)
            {
                Debug.LogError("GameNetworkManager: 加入房间失败，地址为空。");
                return;
            }

            if (manager.networkDiscovery != null)
                manager.networkDiscovery.StopDiscovery();

            manager.StartClient(uri);
        }

        /// <summary>
        /// 从菜单进关后重新捕获场景玩家：单机恢复操控，联机则关掉占位玩家。
        /// </summary>
        private void HandleGameplaySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Application.isPlaying || NetworkManager.singleton != this)
                return;
            if (mode == LoadSceneMode.Additive)
                return;

            PlayerRegistry.CaptureScenePlayers();
            if (GameNetwork.IsActive)
            {
                PlayerRegistry.DisableUnnetworkedPlayers();
                EnsureFallbackAudioListener();
            }
            else
            {
                PlayerRegistry.RestoreScenePlayersForOffline();
            }

            CacheSceneSpawnPose();
        }

        /// <summary>
        /// Mirror 会在进 Play 时关掉带 NetworkIdentity 的场景 FullPlayer，单机先没有监听器。
        /// 在本机玩家恢复前先挂一个兜底监听器，避免每帧刷屏。
        /// 场景里已有可用监听器（如 Menu 的主相机）时不开兜底，否则报双监听器警告。
        /// </summary>
        private void EnsureFallbackAudioListener()
        {
            AudioListener fallback = GetComponent<AudioListener>();
            AudioListener existing = FindFirstObjectByType<AudioListener>(FindObjectsInactive.Exclude);
            if (existing != null && existing.enabled && existing != fallback)
            {
                if (fallback != null)
                    fallback.enabled = false;
                return;
            }

            if (fallback == null)
                fallback = gameObject.AddComponent<AudioListener>();
            fallback.enabled = true;
        }

        /// <summary>
        /// 记下场景 FullPlayer 的位置，联机生成玩家时作为出生点。
        /// </summary>
        private void CacheSceneSpawnPose()
        {
            PlayerBehaviour scenePlayer = FindFirstObjectByType<PlayerBehaviour>(FindObjectsInactive.Include);
            if (scenePlayer == null)
                return;

            sceneSpawnPosition = scenePlayer.transform.position;
            sceneSpawnRotation = scenePlayer.transform.rotation;
            hasSceneSpawn = true;
        }

        /// <summary>
        /// 校验 playerPrefab 是否为带 PlayerBehaviour 的 FullPlayer（允许在子物体上）。
        /// </summary>
        private void ValidatePlayerPrefab()
        {
            if (playerPrefab == null)
            {
                Debug.LogError("GameNetworkManager: 请把 FullPlayer 赋给 Player Prefab。菜单 TPS Shooter/Setup FullPlayer For LAN 可自动配置。");
                return;
            }

            NetworkIdentity identity = playerPrefab.GetComponent<NetworkIdentity>();
            if (identity == null)
            {
                Debug.LogError("GameNetworkManager: FullPlayer 根节点缺少 NetworkIdentity。请先执行菜单 TPS Shooter/Setup FullPlayer For LAN。", playerPrefab);
                return;
            }

            if (identity.sceneId != 0)
            {
                Debug.LogError("GameNetworkManager: Player Prefab 指向了场景里的 FullPlayer 实例，客户端无法生成这个角色。请把 Project 窗口中的 FullPlayer 预制体拖到 Player Prefab。", playerPrefab);
                return;
            }

            if (playerPrefab.GetComponentInChildren<PlayerBehaviour>(true) == null)
                Debug.LogError("GameNetworkManager: Player Prefab 下找不到 PlayerBehaviour。", playerPrefab);
        }

        public override void OnStartServer()
        {
            BindOnlineSceneToCurrent();
            PlayerRegistry.DisableUnnetworkedPlayers();
            EnsureFallbackAudioListener();
            base.OnStartServer();

            NetworkServer.RegisterHandler<GamePauseRequestMessage>(OnServerPauseRequested);
            NetworkServer.RegisterHandler<GameResumeRequestMessage>(OnServerResumeRequested);
            NetworkServer.RegisterHandler<GameReplayRequestMessage>(OnServerReplayRequested);
            NetworkServer.RegisterHandler<GameLoadHomeRequestMessage>(OnServerLoadHomeRequested);

            if (networkDiscovery != null)
                networkDiscovery.AdvertiseServer();
        }

        public override void OnStopServer()
        {
            if (networkDiscovery != null)
                networkDiscovery.StopDiscovery();

            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            if (!NetworkServer.active)
            {
                PlayerRegistry.DisableUnnetworkedPlayers();
                EnsureFallbackAudioListener();
            }

            base.OnStartClient();
            NetworkClient.RegisterHandler<GameSessionStateMessage>(OnClientSessionState);
        }

        /// <summary>
        /// Host 为每个连接生成 FullPlayer，使用场景出生点并按人数横向错开。
        /// </summary>
        /// <param name="conn">加入的客户端连接。</param>
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            Transform startPos = GetStartPosition();
            Vector3 position;
            Quaternion rotation;
            if (startPos != null)
            {
                position = startPos.position;
                rotation = startPos.rotation;
            }
            else if (hasSceneSpawn)
            {
                position = sceneSpawnPosition;
                rotation = sceneSpawnRotation;
            }
            else
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
            }

            position += Vector3.right * (numPlayers * extraPlayerSpawnOffset);

            if (playerPrefab == null)
            {
                Debug.LogError("GameNetworkManager: 无法生成玩家，Player Prefab 为空。");
                return;
            }

            GameObject playerObject = Instantiate(playerPrefab, position, rotation);
            NetworkServer.AddPlayerForConnection(conn, playerObject);
        }

        /// <summary>
        /// 把当前关卡登记为联机场景，让后加入的客户端加载与 Host 相同的场景。
        /// 不触发 Host 自己再加载一遍。
        /// </summary>
        private void BindOnlineSceneToCurrent()
        {
            string currentScene = SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(currentScene))
                return;

            if (string.IsNullOrEmpty(onlineScene))
                onlineScene = currentScene;

            networkSceneName = currentScene;
        }

        public override void OnStopClient()
        {
            if (networkDiscovery != null)
                networkDiscovery.StopDiscovery();

            base.OnStopClient();
        }

        /// <summary>
        /// 由 Host 把当前暂停/结束/胜负状态广播给所有客户端。
        /// </summary>
        public static void BroadcastSessionState()
        {
            if (!NetworkServer.active)
                return;

            GameManager gameManager = GameManager.ActiveInstance;
            if (gameManager == null)
                return;

            GameSessionStateMessage message = new GameSessionStateMessage
            {
                paused = GameManager.IsGamePaused,
                finished = GameManager.IsGameFinished,
                isWin = GameManager.IsGameWon
            };
            NetworkServer.SendToAll(message);
        }

        /// <summary>
        /// 停止联机并回到菜单场景。
        /// </summary>
        public static void ReturnToMenu()
        {
            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                SceneManager.LoadScene(0);
                return;
            }

            if (NetworkServer.active)
                manager.StopHost();
            else if (NetworkClient.active)
                manager.StopClient();

            if (string.IsNullOrEmpty(manager.offlineScene))
                SceneManager.LoadScene(0);
        }

        /// <summary>
        /// 切场景前清掉对象池。Host 走 OnServerChangeScene，纯客户端走 OnClientChangeScene。
        /// </summary>
        public override void OnServerChangeScene(string newSceneName)
        {
            CleanupTransientState();
            base.OnServerChangeScene(newSceneName);
        }

        /// <summary>
        /// 纯客户端在开始加载 Host 指定场景前清掉上一局残留。
        /// </summary>
        public override void OnClientChangeScene(string newSceneName, SceneOperation sceneOperation, bool customHandling)
        {
            if (!NetworkServer.active)
                CleanupTransientState();

            base.OnClientChangeScene(newSceneName, sceneOperation, customHandling);
        }

        /// <summary>
        /// Host 重开当前联机关卡，所有客户端跟随切场景。
        /// 必须先摘掉上一局玩家，再加载同一场景；否则 conn.identity 仍占着，AddPlayer 失败，角色不会重生。
        /// </summary>
        public static void ReplayCurrentScene()
        {
            if (!NetworkServer.active || NetworkManager.singleton == null)
                return;

            if (NetworkServer.isLoadingScene)
                return;

            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                if (conn == null || conn.identity == null)
                    continue;

                NetworkServer.RemovePlayerForConnection(conn, RemovePlayerOptions.Destroy);
            }

            NetworkManager.singleton.ServerChangeScene(SceneManager.GetActiveScene().name);
        }

        private static void CleanupTransientState()
        {
            GamePool.ClearAll();
            LightDev.Events.SceneUnload.Call();
        }

        /// <summary>
        /// 处理客户端暂停请求。
        /// </summary>
        /// <param name="conn">发起请求的连接。</param>
        /// <param name="message">暂停请求消息。</param>
        private void OnServerPauseRequested(NetworkConnectionToClient conn, GamePauseRequestMessage message)
        {
            EventsProxy.RequestPause();
        }

        /// <summary>
        /// 处理客户端恢复请求。
        /// </summary>
        /// <param name="conn">发起请求的连接。</param>
        /// <param name="message">恢复请求消息。</param>
        private void OnServerResumeRequested(NetworkConnectionToClient conn, GameResumeRequestMessage message)
        {
            EventsProxy.RequestResume();
        }

        /// <summary>
        /// 处理客户端重开请求。
        /// </summary>
        /// <param name="conn">发起请求的连接。</param>
        /// <param name="message">重开请求消息。</param>
        private void OnServerReplayRequested(NetworkConnectionToClient conn, GameReplayRequestMessage message)
        {
            EventsProxy.RequestReplay();
        }

        /// <summary>
        /// 处理客户端返回菜单请求。
        /// </summary>
        /// <param name="conn">发起请求的连接。</param>
        /// <param name="message">返回菜单请求消息。</param>
        private void OnServerLoadHomeRequested(NetworkConnectionToClient conn, GameLoadHomeRequestMessage message)
        {
            EventsProxy.RequestLoadHome();
        }

        /// <summary>
        /// 客户端接收 Host 会话状态并应用到本地 GameManager。
        /// </summary>
        /// <param name="message">会话状态。</param>
        private void OnClientSessionState(GameSessionStateMessage message)
        {
            if (NetworkServer.active)
                return;

            GameManager gameManager = GameManager.ActiveInstance;
            if (gameManager != null)
                gameManager.ApplyNetworkSessionState(message.paused, message.finished, message.isWin);
        }

        private static class EventsProxy
        {
            public static void RequestPause()
            {
                LightDev.Events.GamePauseRequested.Call();
            }

            public static void RequestResume()
            {
                LightDev.Events.GameResumeRequested.Call();
            }

            public static void RequestReplay()
            {
                LightDev.Events.GameReplayRequested.Call();
            }

            public static void RequestLoadHome()
            {
                LightDev.Events.GameLoadHomeSceneRequested.Call();
            }
        }
    }
}
