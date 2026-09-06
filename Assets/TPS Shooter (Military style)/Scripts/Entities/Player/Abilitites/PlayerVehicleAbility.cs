using UnityEngine;

using LightDev;

namespace TPSShooter
{
  [RequireComponent(typeof(PlayerBehaviour))]
  public class PlayerVehicleAbility : MonoBehaviour
  {
    public TPSCamera tpsCamera;
    public bool hideSkinWhileDriving = true;
    public GameObject skin;

    private bool _isDriving;
    private bool _isCarDetected;
    private PlayerBehaviour ownerPlayer;

    private void Awake()
    {
      ownerPlayer = GetComponent<PlayerBehaviour>();
      Events.UseVehicleRequested += OnUseVehicleRequested;
    }

    private void OnDestroy()
    {
      Events.UseVehicleRequested -= OnUseVehicleRequested;
    }

    private void Update()
    {
      if (ownerPlayer == null || !ownerPlayer.IsLocalPlayer)
        return;

      if (_isDriving)
      {
        UpdatePlayerPositionInVehicle();
      }
      else
      {
        CheckVehicleDetection();
      }
    }

    private void OnUseVehicleRequested()
    {
      if (ownerPlayer == null || !ownerPlayer.IsLocalPlayer)
        return;

      if (_isDriving)
      {
        GetOutVehicle();
      }
      else if (_isCarDetected)
      {
        GetInVehicle();
      }
    }

    private void CheckVehicleDetection()
    {
      // Check nearby vehicles
      if (CheckNearbyVehicles())
      {
        if (!_isCarDetected)
        {
          _isCarDetected = true;
          Events.PlayerDetectVehicle.Call();
        }
      }
      else
      {
        if (_isCarDetected)
        {
          _isCarDetected = false;
          Events.PlayerUndetectVehicle.Call();
        }
      }
    }

    // The minimum distance between car and the player, when the player can get in car.
    private readonly float minVehicleDistance = 3f;
    private Vehicle currentVechicleBehaviour;

    // Checks if there are any vehicle that the player can use.
    private bool CheckNearbyVehicles()
    {
      if (ownerPlayer == null || ownerPlayer.FireHitObject == null) return false;
      if (!ownerPlayer.FireHitObject.GetComponentInParent<Vehicle>()) return false;
      if (Vector3.Distance(transform.position, ownerPlayer.FireHitObject.position) > minVehicleDistance) return false;

      VehicleHealthBar vehicleHP = ownerPlayer.FireHitObject.GetComponent<VehicleHealthBar>();
      if(vehicleHP && vehicleHP.WasExplode) return false;

      return true;
    }

    // The player gets the in car.
    private void GetInVehicle()
    {
      // Determines a new VehicleBehaviour
      currentVechicleBehaviour = ownerPlayer.FireHitObject.GetComponent<Vehicle>();

      // sets the player state
      _isDriving = true;

      // calls this method in order to vehicle make some actions
      currentVechicleBehaviour.PlayerGetIn();

      // makes the player invisible
      if (hideSkinWhileDriving)
        skin.SetActive(false);

      ownerPlayer.IsDrivingVehicle = true;
      ownerPlayer.DrivingVehicle = currentVechicleBehaviour;
      Events.PlayerGetInVehicle.Call();
    }

    // The plyaer gets the out car.
    private void GetOutVehicle()
    {
      // calls this method in order to vehicle make some actions
      currentVechicleBehaviour.PlayerGetOut();

      // sets the player's state
      _isDriving = false;

      // sets position on the ground
      transform.position = new Vector3(
          currentVechicleBehaviour.playerStand.position.x,
          currentVechicleBehaviour.playerStand.position.y + 1.5f,
          currentVechicleBehaviour.playerStand.position.z
      );

      transform.rotation = Quaternion.Euler(0, currentVechicleBehaviour.playerStand.rotation.y, 0);

      // makes the player's skin visible
      if (hideSkinWhileDriving)
        skin.SetActive(true);

      ownerPlayer.IsDrivingVehicle = false;
      ownerPlayer.DrivingVehicle = null;
      Events.PlayerGetOutVehicle.Call();
    }

    // Updates the player position in vehicle.
    private void UpdatePlayerPositionInVehicle()
    {
      transform.position = currentVechicleBehaviour.playerSit.position;
      transform.rotation = currentVechicleBehaviour.playerSit.rotation;
    }
  }
}
