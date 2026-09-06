using UnityEngine;

namespace TPSShooter
{
  public class PlayerBullet : AbstractBullet
  {
    protected override void OnBulletCollision(RaycastHit hit)
    {
      // Apply impact force (single GetComponent instead of two)
      var rb = hit.transform.GetComponent<Rigidbody>();
      if (rb != null)
      {
        rb.AddForceAtPosition(transform.forward * 1800, hit.point);
      }

      // Enemy damage (single GetComponent per type instead of checking then getting again)
      var enemyDamagable = hit.transform.GetComponent<EnemyDamagable>();
      if (enemyDamagable != null)
      {
        enemyDamagable.OnBulletHit(this);
      }
      else
      {
        var zombieDamagable = hit.transform.GetComponent<ZombieDamagable>();
        if (zombieDamagable != null)
        {
          zombieDamagable.OnBulletHit(this);
        }
      }
    }
  }
}
