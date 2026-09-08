using UnityEngine;
using TPSShooter.UI;

using LightDev;

namespace TPSShooter
{
  [RequireComponent(typeof(ZombieBehaviour))]
  public class ZombieRadarableObject : RadarableObject
  {
    private ZombieBehaviour zombie;

    private void Awake()
    {
      zombie = GetComponent<ZombieBehaviour>();

      Events.ZobmieKilled += OnEnemyKilled;
    }

    protected override void OnDestroy()
    {
      Events.ZobmieKilled -= OnEnemyKilled;
      base.OnDestroy();
    }

    protected override bool CanShowOnRadar()
    {
      return zombie != null && zombie.GetHP() > 0;
    }

    private void OnEnemyKilled(ZombieBehaviour zombie)
    {
      if(this.zombie == zombie)
      {
        DestroyRadarableObject();
      }
    }
  }
}
