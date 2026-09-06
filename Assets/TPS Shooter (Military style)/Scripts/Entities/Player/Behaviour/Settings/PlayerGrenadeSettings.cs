using UnityEngine;

namespace TPSShooter
{
  [System.Serializable]
  public class PlayerGrenadeSettings
  {
    public GameObject GrenadePrefab;
    public Transform GrenadePosition;
    public Transform GrenadeArmParent;

    [Header("Grenade Count")]
    public int maxGrenadeCount = 3;
    public int grenadesPerVideo = 1;
  }
}
