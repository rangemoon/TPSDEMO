using UnityEngine;

namespace TPSShooter
{
  public class EnemyBullet : AbstractBullet
  {
    // Can be used by hit marker to find position from where was shot
    public Transform MasterOfBullet { get; set; }

    protected override void OnBulletCollision(RaycastHit hit)
    {
      // 命中子碰撞体时也要找到 PlayerBehaviour，否则联机远端玩家挨打无伤害
      var player = hit.transform.GetComponentInParent<PlayerBehaviour>();
      if (player != null)
      {
        player.OnBulletHit(this);
      }
    }
  }
}
