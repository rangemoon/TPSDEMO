using DG.Tweening;

namespace LightDev.Core
{
  public sealed class TweenManager : IAutoLoadable
  {
    static TweenManager()
    {
      Subscribe();
      InitializeDOTween();
    }

    private static void InitializeDOTween()
    {
      DOTween.Init(false, false, LogBehaviour.ErrorsOnly).SetCapacity(500, 100);
      DOTween.defaultEaseType = Ease.Linear;
      DOTween.defaultUpdateType = UpdateType.Normal;
      DOTween.useSafeMode = false;
    }

    private static void Subscribe()
    {
      Events.ApplicationResumed += OnApplicationResumed;
      Events.ApplicationPaused += OnApplicationPaused;
      Events.SceneUnload += OnSceneUnload;
    }

    private static void OnSceneUnload()
    {
      // 项目关闭了 DOTween SafeMode，切场景后旧 tween 还会去改已销毁的 Transform
      DOTween.KillAll();
    }

    private static void OnApplicationPaused()
    {
      DOTween.PauseAll();
    }

    private static void OnApplicationResumed()
    {
      DOTween.PlayAll();
    }
  }
}
