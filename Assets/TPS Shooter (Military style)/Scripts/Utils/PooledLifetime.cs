using UnityEngine;

namespace TPSShooter
{
  /// <summary>
  /// Attach to pooled objects that should auto-return to pool after a delay.
  /// Timer resets on each enable (re-spawn from pool).
  /// Also restarts any ParticleSystem on the same GameObject.
  /// </summary>
  [AddComponentMenu("")]
  public class PooledLifetime : MonoBehaviour
  {
    [Tooltip("Seconds before auto-returning to pool.")]
    public float delay = 1f;

    private float _elapsed;

    private void OnEnable()
    {
      _elapsed = 0f;

      // Restart particle system so it replays on each spawn
      var ps = GetComponent<ParticleSystem>();
      if (ps != null)
      {
        ps.Clear();
        ps.Play();
      }
    }

    private void Update()
    {
      _elapsed += Time.deltaTime;
      if (_elapsed >= delay)
      {
        GamePool.Despawn(gameObject);
      }
    }
  }
}
