using LightDev;
using LightDev.UI;

namespace TPSShooter.UI
{
  public class Pause : CanvasElement
  {
    public override void Subscribe()
    {
      base.Subscribe();

      Events.GamePaused += Show;
      Events.GameResumed += Hide;
      Events.GameLoadHomeScene += Hide;
      Events.GameReplay += Hide;
    }

    public override void Unsubscribe()
    {
      base.Unsubscribe();

      Events.GamePaused -= Show;
      Events.GameResumed -= Hide;
      Events.GameLoadHomeScene -= Hide;
      Events.GameReplay -= Hide;
    }

    /// <summary>
    /// 暂停面板不能由联机补 HUD 打开，只响应 GamePaused。
    /// </summary>
    public override void ShowForLateSpawn()
    {
    }

    public void OnResume()
    {
      Events.GameResumeRequested.Call();
    }

    public void OnReplay()
    {
      if (GameNetwork.IsClientOnly)
        return;

      Events.GameReplayRequested.Call();
    }

    public void OnLoadHome()
    {
      if (GameNetwork.IsClientOnly)
        return;

      Events.GameLoadHomeSceneRequested.Call();
    }
  }
}
