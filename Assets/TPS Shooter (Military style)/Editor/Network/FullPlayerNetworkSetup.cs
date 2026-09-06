using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TPSShooter
{
    /// <summary>
    /// 把 FullPlayer 配成 Mirror 联机玩家预制体：根节点加网络组件，同步目标指向子物体 Player。
    /// </summary>
    public static class FullPlayerNetworkSetup
    {
        private const string FullPlayerPath = "Assets/TPS Shooter (Military style)/Prefabs/Entities/FullPlayer.prefab";

        /// <summary>
        /// 打开 FullPlayer 预制体并写入联机组件。
        /// </summary>
        [MenuItem("TPS Shooter/Setup FullPlayer For LAN")]
        public static void SetupFullPlayer()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FullPlayerPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("FullPlayer", "找不到 FullPlayer.prefab。", "OK");
                return;
            }

            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                PlayerBehaviour player = contents.GetComponentInChildren<PlayerBehaviour>(true);
                if (player == null)
                {
                    EditorUtility.DisplayDialog("FullPlayer", "FullPlayer 下没有 PlayerBehaviour，无法配置联机。", "OK");
                    return;
                }

                NetworkIdentity identity = GetOrAdd<NetworkIdentity>(contents);
                identity.hideFlags = HideFlags.None;

                NetworkTransformUnreliable networkTransform = GetOrAdd<NetworkTransformUnreliable>(contents);
                networkTransform.syncDirection = SyncDirection.ClientToServer;
                networkTransform.coordinateSpace = CoordinateSpace.World;
                networkTransform.target = player.transform;

                NetworkAnimator networkAnimator = GetOrAdd<NetworkAnimator>(contents);
                networkAnimator.clientAuthority = true;
                networkAnimator.animator = player.GetComponent<Animator>();

                GetOrAdd<PlayerNetwork>(contents);

                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                AssignToNetworkManager(prefab);
                SaveOpenScenesIfNeeded();

                EditorUtility.DisplayDialog(
                    "FullPlayer",
                    "已把 FullPlayer 配成联机玩家预制体，并尝试赋给场景里的 GameNetworkManager.playerPrefab。\n当前打开的场景已保存，以生成 sceneId。",
                    "OK");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// 取得或添加指定组件。
        /// </summary>
        /// <param name="host">要挂组件的物体。</param>
        private static T GetOrAdd<T>(GameObject host) where T : Component
        {
            T component = host.GetComponent<T>();
            if (component == null)
                component = host.AddComponent<T>();
            return component;
        }

        /// <summary>
        /// 在当前打开的场景里创建 GameNetworkManager，并赋上 FullPlayer。
        /// </summary>
        [MenuItem("TPS Shooter/Add LAN Manager To Open Scene")]
        public static void AddLanManagerToOpenScene()
        {
            GameNetworkManager existing = Object.FindFirstObjectByType<GameNetworkManager>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorUtility.DisplayDialog("LAN", "当前场景已经有 GameNetworkManager。", "OK");
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FullPlayerPath);
            GameObject host = new GameObject("GameNetworkManager");
            Undo.RegisterCreatedObjectUndo(host, "Add LAN Manager");
            GameNetworkManager manager = host.AddComponent<GameNetworkManager>();
            host.AddComponent<GameLanHud>();
            if (prefab != null)
            {
                manager.playerPrefab = prefab;
                EditorUtility.SetDirty(manager);
            }

            EditorSceneManager.MarkSceneDirty(host.scene);
            SaveOpenScenesIfNeeded();
            Selection.activeGameObject = host;
            EditorUtility.DisplayDialog(
                    "LAN",
                    "已在当前场景创建 GameNetworkManager。\n请保存场景后 Play，左上角点「创建房间 (Host)」。\n第二台用 Build 或另一台电脑点「加入 IP」。",
                    "OK");
        }

        /// <summary>
        /// 在 Menu 场景放入 GameNetworkManager，并设好 offlineScene，方便从菜单开三个关卡的房间。
        /// </summary>
        [MenuItem("TPS Shooter/Setup Menu For LAN")]
        public static void SetupMenuForLan()
        {
            string menuPath = FindMenuScenePath();
            if (string.IsNullOrEmpty(menuPath))
            {
                EditorUtility.DisplayDialog("LAN", "找不到 Menu 场景。请确认 Assets/TPS Shooter (Military style) 下有名称含 Menu 的场景。", "OK");
                return;
            }

            string previousPath = EditorSceneManager.GetActiveScene().path;
            UnityEngine.SceneManagement.Scene menuScene = EditorSceneManager.OpenScene(menuPath, OpenSceneMode.Single);
            try
            {
                GameNetworkManager existing = Object.FindFirstObjectByType<GameNetworkManager>();
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FullPlayerPath);
                if (existing == null)
                {
                    GameObject host = new GameObject("GameNetworkManager");
                    Undo.RegisterCreatedObjectUndo(host, "Setup Menu For LAN");
                    existing = host.AddComponent<GameNetworkManager>();
                    host.AddComponent<GameLanHud>();
                }

                if (prefab != null)
                    existing.playerPrefab = prefab;
                existing.dontDestroyOnLoad = true;
                existing.offlineScene = menuScene.name;
                EditorUtility.SetDirty(existing);
                EditorSceneManager.MarkSceneDirty(menuScene);
                EditorSceneManager.SaveScene(menuScene);
            }
            finally
            {
                if (!string.IsNullOrEmpty(previousPath) && previousPath != menuPath)
                    EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
            }

            EditorUtility.DisplayDialog(
                "LAN",
                "已在 Menu 放好 GameNetworkManager。\n请把 Build Settings 第 0 个设为 Menu，后面放 3 个关卡。\n从 Menu 按 Play，左上角点「创建房间: 关卡名」。另一进程在 Menu 点加入。",
                "OK");
        }

        /// <summary>
        /// 查找名称包含 Menu 的场景路径。
        /// </summary>
        private static string FindMenuScenePath()
        {
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/TPS Shooter (Military style)" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (name.IndexOf("Menu", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return path;
            }

            return null;
        }

        /// <summary>
        /// 打开并保存 TPS 关卡，给场景 FullPlayer 生成 Mirror sceneId，避免 Play/Build 被中断。
        /// </summary>
        [MenuItem("TPS Shooter/Resave Game Scenes For LAN")]
        public static void ResaveGameScenesForLan()
        {
            string[] searchFolders = { "Assets/TPS Shooter (Military style)/Scenes" };
            string[] guids = AssetDatabase.FindAssets("t:Scene", searchFolders);
            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog("LAN", "找不到 TPS Shooter 的场景。", "OK");
                return;
            }

            string currentScenePath = EditorSceneManager.GetActiveScene().path;
            int savedCount = 0;
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (string.IsNullOrEmpty(path))
                        continue;

                    UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    GameNetworkManager manager = Object.FindFirstObjectByType<GameNetworkManager>();
                    if (manager != null && manager.playerPrefab != null)
                    {
                        NetworkIdentity prefabIdentity = manager.playerPrefab.GetComponent<NetworkIdentity>();
                        if (prefabIdentity != null && prefabIdentity.sceneId != 0)
                        {
                            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FullPlayerPath);
                            if (prefab != null)
                            {
                                manager.playerPrefab = prefab;
                                EditorUtility.SetDirty(manager);
                            }
                        }
                    }

                    EditorSceneManager.MarkSceneDirty(scene);
                    if (EditorSceneManager.SaveScene(scene))
                        savedCount++;
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(currentScenePath))
                    EditorSceneManager.OpenScene(currentScenePath, OpenSceneMode.Single);
            }

            EditorUtility.DisplayDialog(
                "LAN",
                "已重新保存 " + savedCount + " 个场景，FullPlayer 的 sceneId 应已生成。\n若刚才打过 exe，需要再 Build 一次。",
                "OK");
        }

        /// <summary>
        /// 把 FullPlayer 赋给当前打开场景中的 GameNetworkManager。
        /// </summary>
        /// <param name="fullPlayerPrefab">已配置好的 FullPlayer 预制体。</param>
        private static void AssignToNetworkManager(GameObject fullPlayerPrefab)
        {
            GameNetworkManager manager = Object.FindFirstObjectByType<GameNetworkManager>();
            if (manager == null)
                return;

            Undo.RecordObject(manager, "Assign FullPlayer prefab");
            manager.playerPrefab = fullPlayerPrefab;
            EditorUtility.SetDirty(manager);
        }

        /// <summary>
        /// 保存当前打开的场景，让 Mirror 给场景 FullPlayer 写入 sceneId。
        /// </summary>
        private static void SaveOpenScenesIfNeeded()
        {
            if (EditorSceneManager.GetActiveScene().isDirty)
                EditorSceneManager.SaveOpenScenes();
        }
    }
}
