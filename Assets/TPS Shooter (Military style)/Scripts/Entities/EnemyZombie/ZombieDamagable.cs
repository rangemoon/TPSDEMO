using UnityEngine;

namespace TPSShooter
{
  /// <summary>
  /// Zombie part that can be damaged by Player Bullet.
  ///
  /// GameObject has to have EnemyDamagable layer.
  /// </summary>
  [RequireComponent(typeof(Collider))]
  public class ZombieDamagable : MonoBehaviour
  {
    [SerializeField] private float _damageMultiplier = 1f;

    private ZombieBehaviour _cachedZombie;

    private ZombieBehaviour GetZombieBehaviour()
    {
      if (_cachedZombie == null)
        _cachedZombie = transform.GetComponentInParent<ZombieBehaviour>();
      return _cachedZombie;
    }

    public virtual void OnBulletHit(PlayerBullet bullet)
    {
      GetZombieBehaviour().OnBulletHit(bullet, _damageMultiplier);
    }
  }
}
