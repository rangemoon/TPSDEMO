using UnityEngine;
using LightDev;

namespace TPSShooter
{
  public partial class PlayerBehaviour
  {
    private const float MaxHP = 100;
    private float hp = MaxHP;
    private float spawnProtectUntil;

    public float GetMaxHP() { return MaxHP; }
    public float GetCurrentHP() { return hp; }

    /// <summary>
    /// 出生后短时间不受伤。加入端刷在房主旁边时，会立刻吃到已在开火的敌人子弹。
    /// </summary>
    /// <param name="seconds">保护时长（秒）。</param>
    public void EnableSpawnProtection(float seconds)
    {
      spawnProtectUntil = Time.time + Mathf.Max(0f, seconds);
    }

    /// <summary>
    /// Host 生成后把血量拉满，避免 SyncVar 仍是未写入的 0。
    /// </summary>
    public void ResetHpToFull()
    {
      hp = MaxHP;
      Events.PlayerChangedHP.Call();
      if (_playerNetwork != null)
        _playerNetwork.ServerSetHp(hp);
    }

    public void OnBulletHit(EnemyBullet bullet)
    {
      float hpBefore = hp;
      DecreaseHP(bullet.damage);
      if (hp >= hpBefore)
        return;

      Vector3 sourcePosition = bullet != null && bullet.MasterOfBullet != null
        ? bullet.MasterOfBullet.position
        : (bullet != null ? bullet.transform.position : transform.position);

      if (IsLocalPlayer)
      {
        sounds.PlaySound(sounds.HitSound);
        Events.PlayerBulletHit.Call(bullet);
      }

      if (_playerNetwork != null)
        _playerNetwork.ServerNotifyHit(sourcePosition);
    }

    public void OnGrenadeHit(AbstractGrenade grenade)
    {
      if (Time.time < spawnProtectUntil)
        return;

      if (Vector3.Distance(transform.position, grenade.transform.position) < 2)
      {
        Die();
      }
    }

    public void OnZombieHit(ZombieBehaviour zombie)
    {
      float hpBefore = hp;
      DecreaseHP(zombie.damage);
      if (hp >= hpBefore)
        return;

      if (IsLocalPlayer)
      {
        sounds.PlaySound(sounds.HitSound);
        Events.PlayerZombieHit.Call(zombie);
      }

      if (_playerNetwork != null)
        _playerNetwork.ServerNotifyHit(zombie != null ? zombie.GetPosition() : transform.position);
    }

    /// <summary>
    /// 本机玩家被 Host 结算命中后，播放受击音和方向提示。血量仍走 SyncVar。
    /// </summary>
    /// <param name="sourcePosition">攻击来源世界坐标。</param>
    public void PlayNetworkHitFeedback(Vector3 sourcePosition)
    {
      if (!IsLocalPlayer)
        return;

      sounds.PlaySound(sounds.HitSound);
      Events.PlayerHitFromPosition.Call(sourcePosition);
    }

    public void IncreaseHP(float deltaHP)
    {
      AddDeltaHP(deltaHP);
    }

    public void DecreaseHP(float hp)
    {
      AddDeltaHP(-hp);
    }

    /// <summary>
    /// 应用由 Host 同步过来的血量；降到 0 时走同一套死亡流程。
    /// </summary>
    /// <param name="networkHp">Host 同步的当前血量。</param>
    public void ApplyNetworkHp(float networkHp)
    {
      // 生成瞬间 hook 可能先带 0：满血且仍存活时忽略，避免加入端当场死亡
      if (networkHp <= 0 && IsAlive && hp >= MaxHP)
        return;

      hp = Mathf.Clamp(networkHp, 0, MaxHP);
      Events.PlayerChangedHP.Call();

      if (hp <= 0)
        Die();
    }

    private void AddDeltaHP(float deltaHP)
    {
      if (deltaHP < 0 && Time.time < spawnProtectUntil)
        return;

      hp += deltaHP;
      hp = Mathf.Clamp(hp, 0, MaxHP);
      Events.PlayerChangedHP.Call();

      if (_playerNetwork != null)
        _playerNetwork.ServerSetHp(hp);

      if (hp <= 0)
        Die();
    }
  }
}
