using UnityEngine;

namespace TPSShooter
{
  /// <summary>
  /// Enemy part that can be damaged by Player Bullet.
  ///
  /// GameObject has to have EnemyDamagable layer.
  /// </summary>
  [RequireComponent(typeof(Collider))]
  public class EnemyDamagable : MonoBehaviour
  {
    [SerializeField] private float _damageMultiplier = 1f;

    private EnemyBehaviour _cachedEnemy;

    private EnemyBehaviour GetEnemyBehaviour()
    {
      if (_cachedEnemy == null)
        _cachedEnemy = transform.GetComponentInParent<EnemyBehaviour>();
      return _cachedEnemy;
    }

    public virtual void OnBulletHit(PlayerBullet bullet)
    {
      GetEnemyBehaviour().OnBulletHit(bullet, _damageMultiplier);
    }
  }
}
