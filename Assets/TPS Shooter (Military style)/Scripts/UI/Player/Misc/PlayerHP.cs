using UnityEngine;
using UnityEngine.UI;

using LightDev;
using LightDev.UI;

namespace TPSShooter.UI
{
  public class PlayerHP : CanvasElement
  {
    [Header("References")]
    public Image healthBar;

    public override void Subscribe()
    {
      Events.SceneLoaded += Show;
      Events.GamePaused += Hide;
      Events.GameResumed += Show;
      Events.PlayerDied += Hide;
      Events.PlayerChangedHP += OnPlayerChangedHP;
    }

    public override void Unsubscribe()
    {
      Events.SceneLoaded -= Show;
      Events.GamePaused -= Hide;
      Events.GameResumed -= Show;
      Events.PlayerDied -= Hide;
      Events.PlayerChangedHP -= OnPlayerChangedHP;
    }

    public override void ShowForLateSpawn()
    {
      Show();
      OnPlayerChangedHP();
    }

    protected override void OnStartShowing()
    {
      OnPlayerChangedHP();
    }

    private void OnPlayerChangedHP()
    {
      var player = PlayerBehaviour.GetInstance();
      if (player == null || healthBar == null)
        return;

      healthBar.fillAmount = player.GetCurrentHP() / player.GetMaxHP();
    }
  }
}