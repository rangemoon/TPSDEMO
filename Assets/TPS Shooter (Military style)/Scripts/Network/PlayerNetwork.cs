using Mirror;
using UnityEngine;

namespace TPSShooter
{
    /// <summary>
    /// 挂在 FullPlayer 根节点上：在子物体中查找 PlayerBehaviour，同步角色，并关闭远端的相机/HUD。
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(NetworkTransformUnreliable))]
    [RequireComponent(typeof(NetworkAnimator))]
    public class PlayerNetwork : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnSyncedHpChanged))]
        private float syncedHp = 100f;

        [SyncVar(hook = nameof(OnSyncedAliveChanged))]
        private bool syncedAlive = true;

        private PlayerBehaviour player;

        private void Awake()
        {
            player = ResolvePlayer();
        }

        /// <summary>
        /// 在自身或子物体上查找 PlayerBehaviour（FullPlayer 的角色在子节点 Player 上）。
        /// </summary>
        private PlayerBehaviour ResolvePlayer()
        {
            PlayerBehaviour resolved = GetComponent<PlayerBehaviour>();
            if (resolved == null)
                resolved = GetComponentInChildren<PlayerBehaviour>(true);
            return resolved;
        }

        public override void OnStartServer()
        {
            if (player == null)
                player = ResolvePlayer();

            if (player != null)
            {
                player.ResetHpToFull();
                player.EnableSpawnProtection(3f);
            }

            syncedHp = 100f;
            syncedAlive = true;
        }

        /// <summary>
        /// Host 同步当前血量到所有客户端。
        /// </summary>
        /// <param name="hp">最新血量。</param>
        public void ServerSetHp(float hp)
        {
            if (!isServer)
                return;

            syncedHp = hp;
        }

        /// <summary>
        /// Host 同步死亡状态到所有客户端。
        /// </summary>
        public void ServerNotifyDied()
        {
            if (!isServer)
                return;

            syncedAlive = false;
        }

        /// <summary>
        /// Host 把受击来源发给该玩家本机，用于方向提示。血量仍走 SyncVar。
        /// </summary>
        /// <param name="shooterPosition">攻击来源世界坐标。</param>
        public void ServerNotifyHit(Vector3 shooterPosition)
        {
            if (!isServer)
                return;

            TargetNotifyHit(shooterPosition);
        }

        [TargetRpc]
        private void TargetNotifyHit(Vector3 shooterPosition)
        {
            if (isServer)
                return;

            if (player == null)
                player = ResolvePlayer();
            if (player != null)
                player.PlayNetworkHitFeedback(shooterPosition);
        }

        /// <summary>
        /// 本机开火后同步弹道给其他端。
        /// </summary>
        public void NotifyShotVisual(Vector3 position, Quaternion rotation)
        {
            if (isServer)
                RpcPlayShot(position, rotation);
            else
                CmdPlayShot(position, rotation);
        }

        [Command]
        private void CmdPlayShot(Vector3 position, Quaternion rotation)
        {
            RpcPlayShot(position, rotation);
        }

        [ClientRpc]
        private void RpcPlayShot(Vector3 position, Quaternion rotation)
        {
            if (isOwned)
                return;

            PlayReplicatedShot(position, rotation);
        }

        private void PlayReplicatedShot(Vector3 position, Quaternion rotation)
        {
            if (player == null)
                player = ResolvePlayer();
            if (player == null || player.CurrentWeaponBehaviour == null)
                return;

            player.CurrentWeaponBehaviour.PlayReplicatedShot(position, rotation);
        }

        /// <summary>
        /// SyncVar 回调：把 Host 血量应用到客户端玩家。
        /// </summary>
        /// <param name="oldHp">同步前的血量。</param>
        /// <param name="newHp">同步后的血量。</param>
        private void OnSyncedHpChanged(float oldHp, float newHp)
        {
            if (isServer || player == null)
                return;

            // 与 syncedAlive 对照：仍存活时忽略 0 血的初始脏数据
            if (newHp <= 0 && syncedAlive)
                return;

            player.ApplyNetworkHp(newHp);
        }

        /// <summary>
        /// SyncVar 回调：Host 判定死亡后，客户端播放同一套死亡流程。
        /// </summary>
        /// <param name="oldAlive">同步前是否存活。</param>
        /// <param name="newAlive">同步后是否存活。</param>
        private void OnSyncedAliveChanged(bool oldAlive, bool newAlive)
        {
            if (isServer || player == null || newAlive)
                return;

            if (player.IsAlive)
                player.Die();
        }

        public override void OnStartClient()
        {
            if (player == null)
                player = ResolvePlayer();

            ConfigureTransformAndAnimator();
            PlayerRegistry.Register(player);

            if (!isLocalPlayer)
                DisableRemotePresentation();
            else if (player != null)
            {
                Animator animator = player.GetComponent<Animator>();
                if (animator != null)
                    animator.applyRootMotion = player.movementSettings.ApplyRootMotion;
            }
        }

        public override void OnStartLocalPlayer()
        {
            if (player == null)
                player = ResolvePlayer();

            ConfigureTransformAndAnimator();
            PlayerRegistry.SetLocal(player);
            EnableLocalPresentation();
            if (player != null)
            {
                player.EnableLocalControl();
                PlayerRegistry.EnsureSingleAudioListener(gameObject);
            }

            ShowLocalHud();
        }

        /// <summary>
        /// 联机生成晚于 SceneLoaded 事件，本地玩家的 HUD 元素（准星/受击/血条等）会错过初始 Show，这里补齐。
        /// </summary>
        private void ShowLocalHud()
        {
            LightDev.UI.CanvasElement[] elements = GetComponentsInChildren<LightDev.UI.CanvasElement>(true);
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i] != null)
                    elements[i].ShowForLateSpawn();
            }

            // 场景里的 PlayerCanvas 在 Mirror 切场景时可能已错过 SceneLoaded，加入端要补一次
            LightDev.UI.CanvasManager.ShowLateJoinHud();
        }

        public override void OnStopClient()
        {
            if (player == null)
                return;

            if (isLocalPlayer)
                player.DisableLocalControl();

            PlayerRegistry.Unregister(player);
        }

        /// <summary>
        /// 把位移同步目标指到角色子物体，并把动画器绑到该角色。
        /// </summary>
        private void ConfigureTransformAndAnimator()
        {
            if (player == null)
                return;

            NetworkTransformUnreliable networkTransform = GetComponent<NetworkTransformUnreliable>();
            if (networkTransform != null)
            {
                networkTransform.syncDirection = SyncDirection.ClientToServer;
                networkTransform.coordinateSpace = CoordinateSpace.World;
                if (networkTransform.target == null)
                    networkTransform.target = player.transform;
            }

            NetworkAnimator networkAnimator = GetComponent<NetworkAnimator>();
            if (networkAnimator == null)
                return;

            networkAnimator.clientAuthority = true;
            if (networkAnimator.animator == null)
                networkAnimator.animator = player.GetComponent<Animator>();
        }

        /// <summary>
        /// 本机玩家若在 OnStartClient 里被当成远端关掉了 Canvas/相机，这里重新打开。
        /// </summary>
        private void EnableLocalPresentation()
        {
            Camera[] cameras = GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
                cameras[i].enabled = true;

            TPSCamera[] tpsCameras = GetComponentsInChildren<TPSCamera>(true);
            for (int i = 0; i < tpsCameras.Length; i++)
                tpsCameras[i].enabled = true;

            Canvas[] canvases = GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
                canvases[i].gameObject.SetActive(true);

            DesktopInput[] desktopInputs = GetComponentsInChildren<DesktopInput>(true);
            for (int i = 0; i < desktopInputs.Length; i++)
                desktopInputs[i].enabled = true;
        }

        /// <summary>
        /// 关闭远端 FullPlayer 上的本机专用部件，避免第二套相机、HUD 和输入。
        /// </summary>
        private void DisableRemotePresentation()
        {
            if (player != null)
            {
                CharacterController characterController = player.GetComponent<CharacterController>();
                if (characterController != null)
                {
                    characterController.enabled = false;
                    AddRemoteHitCollider(characterController);
                }

                Animator animator = player.GetComponent<Animator>();
                if (animator != null)
                    animator.applyRootMotion = false;
            }

            PlayerRegistry.UnsubscribePlayerUi(gameObject);

            Camera[] cameras = GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
                cameras[i].enabled = false;

            AudioListener[] listeners = GetComponentsInChildren<AudioListener>(true);
            for (int i = 0; i < listeners.Length; i++)
                listeners[i].enabled = false;

            TPSCamera[] tpsCameras = GetComponentsInChildren<TPSCamera>(true);
            for (int i = 0; i < tpsCameras.Length; i++)
                tpsCameras[i].enabled = false;

            Canvas[] canvases = GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
                canvases[i].gameObject.SetActive(false);

            DesktopInput[] desktopInputs = GetComponentsInChildren<DesktopInput>(true);
            for (int i = 0; i < desktopInputs.Length; i++)
                desktopInputs[i].enabled = false;
        }

        /// <summary>
        /// 远端玩家的 CharacterController 禁用后没有碰撞体，敌人子弹与近战会打不中它。
        /// 补一个等大的胶囊碰撞体（kinematic，不参与本地物理模拟）专门用于被弹检测。
        /// </summary>
        /// <param name="source">被禁用的 CharacterController，用于复制胶囊尺寸。</param>
        private void AddRemoteHitCollider(CharacterController source)
        {
            if (source.GetComponent<CapsuleCollider>() != null)
                return;

            CapsuleCollider hitCollider = source.gameObject.AddComponent<CapsuleCollider>();
            hitCollider.center = source.center;
            hitCollider.height = source.height;
            hitCollider.radius = source.radius;

            Rigidbody hitBody = source.GetComponent<Rigidbody>();
            if (hitBody == null)
                hitBody = source.gameObject.AddComponent<Rigidbody>();
            hitBody.isKinematic = true;
            hitBody.useGravity = false;
        }
    }
}
