using System.Collections.Generic;
using UnityEngine;

using LightDev;
using LightDev.Core;

using DG.Tweening;

namespace TPSShooter
{
    [RequireComponent(typeof(FootstepSounds))]
    [RequireComponent(typeof(PathAgent))]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Animator))]
    public partial class EnemyBehaviour : Base
    {
        public EnemyVisionSettings VisionSettings = new EnemyVisionSettings();
        public EnemyPatrollingSettings PatrollingSettings = new EnemyPatrollingSettings();
        public EnemyWeaponSettings WeaponSettings = new EnemyWeaponSettings();
        public EnemyDeathSettings DeathSettings = new EnemyDeathSettings();
        public EnemyAIBehaviour AI_Behaviour = new EnemyAIBehaviour();
        public EnemySettingsIK SettingsIK = new EnemySettingsIK();
        private EnemyAnimationParameters AnimatorParameters = new EnemyAnimationParameters();

        [Space]
        [Tooltip("Distance when Enemy starts attacking player.")]
        public float InnerAttackRadius = 35;
        [Tooltip("Distance when Enemy stop attacking player.")]
        public float OuterAttackRadius = 40f;
        public float MaxPlayerDetectionRadius = 150;

        [Space]
        [Tooltip("Pooled blood effect prefab (Particle System). Leave empty to disable.")]
        public GameObject BloodEffectPrefab;

        private CharacterController characterController;
        private Animator animator;
        private PathAgent pathAgent;
        private PlayerBehaviour player;
        private Rigidbody[] cachedRigidbodies;

        private EnemyBehaviourState currentState;
        private IdleState idleState;
        private PatrolState patrolState;
        private SearchState searchState;
        private ChaseState chaseState;
        private AttackState attackState;
        private DeathState deathState;
        private PlayerDiedState playerDiedState;

        private float hp = 100f;
        private bool updateSpineIK = false;
        private Vector3 spineIKLookAt;

        private const float MaxRunSpeed = 5;
        private const float MaxRunAcceleration = 20;

        private const string forwardAnimParamSequenceID = "f";
        private const string strafeAnimParamSequenceID = "s";

        public event System.Action onHpChanged;
        public event System.Action onDied;

        public float GetHP() { return hp; }
        public bool IsAlive() { return currentState != deathState; }

        private void OnValidate()
        {
            const string timeBeforeShotError = "TimeBeforeShot has to be more or equal 0 and less than EnemyWeapon.shootFrequency. It is a delay that used to stop Enemy from tracking player before shoot.";
            if (WeaponSettings.TimeBeforeShot < 0)
            {
                WeaponSettings.TimeBeforeShot = 0;
                Debug.LogError(timeBeforeShotError);
            }
            else if (WeaponSettings.Weapon && WeaponSettings.TimeBeforeShot > WeaponSettings.Weapon.ShootFrequency)
            {
                WeaponSettings.TimeBeforeShot = WeaponSettings.Weapon.ShootFrequency;
                Debug.LogError(timeBeforeShotError);
            }

            if (InnerAttackRadius < 0)
            {
                InnerAttackRadius = 0;
                Debug.LogError("InnerAttackRadius must be more than 0.");
            }

            if (OuterAttackRadius < 0)
            {
                OuterAttackRadius = 0;
                Debug.LogError("OuterAttackRadius must be more than 0.");
            }


            if (InnerAttackRadius >= OuterAttackRadius)
            {
                InnerAttackRadius = OuterAttackRadius;
                OuterAttackRadius += 0.1f;
                Debug.LogError("InnerAttackRadius must be less than OuterAttackRadius.");
            }

            if (MaxPlayerDetectionRadius <= InnerAttackRadius || MaxPlayerDetectionRadius <= OuterAttackRadius)
            {
                MaxPlayerDetectionRadius = Mathf.Max(InnerAttackRadius, OuterAttackRadius) + 0.1f;
                Debug.LogError("MaxPlayerDetectionRadius must be more than InnerAttackRadius and OuterAttackRadius.");
            }
        }

        private void Start()
        {
            EnsureAIInitialized();
            RefreshCombatTarget();
            InitializeStartState();

            EnemyRegistry.Register(this);
            Events.EnemyCreated.Call(this);
            Events.AnyPlayerDied += OnAnyPlayerDied;
        }

        private void OnDestroy()
        {
            Events.AnyPlayerDied -= OnAnyPlayerDied;
            EnemyRegistry.Unregister(this);
        }

        private void Update()
        {
            if (GameManager.IsGamePaused || GameManager.IsGameFinished)
                return;
            if (GameNetwork.IsClientOnly)
                return;

            if (Time.frameCount % 15 == 0)
                RefreshCombatTarget();

            if (currentState != null)
                currentState.OnUpdate();
            UpdateGravity();
        }

        private void LateUpdate()
        {
            UpdateSpineIK();
        }

        /// <summary>
        /// 有玩家死亡时重新选目标；全灭才进入停止战斗状态。
        /// </summary>
        /// <param name="deadPlayer">刚刚死亡的玩家。</param>
        private void OnAnyPlayerDied(PlayerBehaviour deadPlayer)
        {
            if (currentState == deathState) return;

            if (!PlayerRegistry.HasAlivePlayer())
            {
                ChangeState(playerDiedState);
                return;
            }

            if (player == null || player == deadPlayer || !PlayerRegistry.IsCountableAlive(player))
                RefreshCombatTarget();
        }

        /// <summary>
        /// 在全部存活玩家中选仇恨：优先感知范围内可见/可听的最近目标，否则回退到最近存活玩家。
        /// </summary>
        private void RefreshCombatTarget()
        {
            const float currentTargetStickiness = 0.85f;

            PlayerBehaviour bestDetected = null;
            float bestDetectedSqr = float.MaxValue;
            PlayerBehaviour nearest = null;
            float nearestSqr = float.MaxValue;
            Vector3 selfPos = GetPosition();
            float maxDetectSqr = MaxPlayerDetectionRadius * MaxPlayerDetectionRadius;

            IReadOnlyList<PlayerBehaviour> players = PlayerRegistry.GetPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                PlayerBehaviour candidate = players[i];
                if (!PlayerRegistry.IsCountableAlive(candidate))
                    continue;

                float sqr = (candidate.GetPosition() - selfPos).sqrMagnitude;
                float compareSqr = sqr;
                if (candidate == player)
                    compareSqr *= currentTargetStickiness;

                if (compareSqr < nearestSqr)
                {
                    nearestSqr = compareSqr;
                    nearest = candidate;
                }

                if (sqr > maxDetectSqr)
                    continue;

                if (!IsPlayerDetected(candidate))
                    continue;

                if (compareSqr < bestDetectedSqr)
                {
                    bestDetectedSqr = compareSqr;
                    bestDetected = candidate;
                }
            }

            player = bestDetected != null ? bestDetected : nearest;
        }

        /// <summary>
        /// 当前是否有可攻击的存活玩家。
        /// </summary>
        private bool HasCombatTarget()
        {
            return PlayerRegistry.IsCountableAlive(player);
        }

        /// <summary>
        /// 指定玩家是否在 FOV/噪声感知下且视线可达。
        /// </summary>
        /// <param name="target">待检测玩家。</param>
        private bool IsPlayerDetected(PlayerBehaviour target)
        {
            if (target == null)
                return false;

            return (IsPlayerInFieldOfView(target) || IsPlayerNoiseDetected(target))
                && IsPlayerNoticedByRaycast(target);
        }

        /// <summary>
        /// 应用由网络同步过来的血量；降到 0 时进入死亡。
        /// </summary>
        /// <param name="networkHp">Host 同步的当前血量。</param>
        public void ApplyNetworkHp(float networkHp)
        {
            EnsureAIInitialized();
            hp = networkHp;
            onHpChanged?.Invoke();

            if (hp <= 0)
                ApplyNetworkDeath();
        }

        /// <summary>
        /// 客户端按 Host 通知进入死亡状态。
        /// </summary>
        public void ApplyNetworkDeath()
        {
            EnsureAIInitialized();
            if (currentState == deathState)
                return;

            ChangeState(deathState);
        }

        /// <summary>
        /// 死亡动画播完后销毁。加入端不能本地 Destroy 带 NetworkIdentity 的敌人，否则 Host 已死的怪还会留在客户端。
        /// </summary>
        /// <param name="delay">延迟秒数。</param>
        internal void ScheduleDespawn(float delay)
        {
            if (GameNetwork.IsClientOnly)
                return;

            Destroy(gameObject, delay);
        }

        private void UpdateGravity()
        {
            if (!characterController.enabled || characterController.isGrounded) return;

            Vector3 gravity = Vector3.zero;
            gravity.y = characterController.isGrounded ? -1 : Physics.gravity.y * Time.deltaTime;
            gravity.y = gravity.y - Mathf.Min(0, characterController.velocity.y);
            characterController.Move(gravity * Time.deltaTime);
        }

        private void UpdateSpineIK()
        {
            if (updateSpineIK)
            {
                Quaternion startRotation = SettingsIK.Spine.rotation;
                SettingsIK.Spine.LookAt(spineIKLookAt);
                SettingsIK.Spine.Rotate(SettingsIK.SpineRotation);
            }
        }

        public void OnVehicleCollision()
        {
            if (HasCombatTarget() && player.IsDrivingVehicle)
            {
                ChangeState(deathState);
            }
        }

        /// <summary>
        /// Host 结算子弹伤害并在血量耗尽时死亡。
        /// </summary>
        /// <param name="damage">已经乘过部位倍率的最终伤害。</param>
        public void ApplyServerDamage(float damage)
        {
            if (GameNetwork.IsActive && !GameNetwork.IsServer) return;
            if (currentState == deathState) return;

            hp -= damage;
            onHpChanged?.Invoke();

            if (hp <= 0)
                ChangeState(deathState);
        }

        /// <summary>
        /// Host 结算手雷击杀。
        /// </summary>
        public void ApplyServerGrenadeKill()
        {
            if (GameNetwork.IsActive && !GameNetwork.IsServer) return;
            if (currentState == deathState) return;

            ChangeState(deathState);
        }

        public void OnBulletHit(PlayerBullet bullet, float damageMultiplier)
        {
            if (GameNetwork.IsClientOnly)
            {
                EnemyNetwork enemyNetwork = GetComponent<EnemyNetwork>();
                if (enemyNetwork != null)
                    enemyNetwork.CmdApplyBulletDamage(bullet.damage * damageMultiplier);
                return;
            }
            if (currentState == deathState) return;

            hp -= bullet.damage * damageMultiplier;
            onHpChanged?.Invoke();

#if !MINIGAME_OPPO
            if (BloodEffectPrefab != null)
            {
                GameObject blood = GamePool.Spawn(BloodEffectPrefab, bullet.transform.position, Quaternion.identity);
                var lifetime = blood.GetComponent<PooledLifetime>();
                if (lifetime == null) lifetime = blood.AddComponent<PooledLifetime>();
                var ps = blood.GetComponent<ParticleSystem>();
                if (ps != null)
                    lifetime.delay = ps.main.duration;
            }
#endif

            if (hp <= 0)
            {
                ChangeState(deathState);
            }
        }

        public void OnGrenadeHit(AbstractGrenade grenade)
        {
            if (GameNetwork.IsClientOnly)
            {
                EnemyNetwork enemyNetwork = GetComponent<EnemyNetwork>();
                if (enemyNetwork != null)
                    enemyNetwork.CmdKillByGrenade();
                return;
            }

            ApplyServerGrenadeKill();
        }

        /// <summary>
        /// 同步血量可能早于 Start：先建好状态机，避免加入端还没初始化就被写成 Idle。
        /// </summary>
        private void EnsureAIInitialized()
        {
            if (deathState != null)
                return;

            idleState = new IdleState(this);
            patrolState = new PatrolState(this);
            searchState = new SearchState(this);
            chaseState = new ChaseState(this);
            attackState = (AI_Behaviour.AttackMotion == AttackMotion.None) ? new AttackState(this) : new StrafeAttackState(this);
            deathState = new DeathState(this);
            playerDiedState = new PlayerDiedState(this);

            animator = GetComponent<Animator>();
            characterController = GetComponent<CharacterController>();
            pathAgent = GetComponent<PathAgent>();
            if (pathAgent == null)
                pathAgent = gameObject.AddComponent<PathAgent>();
            if (animator != null)
                animator.applyRootMotion = false;
            if (pathAgent != null)
                pathAgent.autoBraking = false;

            cachedRigidbodies = GetComponentsInChildren<Rigidbody>();
            if (cachedRigidbodies != null)
            {
                foreach (Rigidbody b in cachedRigidbodies)
                    b.isKinematic = true;
            }
        }

        private void InitializeStartState()
        {
            EnsureAIInitialized();
            if (currentState == deathState)
                return;

            if (hp <= 0)
            {
                currentState = deathState;
                currentState.OnEnter();
                return;
            }

            if (HasWaypoints())
            {
                currentState = patrolState;
            }
            else
            {
                currentState = idleState;
            }
            currentState.OnEnter();
        }

        private void ChangeState(EnemyBehaviourState state)
        {
            if (state == null || currentState == state)
                return;

            if (currentState != null)
                currentState.OnExit();
            currentState = state;
            currentState.OnEnter();
        }

        private void LookAtLerp(Vector3 lookAt, float lerp = 5)
        {
            Quaternion previous = transform.rotation;
            transform.LookAt(lookAt);
            transform.rotation = Quaternion.Lerp(previous, transform.rotation, lerp * Time.deltaTime);
        }

        private void SetForwardAnimatorParameter(float value)
        {
            KillSequence(forwardAnimParamSequenceID);
            Sequence(
              DOTween.To((v) =>
              {
                  animator.SetFloat(AnimatorParameters.ForwardHash, v);
              }, animator.GetFloat(AnimatorParameters.ForwardHash), value, 0.3f)
            ).stringId = forwardAnimParamSequenceID;
        }

        private void SetStrafeAnimatorParameter(float value)
        {
            KillSequence(strafeAnimParamSequenceID);
            Sequence(
              DOTween.To((v) =>
              {
                  animator.SetFloat(AnimatorParameters.StrafeHash, v);
              }, animator.GetFloat(AnimatorParameters.StrafeHash), value, 0.3f)
            ).stringId = strafeAnimParamSequenceID;
        }

        private void StopNavMeshAgent()
        {
            if (pathAgent != null)
                pathAgent.StopIfReady();
        }

        private void ResumeNavMeshAgent()
        {
            if (pathAgent != null)
                pathAgent.ResumeIfReady();
        }

        private float GetDistanceToPlayer()
        {
            if (!HasCombatTarget())
                return float.MaxValue;

            return Vector3.Distance(GetPosition(), player.GetPosition());
        }

        private bool HasWaypoints()
        {
            return PatrollingSettings.Waypoints.Length != 0;
        }

        private bool cachedIsPlayerRaycasted;
        private int cachedRaycastFrame = -1;
        private RaycastHit playerRaycastHit;
        private Vector3 playerPos;
        private Vector3 visionPos;
        private bool warnedVisionPositionMissing;

        private bool IsPlayerNoticedByRaycast()
        {
            if (!HasCombatTarget())
                return false;

            if (cachedRaycastFrame == Time.frameCount)
                return cachedIsPlayerRaycasted;

            cachedRaycastFrame = Time.frameCount;
            cachedIsPlayerRaycasted = IsPlayerNoticedByRaycast(player);
            return cachedIsPlayerRaycasted;
        }

        /// <summary>
        /// 对指定玩家做视线检测（不走当前目标缓存）。
        /// </summary>
        /// <param name="target">待检测玩家。</param>
        private bool IsPlayerNoticedByRaycast(PlayerBehaviour target)
        {
            if (target == null)
                return false;

            playerPos = target.GetPosition() + new Vector3(0, 1, 0);
            if (VisionSettings.VisionPosition == null)
            {
                if (!warnedVisionPositionMissing)
                {
                    warnedVisionPositionMissing = true;
                    Debug.LogWarning(name + ": VisionSettings.VisionPosition 未配置，视线检测退化为自身位置。请在预制体 Inspector 中重新绑定。", this);
                }
                visionPos = transform.position + new Vector3(0, 1, 0);
            }
            else
            {
                visionPos = VisionSettings.VisionPosition.position;
            }

            if (!Physics.Linecast(visionPos, playerPos, out playerRaycastHit, VisionSettings.VisionLayers))
                return false;

            if (playerRaycastHit.collider.GetComponentInParent<PlayerBehaviour>() != null)
                return true;

            return playerRaycastHit.collider.gameObject.GetComponentInParent<Vehicle>() != null && target.IsDrivingVehicle;
        }

        private bool IsPlayerNoiseDetected()
        {
            if (!HasCombatTarget())
                return false;

            return IsPlayerNoiseDetected(player);
        }

        private bool IsPlayerNoiseDetected(PlayerBehaviour target)
        {
            if (target == null)
                return false;

            return target.Noise > Vector3.Distance(transform.position, target.GetPosition());
        }

        private bool cachedIsPlayerInFOV;
        private int cachedFOVFrame = -1;

        private bool IsPlayerInFieldOfView()
        {
            if (!HasCombatTarget())
                return false;

            if (cachedFOVFrame == Time.frameCount)
                return cachedIsPlayerInFOV;

            cachedFOVFrame = Time.frameCount;
            cachedIsPlayerInFOV = IsPlayerInFieldOfView(player);
            return cachedIsPlayerInFOV;
        }

        private bool IsPlayerInFieldOfView(PlayerBehaviour target)
        {
            if (target == null)
                return false;

            Vector3 targetDir = target.GetPosition() - transform.position;
            float angle = Vector3.Angle(targetDir, transform.forward);
            return Mathf.Abs(angle) <= VisionSettings.fov;
        }

        private bool CanChangeStateToSearch()
        {
            if (IsPlayerNoticedByRaycast()) return false;
            if (!IsPlayerNoiseDetected()) return false;
            if (GetDistanceToPlayer() > MaxPlayerDetectionRadius) return false;
            if (AI_Behaviour.SearchSettings != SearchSettings.Search) return false;

            return true;
        }

        private bool CanChangeStateToChase()
        {
            if (!IsPlayerInFieldOfView() && !IsPlayerNoiseDetected()) return false;
            if (!IsPlayerNoticedByRaycast()) return false;
            if (GetDistanceToPlayer() < OuterAttackRadius) return false;
            if (GetDistanceToPlayer() > MaxPlayerDetectionRadius) return false;
            if (AI_Behaviour.SearchSettings != SearchSettings.Search) return false;

            return true;
        }

        private bool CanChangeStateToAttack()
        {
            if (!IsPlayerInFieldOfView() && !IsPlayerNoiseDetected()) return false;
            if (!IsPlayerNoticedByRaycast()) return false;
            if (GetDistanceToPlayer() > InnerAttackRadius) return false;

            return true;
        }

        #region AdditionalClasses

        public class EnemyBehaviourDebugFriend
        {
            private EnemyBehaviour enemy;

            public EnemyBehaviourDebugFriend(EnemyBehaviour enemy)
            {
                this.enemy = enemy;
            }

            public Vector3 GetVisionPos() { return enemy.visionPos; }
            public Vector3 GetPlayerVisionPos() { return enemy.playerPos; }
            public RaycastHit GetHitPos() { return enemy.playerRaycastHit; }
            public bool IsAttacking() { return enemy.currentState == enemy.attackState; }
        }

        [System.Serializable]
        public class EnemyAnimationParameters
        {
            public string ForwardHash = "Forward";
            public string StrafeHash = "Strafe";
            public string DieHash = "Die";
        }

        [System.Serializable]
        public class EnemyVisionSettings
        {
            public float fov = 80;
            public Transform VisionPosition;
            public LayerMask VisionLayers = 1 << 0;
        }

        [System.Serializable]
        public class EnemyPatrollingSettings
        {
            public WaypointBase[] Waypoints;
        }

        [System.Serializable]
        public class WaypointBase
        {
            public Transform Destination;
            // how much time the enemy will be at this position before going on another position
            public float WaitTime;
        }

        [System.Serializable]
        public class EnemyWeaponSettings
        {
            public EnemyWeapon Weapon;
            [Tooltip("Varible has to be less than WeaponBahaviour.ShootFrequency. This variable show how much time enemy will not be looking at the player before shooting at him")]
            public float TimeBeforeShot = 0.1f;
        }

        [System.Serializable]
        public class EnemyDeathSettings
        {
            public bool IsRagdolled;
            public float EnemyDieTime = 20f;
            [Header("Items that will be unattached when enemy dies")]
            public Transform[] Items;
        }

        [System.Serializable]
        public class EnemySettingsIK
        {
            public Transform Spine;
            public Vector3 SpineRotation;
        }

        [System.Serializable]
        public class EnemyAIBehaviour
        {
            public AttackMotion AttackMotion = AttackMotion.None;
            public SearchSettings SearchSettings = SearchSettings.Search;
        }

        /// <summary>
        /// Defines whether Enemy would move in Attack state.
        /// </summary>
        [System.Serializable]
        public enum AttackMotion
        {
            None, Strafe
        }

        /// <summary>
        /// 1) Search: means that Enemy could go to states Chase and Search.
        /// After Chase/Search/Attack states Enemy would go to Idle state.
        ///
        /// 2) None: means that Enemy could not go to states Chase and Search.
        /// After Attack state Enemy would go to Patrol or Idle state depending on whether Enemy has waypoints.
        /// </summary>
        [System.Serializable]
        public enum SearchSettings
        {
            None, Search
        }

        #endregion
    }
}
