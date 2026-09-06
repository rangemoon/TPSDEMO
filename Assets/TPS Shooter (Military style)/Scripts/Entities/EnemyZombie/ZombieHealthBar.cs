using UnityEngine;
using UnityEngine.UI;

namespace TPSShooter
{
  [RequireComponent(typeof(ZombieBehaviour))]
  public class ZombieHealthBar : MonoBehaviour
  {
    public Transform holder;
    public Image fillImage;

    private ZombieBehaviour zombie;
    private Transform player;

    private void Start()
    {
      zombie = GetComponent<ZombieBehaviour>();

      zombie.onHpChanged += OnHpChanged;
      zombie.onDied += OnDied;
      RefreshLookTarget();
      UpdateHP();
    }

    private void Update()
    {
      RefreshLookTarget();
      if (player == null || holder == null)
        return;

      holder.LookAt(player);
      holder.AnulateRotationExceptY();
    }

    /// <summary>
    /// 把血条朝向刷新为本机玩家；本机玩家尚未生成时保持为空。
    /// </summary>
    private void RefreshLookTarget()
    {
      PlayerBehaviour localPlayer = PlayerBehaviour.GetLocalPlayer();
      if (localPlayer != null)
        player = localPlayer.transform;
    }

    private void OnHpChanged()
    {
      UpdateHP();
    }

    private void OnDied()
    {
      holder.gameObject.SetActive(false);
    }

    private void UpdateHP()
    {
      fillImage.fillAmount = zombie.GetHP() / 100;
    }
  }
}
