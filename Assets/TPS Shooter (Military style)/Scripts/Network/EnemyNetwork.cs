using Mirror;
using UnityEngine;
using UnityEngine.AI;

namespace TPSShooter
{
    /// <summary>
    /// 士兵敌人网络组件：Host 跑 AI，客户端关闭本地 AI 并同步血量/死亡。
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(NetworkTransformUnreliable))]
    [RequireComponent(typeof(NetworkAnimator))]
    [RequireComponent(typeof(EnemyBehaviour))]
    public class EnemyNetwork : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnSyncedHpChanged))]
        private float syncedHp = 100f;

        private EnemyBehaviour enemy;

        private void Awake()
        {
            enemy = GetComponent<EnemyBehaviour>();
            enemy.onHpChanged += OnEnemyHpChanged;
        }

        private void OnDestroy()
        {
            if (enemy != null)
                enemy.onHpChanged -= OnEnemyHpChanged;
        }

        public override void OnStartServer()
        {
            NetworkAnimator networkAnimator = GetComponent<NetworkAnimator>();
            if (networkAnimator != null && networkAnimator.animator == null)
                networkAnimator.animator = GetComponent<Animator>();
        }

        public override void OnStartClient()
        {
            if (isServer)
                return;

            DisableClientSimulation();
        }

        /// <summary>
        /// 客户端上报伤害，由 Host 结算。敌人不归客户端所有，因此不要求 authority。
        /// </summary>
        /// <param name="damage">已经乘过部位倍率的最终伤害。</param>
        [Command(requiresAuthority = false)]
        public void CmdApplyBulletDamage(float damage)
        {
            if (enemy != null)
                enemy.ApplyServerDamage(damage);
        }

        /// <summary>
        /// 客户端上报手雷命中，由 Host 直接击杀。
        /// </summary>
        [Command(requiresAuthority = false)]
        public void CmdKillByGrenade()
        {
            if (enemy != null)
                enemy.ApplyServerGrenadeKill();
        }

        /// <summary>
        /// 客户端关闭 NavMesh 与 CharacterController，位置交给 NetworkTransform。
        /// </summary>
        private void DisableClientSimulation()
        {
            NavMeshAgent agent = GetComponent<NavMeshAgent>();
            if (agent != null)
                agent.enabled = false;

            CharacterController characterController = GetComponent<CharacterController>();
            if (characterController != null)
                characterController.enabled = false;
        }

        /// <summary>
        /// Host 血量变化时写入 SyncVar，供其他端显示与播死亡。
        /// </summary>
        private void OnEnemyHpChanged()
        {
            if (!isServer || enemy == null)
                return;

            syncedHp = enemy.GetHP();
        }

        /// <summary>
        /// SyncVar 回调：把 Host 血量应用到客户端敌人。
        /// </summary>
        /// <param name="oldHp">同步前的血量。</param>
        /// <param name="newHp">同步后的血量。</param>
        private void OnSyncedHpChanged(float oldHp, float newHp)
        {
            if (isServer || enemy == null)
                return;

            enemy.ApplyNetworkHp(newHp);
        }
    }
}
