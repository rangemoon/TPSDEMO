using UnityEngine;

namespace TPSShooter
{
  public class EnemyBullet : AbstractBullet
  {
    // Can be used by hit marker to find position from where was shot
    public Transform MasterOfBullet { get; set; }

    protected override void OnBulletCollision(RaycastHit hit)
    {
      // Single GetComponent instead of two
      var player = hit.transform.GetComponent<PlayerBehaviour>();
      if (player != null)
      {
        player.OnBulletHit(this);
      }
    }
  }
}
