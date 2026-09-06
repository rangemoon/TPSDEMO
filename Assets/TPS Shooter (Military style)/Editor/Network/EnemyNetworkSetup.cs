using System.Collections.Generic;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TPSShooter
{
    /// <summary>
    /// 把敌人/僵尸预制体配成 Mirror 联机实体：根节点补 NetworkIdentity/NetworkTransform/NetworkAnimator/网络行为组件，
    /// 并注册进每个场景里 GameNetworkManager 的 spawnPrefabs，客户端才能生成并同步敌人。
    /// </summary>
    public static class EnemyNetworkSetup
    {
        private const string EnemiesFolder = "Assets/TPS Shooter (Military style)/Prefabs/Entities/Enemeis";
        private const string ScenesFolder = "Assets/TPS Shooter (Military style)/Scenes";

        private static readonly string[] SoldierPrefabNames = { "SoldierRagdoll", "SoldierSwatNoRagdoll" };
        private static readonly string[] ZombiePrefabNames = { "Zombie" };

        /// <summary>
        /// 配置全部敌人预制体，并注册到所有场景里的 GameNetworkManager。
        /// </summary>
        [MenuItem("TPS Shooter/Setup Enemies For LAN")]
        public static void SetupEnemiesForLan()
        {
            int configured = 0;
            foreach (string prefabName in SoldierPrefabNames)
            {
                if (SetupEnemyPrefab(prefabName, true))
                    configured++;
            }

            foreach (string prefabName in ZombiePrefabNames)
            {
                if (SetupEnemyPrefab(prefabName, false))
                    configured++;
            }

            int registeredScenes = RegisterToAllManagers();

            EditorUtility.DisplayDialog(
                "LAN",
                "已配置 " + configured + " 个敌人预制体，并在 " + registeredScenes + " 个场景的 GameNetworkManager 注册了 spawnPrefabs。\n" +
                "若刚才打过 exe，需要再 Build 一次。",
                "OK");
        }

        /// <summary>
        /// 打开敌人预制体，在根节点补齐 Mirror 组件。
        /// 网络行为组件带 RequireComponent(行为脚本)，行为脚本不在根节点时会误加空组件，因此直接报错跳过。
        /// </summary>
        /// <param name="prefabName">预制体名（不含路径与扩展名）。</param>
        /// <param name="isSoldier">士兵挂 EnemyNetwork，僵尸挂 ZombieNetwork。</param>
        /// <returns>配置成功返回 true。</returns>
        private static bool SetupEnemyPrefab(string prefabName, bool isSoldier)
        {
            string path = EnemiesFolder + "/" + prefabName + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning("EnemyNetworkSetup: 找不到预制体 " + path);
                return false;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool behaviourOk = isSoldier
                    ? contents.GetComponent<EnemyBehaviour>() != null
                    : contents.GetComponent<ZombieBehaviour>() != null;
                if (!behaviourOk)
                {
                    Debug.LogError("EnemyNetworkSetup: " + prefabName + " 根节点没有" + (isSoldier ? "EnemyBehaviour" : "ZombieBehaviour") + "，已跳过。", prefab);
                    return false;
                }

                NetworkIdentity identity = GetOrAdd<NetworkIdentity>(contents);
                identity.hideFlags = HideFlags.None;

                NetworkTransformUnreliable networkTransform = GetOrAdd<NetworkTransformUnreliable>(contents);
                networkTransform.syncDirection = SyncDirection.ServerToClient;
                networkTransform.coordinateSpace = CoordinateSpace.World;
                networkTransform.target = contents.transform;

                NetworkAnimator networkAnimator = GetOrAdd<NetworkAnimator>(contents);
                networkAnimator.clientAuthority = false;
                if (networkAnimator.animator == null)
                    networkAnimator.animator = contents.GetComponent<Animator>();

                if (isSoldier)
                    GetOrAdd<EnemyNetwork>(contents);
                else
                    GetOrAdd<ZombieNetwork>(contents);

                PrefabUtility.SaveAsPrefabAsset(contents, path);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// 把敌人预制体登记进每个场景里 GameNetworkManager 的 spawnPrefabs（去重），并保存场景。
        /// </summary>
        /// <returns>实际修改并保存的场景数。</returns>
        private static int RegisterToAllManagers()
        {
            List<GameObject> enemyPrefabs = LoadEnemyPrefabs();
            if (enemyPrefabs.Count == 0)
                return 0;

            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { ScenesFolder });
            if (guids.Length == 0)
                return 0;

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
                    if (manager == null)
                        continue;

                    bool dirty = false;
                    foreach (GameObject enemyPrefab in enemyPrefabs)
                    {
                        if (manager.spawnPrefabs.Contains(enemyPrefab))
                            continue;

                        manager.spawnPrefabs.Add(enemyPrefab);
                        dirty = true;
                    }

                    if (!dirty)
                        continue;

                    EditorUtility.SetDirty(manager);
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

            return savedCount;
        }

        /// <summary>
        /// 加载全部敌人预制体；缺失的单个预制体只告警不中断。
        /// </summary>
        private static List<GameObject> LoadEnemyPrefabs()
        {
            List<GameObject> enemyPrefabs = new List<GameObject>();
            foreach (string prefabName in SoldierPrefabNames)
                AddEnemyPrefab(enemyPrefabs, prefabName);
            foreach (string prefabName in ZombiePrefabNames)
                AddEnemyPrefab(enemyPrefabs, prefabName);
            return enemyPrefabs;
        }

        /// <summary>
        /// 按名加载敌人预制体并加入列表。
        /// </summary>
        /// <param name="enemyPrefabs">目标列表。</param>
        /// <param name="prefabName">预制体名。</param>
        private static void AddEnemyPrefab(List<GameObject> enemyPrefabs, string prefabName)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemiesFolder + "/" + prefabName + ".prefab");
            if (prefab != null)
                enemyPrefabs.Add(prefab);
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
    }
}
