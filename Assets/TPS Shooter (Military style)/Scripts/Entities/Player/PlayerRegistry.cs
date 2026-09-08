using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace TPSShooter
{
    /// <summary>
    /// 联机/单机共用的玩家注册表：记录全部玩家、本地玩家，并提供最近存活目标查询。
    /// </summary>
    public static class PlayerRegistry
    {
        private static readonly List<PlayerBehaviour> players = new List<PlayerBehaviour>();
        private static readonly List<GameObject> scenePlayerRoots = new List<GameObject>();
        private static PlayerBehaviour localPlayer;

        /// <summary>
        /// 当前本机控制的玩家；未就绪时返回 null。
        /// </summary>
        public static PlayerBehaviour GetLocalPlayer()
        {
            return localPlayer;
        }

        /// <summary>
        /// 当前已注册的全部玩家（含死亡），只读列表。
        /// </summary>
        public static IReadOnlyList<PlayerBehaviour> GetPlayers()
        {
            return players;
        }

        /// <summary>
        /// 将玩家加入注册表。已登记时仍会在变为可计入后通知结算
        /// （Awake 时 netId 往往还是 0，OnStartClient 必须再触发一次）。
        /// </summary>
        /// <param name="player">要登记的玩家。</param>
        public static void Register(PlayerBehaviour player)
        {
            if (player == null)
                return;

            if (!players.Contains(player))
                players.Add(player);

            if (IsCountableAlive(player) && GameManager.ActiveInstance != null)
                GameManager.ActiveInstance.ScheduleEvaluate();
        }

        /// <summary>
        /// 将玩家从注册表移除；若是本地玩家则同时清空本地引用。
        /// 仅当移除的是可计入对局的玩家时才通知结算（掉线），忽略场景占位关闭。
        /// </summary>
        /// <param name="player">要移除的玩家。</param>
        public static void Unregister(PlayerBehaviour player)
        {
            if (player == null)
                return;

            bool shouldEvaluate = IsCountableAlive(player);
            bool removed = players.Remove(player);
            if (localPlayer == player)
                localPlayer = null;

            if (removed && shouldEvaluate && GameManager.ActiveInstance != null)
                GameManager.ActiveInstance.ScheduleEvaluate();
        }

        /// <summary>
        /// 标记本机控制的玩家，并确保其已登记。
        /// </summary>
        /// <param name="player">本机玩家。</param>
        public static void SetLocal(PlayerBehaviour player)
        {
            localPlayer = player;
            Register(player);
        }

        /// <summary>
        /// 是否仍有至少一名可计入对局的存活玩家。
        /// Host 上优先看 Mirror 连接上的玩家，避免注册表短暂漏掉房主时误判全灭。
        /// 离线仍走注册表；忽略 null、未激活、已死亡，以及尚未分配 netId 的场景占位。
        /// </summary>
        public static bool HasAlivePlayer()
        {
            if (NetworkServer.active && NetworkServer.connections.Count > 0)
            {
                bool sawPlayer = false;
                foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
                {
                    if (conn == null || conn.identity == null)
                        continue;

                    PlayerBehaviour player = conn.identity.GetComponentInChildren<PlayerBehaviour>(true);
                    if (player == null)
                        continue;

                    sawPlayer = true;
                    if (player.IsAlive)
                        return true;
                }

                if (sawPlayer)
                    return false;
            }

            for (int i = 0; i < players.Count; i++)
            {
                if (IsCountableAlive(players[i]))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 返回距离指定位置最近的存活玩家；没有存活玩家时返回 null。
        /// </summary>
        /// <param name="position">用于比较距离的世界坐标。</param>
        public static PlayerBehaviour GetNearestAlive(Vector3 position)
        {
            PlayerBehaviour nearest = null;
            float nearestSqr = float.MaxValue;

            for (int i = 0; i < players.Count; i++)
            {
                PlayerBehaviour player = players[i];
                if (!IsCountableAlive(player))
                    continue;

                float sqr = (player.GetPosition() - position).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = player;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 玩家是否可作为存活目标/对局统计：已激活、存活，且联机时不是未生成的场景占位。
        /// </summary>
        /// <param name="player">待检查的玩家。</param>
        public static bool IsCountableAlive(PlayerBehaviour player)
        {
            if (player == null || !player.IsAlive)
                return false;
            if (!player.gameObject.activeInHierarchy)
                return false;

            if (GameNetwork.IsActive)
            {
                NetworkIdentity identity = player.GetComponentInParent<NetworkIdentity>();
                if (identity == null || identity.netId == 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 返回 FullPlayer 根节点；没有父节点时返回角色自身。
        /// </summary>
        /// <param name="player">角色上的 PlayerBehaviour。</param>
        public static GameObject GetPlayerRoot(PlayerBehaviour player)
        {
            if (player == null)
                return null;

            PlayerNetwork network = player.GetComponentInParent<PlayerNetwork>();
            if (network != null)
                return network.gameObject;

            if (player.transform.parent != null)
                return player.transform.parent.gameObject;

            return player.gameObject;
        }

        /// <summary>
        /// 记录当前场景里已经摆好的 FullPlayer，供 Host/Client 启动后关闭。
        /// 包含被 Mirror PostProcess 预禁用的场景实例，但跳过已经分配 netId 的联机玩家。
        /// </summary>
        public static void CaptureScenePlayers()
        {
            scenePlayerRoots.Clear();
            PlayerBehaviour[] found = Object.FindObjectsByType<PlayerBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
            {
                GameObject root = GetPlayerRoot(found[i]);
                if (root == null || scenePlayerRoots.Contains(root))
                    continue;

                if (IsSpawnedNetworkPlayer(root))
                    continue;

                scenePlayerRoots.Add(root);
            }
        }

        /// <summary>
        /// 联机启动后关闭场景里预先摆放的 FullPlayer，避免与 Mirror 生成的玩家重复。
        /// 必须清掉 sceneId，否则随后的 SpawnObjects 会把已关闭的场景实例再激活并同步给客户端。
        /// </summary>
        public static void DisableUnnetworkedPlayers()
        {
            if (scenePlayerRoots.Count == 0)
                CaptureScenePlayers();

            for (int i = scenePlayerRoots.Count - 1; i >= 0; i--)
            {
                GameObject root = scenePlayerRoots[i];
                if (root == null)
                    continue;

                if (IsSpawnedNetworkPlayer(root))
                    continue;

                PlayerBehaviour player = root.GetComponentInChildren<PlayerBehaviour>(true);
                if (player != null)
                {
                    player.PrepareForNetworkReplacement();
                    Unregister(player);
                }

                UnsubscribePlayerUi(root);
                PreventSceneObjectSpawn(root);
                root.SetActive(false);
            }

            scenePlayerRoots.Clear();
        }

        /// <summary>
        /// 单机 Play 时把被 Mirror PostProcess 预禁用的场景 FullPlayer 重新打开。
        /// 联机已启动时不要调用，否则会和 Mirror 生成的玩家重叠。
        /// </summary>
        public static void RestoreScenePlayersForOffline()
        {
            if (GameNetwork.IsActive)
                return;

            if (scenePlayerRoots.Count == 0)
                CaptureScenePlayers();

            GameObject restoredRoot = null;
            for (int i = 0; i < scenePlayerRoots.Count; i++)
            {
                GameObject root = scenePlayerRoots[i];
                if (root == null || IsSpawnedNetworkPlayer(root))
                    continue;

                root.SetActive(true);
                restoredRoot = root;
            }

            if (restoredRoot != null)
                EnsureSingleAudioListener(restoredRoot);
        }

        /// <summary>
        /// 已经由 Mirror 生成并分配 netId 的玩家不能当场景占位关掉。
        /// </summary>
        /// <param name="root">FullPlayer 根节点。</param>
        private static bool IsSpawnedNetworkPlayer(GameObject root)
        {
            NetworkIdentity identity = root.GetComponent<NetworkIdentity>();
            return identity != null && identity.netId != 0;
        }

        /// <summary>
        /// 让场景 FullPlayer 不再被 Mirror 当成可生成的场景物体。
        /// </summary>
        /// <param name="root">FullPlayer 根节点。</param>
        private static void PreventSceneObjectSpawn(GameObject root)
        {
            NetworkIdentity identity = root.GetComponent<NetworkIdentity>();
            if (identity == null)
                return;

            identity.sceneId = 0;
        }

        /// <summary>
        /// 关掉场景/远端玩家 HUD 对全局事件的订阅，避免对未激活物体调 Show。
        /// </summary>
        /// <param name="root">FullPlayer 根节点。</param>
        public static void UnsubscribePlayerUi(GameObject root)
        {
            if (root == null)
                return;

            LightDev.UI.CanvasElement[] elements = root.GetComponentsInChildren<LightDev.UI.CanvasElement>(true);
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i] != null)
                    elements[i].Unsubscribe();
            }
        }

        /// <summary>
        /// 只保留本机玩家根节点上的 AudioListener，关掉场景或其他玩家上的监听器。
        /// </summary>
        /// <param name="keepRoot">本机 FullPlayer 根节点；为空时关掉全部监听器。</param>
        public static void EnsureSingleAudioListener(GameObject keepRoot)
        {
            AudioListener keep = null;
            if (keepRoot != null)
            {
                keep = keepRoot.GetComponentInChildren<AudioListener>(true);
                if (keep != null)
                    keep.enabled = true;
            }

            AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != keep)
                    listeners[i].enabled = false;
            }
        }
    }
}
