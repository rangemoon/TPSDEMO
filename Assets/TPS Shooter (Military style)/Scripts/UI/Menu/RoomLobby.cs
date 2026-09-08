using System.Collections.Generic;
using LightDev;
using LightDev.UI;
using Mirror.Discovery;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TPSShooter.UI.Menu
{
    /// <summary>
    /// 选完武器后的大厅：房主选三张地图之一创建房间并先进入；其他玩家搜索或填 IP 加入。
    /// </summary>
    public class RoomLobby : CanvasElement
    {
        public static bool IsPresent { get; private set; }

        [Header("Source")]
        [SerializeField] private LocationChoose locationSource;

        [Header("Tabs")]
        [SerializeField] private Button createTabButton;
        [SerializeField] private Button joinTabButton;
        [SerializeField] private GameObject createPanel;
        [SerializeField] private GameObject joinPanel;

        [Header("Create Room")]
        [SerializeField] private MapCard[] mapCards;
        [SerializeField] private Button createRoomButton;
        [SerializeField] private TextMeshProUGUI selectedMapLabel;

        [Header("Join Room")]
        [SerializeField] private TMP_InputField addressInput;
        [SerializeField] private Button searchButton;
        [SerializeField] private Button joinAddressButton;
        [SerializeField] private Transform roomListRoot;
        [SerializeField] private RoomListEntry roomEntryPrefab;
        [SerializeField] private TextMeshProUGUI statusLabel;

        [Header("Common")]
        [SerializeField] private Button backButton;
        [SerializeField] private NetworkDiscovery networkDiscovery;

        private readonly Dictionary<long, ServerResponse> discoveredRooms = new Dictionary<long, ServerResponse>();
        private readonly List<RoomListEntry> spawnedEntries = new List<RoomListEntry>();
        private int selectedCardIndex;
        private bool showingCreate = true;

        public override void Subscribe()
        {
            IsPresent = true;
            Events.RequestMenuLocation += Show;

            if (createTabButton != null)
                createTabButton.onClick.AddListener(OnShowCreate);
            if (joinTabButton != null)
                joinTabButton.onClick.AddListener(OnShowJoin);
            if (createRoomButton != null)
                createRoomButton.onClick.AddListener(OnCreateRoom);
            if (searchButton != null)
                searchButton.onClick.AddListener(OnSearchRooms);
            if (joinAddressButton != null)
                joinAddressButton.onClick.AddListener(OnJoinAddress);
            if (backButton != null)
                backButton.onClick.AddListener(OnBack);

            if (mapCards != null)
            {
                for (int i = 0; i < mapCards.Length; i++)
                {
                    int cardIndex = i;
                    if (mapCards[i] != null && mapCards[i].button != null)
                        mapCards[i].button.onClick.AddListener(() => OnMapClicked(cardIndex));
                }
            }

            BindDiscovery();
        }

        public override void Unsubscribe()
        {
            IsPresent = false;
            Events.RequestMenuLocation -= Show;

            if (createTabButton != null)
                createTabButton.onClick.RemoveListener(OnShowCreate);
            if (joinTabButton != null)
                joinTabButton.onClick.RemoveListener(OnShowJoin);
            if (createRoomButton != null)
                createRoomButton.onClick.RemoveListener(OnCreateRoom);
            if (searchButton != null)
                searchButton.onClick.RemoveListener(OnSearchRooms);
            if (joinAddressButton != null)
                joinAddressButton.onClick.RemoveListener(OnJoinAddress);
            if (backButton != null)
                backButton.onClick.RemoveListener(OnBack);

            if (mapCards != null)
            {
                for (int i = 0; i < mapCards.Length; i++)
                {
                    if (mapCards[i] != null && mapCards[i].button != null)
                        mapCards[i].button.onClick.RemoveAllListeners();
                }
            }

            UnbindDiscovery();
        }

        protected override void OnStartShowing()
        {
            RefreshMapCards();
            SelectMap(selectedCardIndex);
            ShowCreatePanel();

            if (addressInput != null && string.IsNullOrEmpty(addressInput.text))
                addressInput.text = "localhost";

            SetStatus("选择地图创建房间，或加入已有房间。");
        }

        /// <summary>
        /// 菜单大厅不能被联机补 HUD 打开。
        /// </summary>
        public override void ShowForLateSpawn()
        {
        }

        /// <summary>
        /// 切到创建房间页，显示三张地图。
        /// </summary>
        public void OnShowCreate()
        {
            Events.MenuClickSound.Call();
            ShowCreatePanel();
        }

        /// <summary>
        /// 切到加入房间页并开始搜索局域网。
        /// </summary>
        public void OnShowJoin()
        {
            Events.MenuClickSound.Call();
            showingCreate = false;
            if (createPanel != null)
                createPanel.SetActive(false);
            if (joinPanel != null)
                joinPanel.SetActive(true);
            SearchRooms();
        }

        /// <summary>
        /// 房主用当前选中的地图开房并进入该关卡。
        /// </summary>
        public void OnCreateRoom()
        {
            Events.MenuClickSound.Call();

            int sceneIndex = GetSelectedSceneIndex();
            if (sceneIndex <= 0)
            {
                SetStatus("没有可用地图。请检查 Build Settings 是否包含 3 个关卡。");
                return;
            }

            if (GameNetworkManager.singleton == null)
            {
                SetStatus("未找到 GameNetworkManager。");
                return;
            }

            StopClientDiscovery();
            Hide();
            GameNetworkManager.StartHostAtScene(sceneIndex);
        }

        /// <summary>
        /// 搜索局域网里已开的房间。
        /// </summary>
        public void OnSearchRooms()
        {
            Events.MenuClickSound.Call();
            SearchRooms();
        }

        private void ShowCreatePanel()
        {
            showingCreate = true;
            if (createPanel != null)
                createPanel.SetActive(true);
            if (joinPanel != null)
                joinPanel.SetActive(false);
            StopClientDiscovery();
            SetStatus("选择一张地图后创建房间，你将先进入该地图。");
        }

        private void SearchRooms()
        {
            discoveredRooms.Clear();
            RebuildRoomList();

            NetworkDiscovery discovery = GetDiscovery();
            if (discovery == null)
            {
                SetStatus("未找到网络发现组件。");
                return;
            }

            discovery.StopDiscovery();
            discovery.StartDiscovery();
            SetStatus("正在搜索局域网房间…");
        }

        /// <summary>
        /// 按 IP 加入房间，随后跟随房主进入同一地图。
        /// </summary>
        public void OnJoinAddress()
        {
            Events.MenuClickSound.Call();
            string address = addressInput != null ? addressInput.text : null;
            if (string.IsNullOrWhiteSpace(address))
            {
                SetStatus("请输入要加入的 IP。");
                return;
            }

            StopClientDiscovery();
            SetStatus("正在加入 " + address + " …");
            GameNetworkManager.JoinByAddress(address.Trim());
        }

        /// <summary>
        /// 返回武器选择。
        /// </summary>
        public void OnBack()
        {
            Events.MenuClickSound.Call();
            StopClientDiscovery();
            Events.RequestMenuWeapon.Call();
            Hide();
        }

        private void OnMapClicked(int cardIndex)
        {
            Events.MenuClickSound.Call();
            SelectMap(cardIndex);
        }

        private void SelectMap(int cardIndex)
        {
            if (mapCards == null || mapCards.Length == 0)
                return;

            selectedCardIndex = Mathf.Clamp(cardIndex, 0, mapCards.Length - 1);
            for (int i = 0; i < mapCards.Length; i++)
            {
                MapCard card = mapCards[i];
                if (card == null)
                    continue;
                if (card.selectedHighlight != null)
                    card.selectedHighlight.SetActive(i == selectedCardIndex);
            }

            MapCard selected = mapCards[selectedCardIndex];
            if (selectedMapLabel != null)
                selectedMapLabel.text = selected != null ? "当前地图：" + selected.DisplayName : string.Empty;
        }

        private void OnServerFound(ServerResponse response)
        {
            discoveredRooms[response.serverId] = response;
            RebuildRoomList();
            if (!showingCreate)
                SetStatus("发现 " + discoveredRooms.Count + " 个房间。");
        }

        private void OnJoinDiscovered(ServerResponse response)
        {
            Events.MenuClickSound.Call();
            StopClientDiscovery();
            SetStatus("正在加入房间…");
            GameNetworkManager.JoinByUri(response.uri);
        }

        private void RefreshMapCards()
        {
            LocationChoose.LocationInfo[] locations = locationSource != null ? locationSource.locations : null;
            if (mapCards == null)
                return;

            for (int i = 0; i < mapCards.Length; i++)
            {
                MapCard card = mapCards[i];
                if (card == null)
                    continue;

                if (locations != null && i < locations.Length && locations[i] != null)
                {
                    card.sceneIndex = locations[i].sceneIndex;
                    card.displayName = string.IsNullOrEmpty(locations[i].info)
                        ? GetSceneName(locations[i].sceneIndex)
                        : locations[i].info;
                    if (card.image != null)
                        card.image.sprite = locations[i].image;
                }
                else if (card.sceneIndex <= 0)
                {
                    card.sceneIndex = i + 1;
                    card.displayName = GetSceneName(card.sceneIndex);
                }

                if (card.nameLabel != null)
                    card.nameLabel.text = card.DisplayName;

                bool hasScene = !string.IsNullOrEmpty(SceneUtility.GetScenePathByBuildIndex(card.sceneIndex));
                if (card.button != null)
                    card.button.interactable = hasScene;
                if (card.gameObject != null)
                    card.gameObject.SetActive(hasScene || (locations != null && i < locations.Length));
            }
        }

        private int GetSelectedSceneIndex()
        {
            if (mapCards != null && selectedCardIndex >= 0 && selectedCardIndex < mapCards.Length && mapCards[selectedCardIndex] != null)
                return mapCards[selectedCardIndex].sceneIndex;

            if (locationSource != null && locationSource.locations != null && locationSource.locations.Length > 0)
                return locationSource.locations[0].sceneIndex;

            return SceneManager.sceneCountInBuildSettings > 1 ? 1 : -1;
        }

        private void RebuildRoomList()
        {
            for (int i = 0; i < spawnedEntries.Count; i++)
            {
                if (spawnedEntries[i] != null)
                    Destroy(spawnedEntries[i].gameObject);
            }
            spawnedEntries.Clear();

            if (roomListRoot == null || roomEntryPrefab == null)
                return;

            foreach (ServerResponse response in discoveredRooms.Values)
            {
                RoomListEntry entry = Instantiate(roomEntryPrefab, roomListRoot);
                entry.gameObject.SetActive(true);
                entry.Bind(response, OnJoinDiscovered);
                spawnedEntries.Add(entry);
            }
        }

        private void BindDiscovery()
        {
            NetworkDiscovery discovery = GetDiscovery();
            if (discovery == null)
                return;

            discovery.OnServerFound.AddListener(OnServerFound);
        }

        private void UnbindDiscovery()
        {
            NetworkDiscovery discovery = GetDiscovery();
            if (discovery == null)
                return;

            discovery.OnServerFound.RemoveListener(OnServerFound);
        }

        private void StopClientDiscovery()
        {
            NetworkDiscovery discovery = GetDiscovery();
            if (discovery != null)
                discovery.StopDiscovery();
        }

        private NetworkDiscovery GetDiscovery()
        {
            if (networkDiscovery != null)
                return networkDiscovery;

            GameNetworkManager manager = GameNetworkManager.singleton;
            if (manager != null)
                networkDiscovery = manager.GetComponent<NetworkDiscovery>();
            return networkDiscovery;
        }

        private void SetStatus(string message)
        {
            if (statusLabel != null)
                statusLabel.text = message;
        }

        private static string GetSceneName(int buildIndex)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
            if (string.IsNullOrEmpty(path))
                return "地图 " + buildIndex;
            return System.IO.Path.GetFileNameWithoutExtension(path);
        }

        [System.Serializable]
        public class MapCard
        {
            public GameObject gameObject;
            public Button button;
            public Image image;
            public TextMeshProUGUI nameLabel;
            public GameObject selectedHighlight;
            public int sceneIndex;
            public string displayName;

            public string DisplayName
            {
                get { return string.IsNullOrEmpty(displayName) ? GetSceneName(sceneIndex) : displayName; }
            }
        }
    }
}
