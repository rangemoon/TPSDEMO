using LightDev.Core;
using UnityEngine;

namespace LightDev.UI
{
  /// <summary>
  /// Attach this script to Canvas GameObject in order to control CanvasElements.
  /// </summary>
  public class CanvasManager : Base
  {
    protected CanvasElement[] canvasElements;

    protected virtual void Awake()
    {
      canvasElements = GetComponentsInChildren<CanvasElement>(true);
      foreach(CanvasElement element in canvasElements)
      {
        element.Activate();
        element.Subscribe();
        element.Deactivate();
      }
    }

    protected virtual void OnDestroy()
    {
      foreach (CanvasElement element in canvasElements)
      {
        element.Unsubscribe();
      }
    }

    /// <summary>
    /// 联机后加入的客户端会错过 SceneLoaded，把已订阅的 HUD 再 Show 一次。
    /// Pause 与菜单大厅已重写 ShowForLateSpawn 为空，不会被误打开。
    /// </summary>
    public static void ShowLateJoinHud()
    {
      CanvasManager[] managers = Object.FindObjectsByType<CanvasManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
      for (int i = 0; i < managers.Length; i++)
      {
        CanvasManager manager = managers[i];
        if (manager == null || manager.canvasElements == null)
          continue;

        for (int j = 0; j < manager.canvasElements.Length; j++)
        {
          if (manager.canvasElements[j] != null)
            manager.canvasElements[j].ShowForLateSpawn();
        }
      }
    }
  }
}
