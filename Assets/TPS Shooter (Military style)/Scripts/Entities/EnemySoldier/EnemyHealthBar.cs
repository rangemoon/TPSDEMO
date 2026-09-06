using UnityEngine;
using UnityEngine.UI;

namespace TPSShooter
{
  [RequireComponent(typeof(EnemyBehaviour))]
  public class EnemyHealthBar : MonoBehaviour
  {
    public Transform holder;
    public Image fillImage;

    private EnemyBehaviour enemy;
    private Transform player;

    private void Start()
    {
      enemy = GetComponent<EnemyBehaviour>();

      enemy.onHpChanged += OnHpChanged;
      enemy.onDied += OnDied;
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
      fillImage.fillAmount = enemy.GetHP() / 100;
    }
  }
}
