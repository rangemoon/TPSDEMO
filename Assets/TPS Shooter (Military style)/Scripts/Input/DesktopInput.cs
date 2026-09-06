using UnityEngine;

using LightDev;

namespace TPSShooter
{
  public class DesktopInput : MonoBehaviour
  {
    [Header("- Sensitivity -")]
    public float mouseSensitivity = 4;

    [Header("- Cursor -")]
    public bool isCursorLocked = true;

    [Header("- Key codes -")]
    public KeyCode runKeyCode = KeyCode.LeftShift;
    public KeyCode jumpKeyCode = KeyCode.Space;
    public KeyCode crouchKeyCode = KeyCode.C;

    [Space]
    public KeyCode reloadKeyCode = KeyCode.R;
    public KeyCode dropWeaponKeyCode = KeyCode.K;
    public KeyCode grenadeKeyCode = KeyCode.G;
    public KeyCode chooseWeaponKeyCode = KeyCode.P;
    public KeyCode[] swapWeaponKeyCodes = {
        KeyCode.Alpha0,
        KeyCode.Alpha1,
        KeyCode.Alpha2,
        KeyCode.Alpha3,
        KeyCode.Alpha4,
        KeyCode.Alpha5,
        KeyCode.Alpha6,
        KeyCode.Alpha7,
        KeyCode.Alpha8,
        KeyCode.Alpha9
    };

    [Space]
    public KeyCode carBrakeKeyCode = KeyCode.Space;
    public KeyCode useVehicleKeyCode = KeyCode.F;

    [Space]
    public KeyCode pauseGameKeyCode = KeyCode.Escape;

    private bool isWeaponChoose;
    private PlayerBehaviour ownerPlayer;

    private void Awake()
    {
      ownerPlayer = transform.root.GetComponentInChildren<PlayerBehaviour>(true);
      if (isCursorLocked) LockCursor();

      Events.GamePaused += OnGamePaused;
      Events.GameResumed += OnGameResumed;
      Events.PlayerDied += OnPlayerDied;
      Events.GameFinished += OnGameFinished;
    }

    private void OnDestroy()
    {
      Events.GamePaused -= OnGamePaused;
      Events.GameResumed -= OnGameResumed;
      Events.PlayerDied -= OnPlayerDied;
      Events.GameFinished -= OnGameFinished;
    }

    private void Update()
    {
      if (ownerPlayer != null && (!ownerPlayer.IsLocalPlayer || !ownerPlayer.isActiveAndEnabled))
        return;

      if (GameManager.IsGamePaused || GameManager.IsGameFinished)
      {
        InputController.VerticalRotation = 0;
        InputController.HorizontalRotation = 0;
        return;
      }

      // Weapon Choose
      if (Input.GetKeyDown(chooseWeaponKeyCode))
      {
        isWeaponChoose = true;
        UnlockCursor();
        Events.WeaponChooseStartRequest.Call();
      }
      else if (Input.GetKeyUp(chooseWeaponKeyCode))
      {
        isWeaponChoose = false;
        LockCursor();
        Events.WeaponChooseFinishRequest.Call();
      }

      InputController.VerticalMovement = Input.GetAxis("Vertical");
      InputController.HorizontalMovement = Input.GetAxis("Horizontal");
      InputController.VerticalRotation = isWeaponChoose ? 0 : Input.GetAxis("Mouse Y") * mouseSensitivity;
      InputController.HorizontalRotation = isWeaponChoose ? 0 : Input.GetAxis("Mouse X") * mouseSensitivity;

      if (isWeaponChoose) return;

      // Pause state
      if (Input.GetKeyDown(pauseGameKeyCode))
      {
        Events.GamePauseRequested.Call();

        if (isWeaponChoose)
        {
          Events.WeaponChooseFinishRequest.Call();
        }
      }

      InputController.IsRun = Input.GetKey(runKeyCode);
      InputController.IsBrakePressed = Input.GetKey(carBrakeKeyCode);

      // Player states
      if (Input.GetKeyDown(jumpKeyCode))
      {
        Events.JumpRequested.Call();
      }
      if (Input.GetKeyDown(crouchKeyCode))
      {
        Events.CrouchRequested.Call();
      }

      // Weapon
      if (Input.GetMouseButton(0))
      {
        Events.FireRequested.Call();
      }
      if (Input.GetKeyDown(reloadKeyCode))
      {
        Events.ReloadRequested.Call();
      }
      if (Input.GetKeyDown(dropWeaponKeyCode))
      {
        PlayerBehaviour localPlayer = PlayerBehaviour.GetLocalPlayer();
        if (localPlayer != null)
          Events.DropWeaponRequested.Call(localPlayer.CurrentWeaponIndex);
      }
      if (Input.GetMouseButtonDown(1))
      {
        Events.AimActivateRequested.Call();
      }
      for (int i = 0; i < swapWeaponKeyCodes.Length; i++)
      {
        if (Input.GetKeyDown(swapWeaponKeyCodes[i]))
        {
          Events.SwapWeaponRequested.Call(i - 1);
        }
      }

      // Grenade
      if (Input.GetKeyDown(grenadeKeyCode))
      {
        Events.GrenadeStartThrowRequest.Call();
      }
      else if (Input.GetKeyUp(grenadeKeyCode))
      {
        Events.GrenadeFinishThrowRequest.Call();
      }

      // Vehicle
      if (Input.GetKeyDown(useVehicleKeyCode))
      {
        Events.UseVehicleRequested.Call();
      }
    }

    private void OnGamePaused()
    {
      if (isCursorLocked) UnlockCursor();
    }

    private void OnGameResumed()
    {
      if (isCursorLocked) LockCursor();
    }

    private void OnPlayerDied()
    {
      if (isCursorLocked) UnlockCursor();
    }

    /// <summary>
    /// 游戏胜利或失败结算时解锁鼠标，方便点击结算 UI。
    /// </summary>
    private void OnGameFinished()
    {
      if (isCursorLocked) UnlockCursor();
    }

    private void LockCursor()
    {
      Cursor.lockState = CursorLockMode.Locked;
      Cursor.visible = false;
    }

    private void UnlockCursor()
    {
      Cursor.lockState = CursorLockMode.None;
      Cursor.visible = true;
    }
  }
}