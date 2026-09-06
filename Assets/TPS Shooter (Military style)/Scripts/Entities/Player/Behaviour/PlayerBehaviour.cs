using UnityEngine;

using LightDev;
using LightDev.Core;

namespace TPSShooter
{
  [RequireComponent(typeof(FootstepSounds))]
  [RequireComponent(typeof(CharacterController))]
  [RequireComponent(typeof(Animator))]
  public partial class PlayerBehaviour : Base
  {
    public PlayerWeaponSettings weaponSettings = new PlayerWeaponSettings();
    public PlayerGrenadeSettings grenadeSettings = new PlayerGrenadeSettings();
    public PlayerSounds sounds = new PlayerSounds();
    public PlayerSettingsIK IkSettings = new PlayerSettingsIK();
    public PlayerCrouchSettings crouchSettings = new PlayerCrouchSettings();
    public PlayerMovementSettings movementSettings = new PlayerMovementSettings();
    private PlayerAnimationParameters animationsParameters = new PlayerAnimationParameters();

    private CharacterController _characterController;
    private Animator _animator;
    private PlayerNetwork _playerNetwork;
    private bool _localControlEnabled;

    public bool IsAlive { get; private set; } = true;
    public float Noise { get { return GetNoise(); } }

    /// <summary>
    /// 返回本机玩家。联机时远端玩家不会成为该引用。
    /// </summary>
    public static PlayerBehaviour GetInstance()
    {
      return PlayerRegistry.GetLocalPlayer();
    }

    /// <summary>
    /// 返回本机玩家，语义与 GetInstance 相同。
    /// </summary>
    public static PlayerBehaviour GetLocalPlayer()
    {
      return PlayerRegistry.GetLocalPlayer();
    }

    /// <summary>
    /// 当前对象是否由本机控制。离线时场景玩家视为本机。
    /// </summary>
    public bool IsLocalPlayer
    {
      get
      {
        if (!GameNetwork.IsActive)
          return true;

        return PlayerRegistry.GetLocalPlayer() == this;
      }
    }

    #region MonoBehaviour

    private void OnValidate()
    {
      crouchSettings.CharacterHeightCrouching = Mathf.Clamp(crouchSettings.CharacterHeightCrouching, 0, GetComponent<CharacterController>().height);
      crouchSettings.CharacterCenterCrouching.y = Mathf.Max(crouchSettings.CharacterCenterCrouching.y, 0);

      movementSettings.AirSpeed = Mathf.Max(movementSettings.AirSpeed, 0);
      movementSettings.JumpSpeed = Mathf.Max(movementSettings.JumpSpeed, 0);
      movementSettings.JumpTime = Mathf.Max(movementSettings.JumpTime, 0);
      movementSettings.ForwardSpeed = Mathf.Max(movementSettings.ForwardSpeed, 0);
      movementSettings.StrafeSpeed = Mathf.Max(movementSettings.StrafeSpeed, 0);
      movementSettings.SprintSpeed = Mathf.Max(movementSettings.SprintSpeed, 0);
      movementSettings.CrouchForwardSpeed = Mathf.Max(movementSettings.CrouchForwardSpeed, 0);
      movementSettings.CrouchStrafeSpeed = Mathf.Max(movementSettings.CrouchStrafeSpeed, 0);

      ValidateWeapons();
    }

    private void Awake()
    {
      if (GameNetwork.IsActive && GetComponentInParent<Mirror.NetworkIdentity>() == null)
      {
        GameObject root = transform.parent != null ? transform.parent.gameObject : gameObject;
        root.SetActive(false);
        return;
      }

      _characterController = GetComponent<CharacterController>();
      _animator = GetComponent<Animator>();
      _playerNetwork = GetComponent<PlayerNetwork>();
      if (_playerNetwork == null)
        _playerNetwork = GetComponentInParent<PlayerNetwork>();
      _animator.applyRootMotion = movementSettings.ApplyRootMotion;

      CheckLayers();

      InitializeWeapon();
      InitializeCrouch();
      InitializeGrenadeCount();

      PlayerRegistry.Register(this);
      if (!GameNetwork.IsActive)
      {
        PlayerRegistry.SetLocal(this);
        EnableLocalControl();
      }
    }

    private void OnDestroy()
    {
      DisableLocalControl();
      PlayerRegistry.Unregister(this);
    }

    private void Update()
    {
      if (!_localControlEnabled)
        return;
      if (GameManager.IsGamePaused || GameManager.IsGameFinished)
        return;

      UpdateGroundCheck();

      UpdateWalk();
      UpdateRun();

      UpdateGravity();
      UpdateMovementSpeed();

      UpdateFirePoint();
    }

    private void LateUpdate()
    {
      if (!_localControlEnabled)
        return;

      UpdateSpineIK();
    }

    private void OnAnimatorIK(int layerIndex)
    {
      if (!_localControlEnabled)
        return;

      UpdateLeftHandIK();
    }

    /// <summary>
    /// 启用本机输入订阅与本地模拟。离线 Awake 或联机 OnStartLocalPlayer 时调用。
    /// </summary>
    public void EnableLocalControl()
    {
      if (_localControlEnabled)
        return;

      _localControlEnabled = true;
      Subscribe();
      PlayerRegistry.EnsureSingleAudioListener(PlayerRegistry.GetPlayerRoot(this));
    }

    /// <summary>
    /// 关闭本机输入订阅。对象销毁或被联机玩家替换时调用。
    /// </summary>
    public void DisableLocalControl()
    {
      if (!_localControlEnabled)
        return;

      _localControlEnabled = false;
      Unsubscribe();
    }

    /// <summary>
    /// 联机启动时停用场景单机玩家的输入，避免与 Mirror 生成的玩家抢控制。
    /// </summary>
    public void PrepareForNetworkReplacement()
    {
      DisableLocalControl();
      Unsubscribe();
    }

    #endregion

    private void CheckLayers()
    {
      if (!LayerMask.LayerToName(gameObject.layer).Equals(Layers.Player))
        Debug.LogError("PlayerBehaviour: Player has to be layered as Player.");
    }

    private void Subscribe()
    {
      Events.JumpRequested += OnJumpRequested;
      Events.CrouchRequested += OnCrouchRequested;

      Events.FireRequested += OnFireRequested;
      Events.ReloadRequested += OnReloadRequested;
      Events.SwapWeaponRequested += OnSwapWeaponRequested;
      Events.DropWeaponRequested += OnDropWeaponRequested;

      Events.GrenadeStartThrowRequest += OnGrenadeStartThrowRequest;
      Events.GrenadeFinishThrowRequest += OnGrenadeFinishThrowRequest;

      Events.AimActivateRequested += OnAimActivateRequested;

      Events.PlayerGetInVehicle += OnPlayerGetInVehicle;
      Events.PlayerGetOutVehicle += OnPlayerGetOutVehicle;
    }

    private void Unsubscribe()
    {
      Events.JumpRequested -= OnJumpRequested;
      Events.CrouchRequested -= OnCrouchRequested;

      Events.FireRequested -= OnFireRequested;
      Events.ReloadRequested -= OnReloadRequested;
      Events.SwapWeaponRequested -= OnSwapWeaponRequested;
      Events.DropWeaponRequested -= OnDropWeaponRequested;

      Events.GrenadeStartThrowRequest -= OnGrenadeStartThrowRequest;
      Events.GrenadeFinishThrowRequest -= OnGrenadeFinishThrowRequest;

      Events.AimActivateRequested -= OnAimActivateRequested;

      Events.PlayerGetInVehicle -= OnPlayerGetInVehicle;
      Events.PlayerGetOutVehicle -= OnPlayerGetOutVehicle;
    }

    private float GetNoise()
    {
      if (IsDrivingVehicle) return 30;
      if (IsFire) return 30;
      if (_jumpingTriggered) return 7;
      if (_animator.GetFloat(animationsParameters.verticalMovementFloat) != 0 || _animator.GetFloat(animationsParameters.horizontalMovementFloat) != 0)
      {
        return IsCrouching ? 3 : 5;
      }
      if (IsReloading) return 3;

      return 0;
    }

    public void Die()
    {
      if (!IsAlive) return;

      IsAlive = false;

      if (_characterController != null)
        _characterController.enabled = false;

      // play die animation if the player is not in a car
      if (!IsDrivingVehicle)
        _animator.SetTrigger(animationsParameters.dieTrigger);

      _animator.SetBool(animationsParameters.aimingBool, false);

      if (IsThrowingGrenade)
        Events.GrenadeFinishThrowRequest.Call();

      if (IsAiming)
        DeactivateAiming();

      if (IsLocalPlayer)
        Events.PlayerDied.Call();

      Events.AnyPlayerDied.Call(this);

      if (_playerNetwork != null)
        _playerNetwork.ServerNotifyDied();
    }
  }
}
