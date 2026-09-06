using UnityEngine;

namespace TPSShooter
{
  public abstract class AbstractBullet : MonoBehaviour
  {
    public float speed = 100;
    public float damage = 1;
    public float lifeTime;
    public LayerMask hitLayers; // layers that can be affected by bullet
    public BulletDecals decals;

    private float _startShootTime;
    private Vector3 _startPosition;
    private Vector3 _startDirection;
    private bool _isActive;

    /// <summary>
    /// Initializes bullet state. Must be called after spawning from pool.
    /// </summary>
    public void Init()
    {
      _startShootTime = Time.time;
      _startPosition = transform.position;
      _startDirection = transform.forward;
      _isActive = true;
    }

    private void Update()
    {
      if (!_isActive) return;

      // Check lifetime expiry
      if (Time.time - _startShootTime >= lifeTime)
      {
        Despawn();
        return;
      }

      Vector3 nextPosition = GetNextPosition();
      RaycastHit hit;

      if (Physics.Linecast(transform.position, nextPosition, out hit, hitLayers))
      {
        SpawnDecals(hit);
        OnBulletCollision(hit);
        Despawn();
      }
      else
      {
        transform.position = nextPosition;
      }
    }

    private Vector3 GetNextPosition()
    {
      float timeFromStart = Time.time - _startShootTime;
      return _startPosition + (_startDirection * speed * timeFromStart);
    }

    private void SpawnDecals(RaycastHit hit)
    {
      if (decals == null) return;

      Quaternion decalRotation = Quaternion.FromToRotation(Vector3.up, hit.normal);

      foreach (var decalProperty in decals.decalProperties)
      {
        if (!hit.collider.material.name.Contains(decalProperty.surfacePhysicMaterial.name)) continue;

        foreach (GameObject decalPrefab in decalProperty.decalPrefabs)
        {
          GameObject decal = GamePool.Spawn(decalPrefab, hit.point, decalRotation, hit.transform);

          var ps = decal.GetComponent<ParticleSystem>();
          float destroyTime = ps != null ? ps.main.duration : decals.destroyTime;

          var lifetime = decal.GetComponent<PooledLifetime>();
          if (lifetime == null) lifetime = decal.AddComponent<PooledLifetime>();
          lifetime.delay = destroyTime;
        }
      }
    }

    /// <summary>
    /// Returns bullet to pool instead of destroying.
    /// </summary>
    protected virtual void Despawn()
    {
      _isActive = false;
      GamePool.Despawn(gameObject);
    }

    protected abstract void OnBulletCollision(RaycastHit hit);
  }
}
