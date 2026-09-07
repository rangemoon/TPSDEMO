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

    private EnemyNetwork enemyNetwork;

    public bool CanShoot { get; private set; } = true;

    private void Awake()
    {
      enemyNetwork = GetComponentInParent<EnemyNetwork>();
    }

    public bool Fire(Vector3 positionWhereToFire)
    {
      if(!CanShoot) return false;

      // makes shooting unavailable for shootFrequency time
      CanShoot = false;
      DelayAction(ShootFrequency, () => CanShoot = true, false);

      BulletPosition.LookAt(positionWhereToFire);
      PlayMuzzleFx();
      SpawnBullet(BulletPosition.position, BulletPosition.rotation, true);

      if (enemyNetwork != null)
        enemyNetwork.ServerBroadcastShot(BulletPosition.position, BulletPosition.rotation);

      return true;
    }

    /// <summary>
    /// 加入端复制 Host 开火：枪口特效和弹道，不结算伤害。
    /// </summary>
    public void PlayReplicatedShot(Vector3 position, Quaternion rotation)
    {
      PlayMuzzleFx();
      SpawnBullet(position, rotation, false);
    }

    private void PlayMuzzleFx()
    {
      FireSound?.PlayOneShot(FireSound.clip);
      FireParticleSystem?.Stop();
      FireParticleSystem?.Play();
    }

    private void SpawnBullet(Vector3 position, Quaternion rotation, bool dealsDamage)
    {
      if (BulletPrefab == null)
        return;

      GameObject bullet = GamePool.Spawn(BulletPrefab, position, rotation);
      EnemyBullet enemyBullet = bullet.GetComponent<EnemyBullet>();
      if (enemyBullet == null)
        return;

      if (dealsDamage)
        enemyBullet.Init();
      else
        enemyBullet.InitVisualOnly();

      enemyBullet.MasterOfBullet = transform;
    }
  }
}
