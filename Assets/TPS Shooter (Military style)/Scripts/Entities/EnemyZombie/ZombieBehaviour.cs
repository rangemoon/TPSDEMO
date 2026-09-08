using System.Collections.Generic;
using UnityEngine.AI;
using UnityEngine;

using LightDev;
using LightDev.Core;

using DG.Tweening;

namespace TPSShooter
{
  [RequireComponent(typeof(NavMeshAgent))]
  [RequireComponent(typeof(CharacterController))]
  [RequireComponent(typeof(Animator))]
  public partial class ZombieBehaviour : Base
  {
    [Header("- PLAYER DETECTION -")]
    public float fov = 60;
    public float playerDetectionRadius = 75;
    public Transform visionPosition;
    public LayerMask visionLayers = 1 << 0;

    [Header("- ATTACK SETTINGS -")]
    public float damage = 5;
    public float AttackSphereRadius = 0.3f;
    public Vector3 AttackSphereOffset = new Vector3(0, 1f, 1f);

    [Header("- PATROLLING SETTINGS -")]
    public Waypoint[] waypoints;

    [Header("- VISUAL EFFECTS -")]
    [Tooltip("Pooled blood effect prefab (Particle System). Leave empty to disable.")]
    public GameObject bloodEffectPrefab;

    [Header("- DEATH -")]
    public float dieTime = 20f;

    private CharacterController characterController;
    private Animator animator;
    private NavMeshAgent navmeshAgent;
    private PlayerBehaviour player;

    private ZombieBehaviourState currentState;
    private IdleState idleState;
    private PatrolState patrolState;
    private SearchState searchState;
    private ChaseState chaseState;
    private AttackState attackState;
    private DeathState deathState;
    private PlayerDiedState playerDiedState;

    private float hp = 100f;

    private static readonly int WalkHash = Animator.StringToHash("Walk");
    private static readonly int RunHash = Animator.StringToHash("Run");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int DeathHash = Animator.StringToHash("Death");

    private const string forwardAnimParamSequenceID = "f";

    public const float MaxWalkSpeed = 2;
    public const float MaxRunSpeed = 3;

    public const float InnerAttackRadius = 1;
    public const float OuterAttackRadius = 2;

    public event System.Action onHpChanged;
    public event System.Action onDied;

    public float GetHP() { return hp; }
    public bool IsAlive() { return currentState != deathState; }

    private void OnValidate()
    {
      playerDetectionRadius = Mathf.Max(0, playerDetectionRadius);
      dieTime = Mathf.Max(0, dieTime);
    }

    private void Awake()
    {
      Events.AnyPlayerDied += OnAnyPlayerDied;
    }

    private void Start()
    {
      EnsureAIInitialized();
      RefreshCombatTarget();
      InitializeStartState();

      EnemyRegistry.Register(this);
      Events.ZombieCreated.Call(this);
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

    private void OnAttack() // Animation Event
    {
      // 动画事件在客户端也会触发；伤害只由 Host 结算，避免客户端本地扣血导致两端状态不一致
      if (GameNetwork.IsClientOnly)
        return;

      Collider[] colliders = Physics.OverlapSphere(
        transform.position + transform.rotation * AttackSphereOffset,
        AttackSphereRadius,
        LayerMask.GetMask(Layers.Player)
      );

      // 打中谁扣谁；同一玩家的多个碰撞体只结算一次
      for (int i = 0; i < colliders.Length; i++)
      {
        PlayerBehaviour hitPlayer = colliders[i].GetComponentInParent<PlayerBehaviour>();
        if (hitPlayer == null || !PlayerRegistry.IsCountableAlive(hitPlayer))
          continue;

        hitPlayer.OnZombieHit(this);
        break;
      }
    }

    void OnDrawGizmosSelected()
    {
      Gizmos.color = UnityEngine.Color.yellow;
      Gizmos.DrawSphere(transform.position + transform.rotation * AttackSphereOffset, AttackSphereRadius);
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
      float maxDetectSqr = playerDetectionRadius * playerDetectionRadius;

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
    /// 死亡动画播完后销毁。加入端不能本地 Destroy 联网僵尸。
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
        ZombieNetwork zombieNetwork = GetComponent<ZombieNetwork>();
        if (zombieNetwork != null)
          zombieNetwork.CmdApplyBulletDamage(bullet.damage * damageMultiplier);
        return;
      }
      if (currentState == deathState) return;

      hp -= bullet.damage * damageMultiplier;
      onHpChanged?.Invoke();

#if !MINIGAME_OPPO
      if (bloodEffectPrefab != null)
      {
        GameObject blood = GamePool.Spawn(bloodEffectPrefab, bullet.transform.position, Quaternion.identity);
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
        ZombieNetwork zombieNetwork = GetComponent<ZombieNetwork>();
        if (zombieNetwork != null)
          zombieNetwork.CmdKillByGrenade();
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
      attackState = new AttackState(this);
      deathState = new DeathState(this);
      playerDiedState = new PlayerDiedState(this);

      animator = GetComponent<Animator>();
      characterController = GetComponent<CharacterController>();
      navmeshAgent = GetComponent<NavMeshAgent>();
      if (animator != null)
        animator.applyRootMotion = false;
      if (navmeshAgent != null)
        navmeshAgent.autoBraking = false;
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

    private void ChangeState(ZombieBehaviourState state)
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

    private void StopNavMeshAgent()
    {
      NavMeshAgentUtil.StopIfReady(navmeshAgent);
    }

    private void ResumeNavMeshAgent()
    {
      NavMeshAgentUtil.ResumeIfReady(navmeshAgent);
    }

    private float GetDistanceToPlayer()
    {
      if (!HasCombatTarget())
        return float.MaxValue;

      return Vector3.Distance(GetPosition(), player.GetPosition());
    }

    private bool HasWaypoints()
    {
      return waypoints.Length != 0;
    }

    private bool cachedIsPlayerRaycasted;
    private int cachedRaycastFrame = -1;
    private Vector3 visionPos;
    private Vector3 playerPos;
    private RaycastHit playerRaycastHit;
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

      if (visionPosition == null)
      {
        if (!warnedVisionPositionMissing)
        {
          warnedVisionPositionMissing = true;
          Debug.LogWarning(name + ": visionPosition 未配置，视线检测退化为自身位置。请在预制体 Inspector 中重新绑定。", this);
        }
        visionPos = transform.position + new Vector3(0, 1, 0);
      }
      else
      {
        visionPos = visionPosition.position;
      }

      playerPos = target.GetPosition() + new Vector3(0, 1, 0);
      if (!Physics.Linecast(visionPos, playerPos, out playerRaycastHit, visionLayers))
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
      return Mathf.Abs(angle) <= fov;
    }

    private bool CanChangeStateToSearch()
    {
      if (IsPlayerNoticedByRaycast()) return false;
      if (!IsPlayerNoiseDetected()) return false;

      return true;
    }

    private bool CanChangeStateToChase()
    {
      if (!IsPlayerInFieldOfView() && !IsPlayerNoiseDetected()) return false;
      if (!IsPlayerNoticedByRaycast()) return false;
      if (GetDistanceToPlayer() < OuterAttackRadius) return false;
      if (GetDistanceToPlayer() > playerDetectionRadius) return false;

      return true;
    }

    private bool CanChangeStateToAttack()
    {
      if (!IsPlayerInFieldOfView() && !IsPlayerNoiseDetected()) return false;
      if (!IsPlayerNoticedByRaycast()) return false;
      if (GetDistanceToPlayer() > InnerAttackRadius) return false;

      return true;
    }

    public class ZombieBehaviourDebugFriend
    {
      private ZombieBehaviour zombie;

      public ZombieBehaviourDebugFriend(ZombieBehaviour zombie)
      {
        this.zombie = zombie;
      }

      public Vector3 GetVisionPos() { return zombie.visionPos; }
      public Vector3 GetPlayerVisionPos() { return zombie.playerPos; }
      public RaycastHit GetHitPos() { return zombie.playerRaycastHit; }
      public bool IsAttacking() { return zombie.currentState == zombie.attackState; }
    }

    [System.Serializable]
    public class Waypoint
    {
      public Transform Destination;
      // how much time zombie will be at this position before going on another position
      public float WaitTime;
    }
  }
}
