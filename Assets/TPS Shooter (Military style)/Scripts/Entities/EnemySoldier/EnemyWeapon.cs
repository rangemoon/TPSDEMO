using UnityEngine;
using System.Collections;

using LightDev.Core;

namespace TPSShooter
{
  public class EnemyWeapon : Base
  {
    [Header("- Bullet settings -")]
    public GameObject BulletPrefab;
    public Transform BulletPosition;

    [Header("- Gun settings -")]
    public float ShootFrequency = 0.14f;

    [Header("- Sound -")]
    public AudioSource FireSound;

    [Header("- Fire particle -")]
    public ParticleSystem FireParticleSystem;

    public bool CanShoot { get; private set; } = true;

    public bool Fire(Vector3 positionWhereToFire)
    {
      if(!CanShoot) return false;

      // makes shooting unavailable for shootFrequency time
      CanShoot = false;
      DelayAction(ShootFrequency, () => CanShoot = true, false);

      // Sound
      FireSound?.PlayOneShot(FireSound.clip);

      // Particle
      FireParticleSystem?.Stop();
      FireParticleSystem?.Play();

      // Bullet position
      BulletPosition.LookAt(positionWhereToFire);

      // Spawns bullet from pool
      GameObject bullet = GamePool.Spawn(
        BulletPrefab,
        BulletPosition.transform.position,
        BulletPosition.transform.rotation
      );
      EnemyBullet enemyBullet = bullet.GetComponent<EnemyBullet>();
      enemyBullet.Init();
      enemyBullet.MasterOfBullet = transform;

      return true;
    }
  }
}
