using UnityEngine;
using TPSShooter.UI;

using LightDev;

namespace TPSShooter
{
  [RequireComponent(typeof(EnemyBehaviour))]
  public class EnemyRadarableObject : RadarableObject
  {
    private EnemyBehaviour enemy;

    private void Awake()
    {
      enemy = GetComponent<EnemyBehaviour>();

      Events.EnemyKilled += OnEnemyKilled;
    }

    protected override void OnDestroy()
    {
      Events.EnemyKilled -= OnEnemyKilled;
      base.OnDestroy();
    }

    protected override bool CanShowOnRadar()
    {
      return enemy != null && enemy.GetHP() > 0;
    }

    private void OnEnemyKilled(EnemyBehaviour enemy)
    {
      if(this.enemy == enemy)
      {
        DestroyRadarableObject();
      }
    }
  }
}
