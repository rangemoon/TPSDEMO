using UnityEngine;

using LightDev.Core;

namespace LightDev.UI
{
  /// <summary>
  /// UI element that controlled by CanvasManager.
  ///
  /// GameObject would be deactived, you need to subscribe on Events to Show.
  /// </summary>
  public abstract class CanvasElement : Base
  {
    [Range(0, 10)]
    public float showTime;
    [Range(0, 10)]
    public float hideTime;

    private Coroutine showCoroutine;
    private Coroutine hideCoroutine;

    /// <summary>
    /// Called by CanvasManager in Awake method.
    ///
    /// Used for subscribing on events in order to know when to Show UI.
    /// </summary>
    public virtual void Subscribe()
    {
    }

    /// <summary>
    /// Called by CanvasManager in OnDestroy method.
    ///
    /// Unsubscribe from all events.
    /// </summary>
    public virtual void Unsubscribe()
    {
    }

    protected virtual void OnStartShowing()
    {
    }

    protected virtual void OnFinishShowing()
    {
    }

    protected virtual void OnStartHiding()
    {
    }

    protected virtual void OnFinishHiding()
    {
    }

    /// <summary>
    /// 联机时本地玩家由 Mirror 在 SceneLoaded 事件之后才生成，订阅了 SceneLoaded 的 HUD 会错过 Show。
    /// 由 PlayerNetwork.OnStartLocalPlayer 调用，补齐初始显示；默认隐藏的元素应重写为空或做状态检查。
    /// </summary>
    public virtual void ShowForLateSpawn()
    {
      Show();
    }

    /// <summary>
    /// 1) Activates GameObject.
    /// 2) Calls OnStartShowing().
    /// 3) After showTime delay calls OnFinishShowing().
    /// Skip the coroutine when a parent is inactive, otherwise Unity throws.
    /// </summary>
    protected void Show()
    {
      StopShowCoroutine();
      StopHideCoroutine();

      Activate();
      if (!gameObject.activeInHierarchy)
        return;

      OnStartShowing();
      showCoroutine = DelayAction(showTime, OnFinishShowing);
    }

    /// <summary>
    /// 1) Activates GameObject.
    /// 2) Calls OnStartShowing() and then OnFinishShowing() without showTime delay.
    /// </summary>
    protected void InstantShow()
    {
      StopShowCoroutine();
      StopHideCoroutine();

      Activate();
      if (!gameObject.activeInHierarchy)
        return;

      OnStartShowing();
      OnFinishShowing();
    }

    /// <summary>
    /// 1) Calls OnStartHiding().
    /// 2) After hideTime delay calls OnFinishShowing().
    /// 3) Deactivates GameObject.
    /// </summary>
    protected void Hide()
    {
      if (!gameObject.activeInHierarchy) return;

      StopShowCoroutine();
      StopHideCoroutine();

      OnStartHiding();
      hideCoroutine = DelayAction(hideTime, () =>
      {
        OnFinishHiding();
        Deactivate();
      });
    }

    /// <summary>
    /// 1) Calls OnStartHiding() and then without hideTime delay OnFinishShowing().
    /// 2) Deactivates GameObject.
    /// </summary>
    protected void InstantHide()
    {
      if (!gameObject.activeInHierarchy) return;

      StopShowCoroutine();
      StopHideCoroutine();

      OnStartHiding();
      OnFinishHiding();
      Deactivate();
    }

    private void StopShowCoroutine()
    {
      if (showCoroutine != null)
      {
        StopCoroutine(showCoroutine);
        showCoroutine = null;
      }
    }

    private void StopHideCoroutine()
    {
      if (hideCoroutine != null)
      {
        StopCoroutine(hideCoroutine);
        hideCoroutine = null;
      }
    }
  }
}
