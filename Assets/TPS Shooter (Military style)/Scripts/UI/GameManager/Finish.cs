using UnityEngine;
using UnityEngine.UI;

using LightDev;
using LightDev.UI;

namespace TPSShooter.UI
{
  public class Finish : CanvasElement
  {
    [Header("Result Panels")]
    public GameObject victoryPanel;
    public GameObject defeatPanel;
    public GameObject continuePanel;

    [Header("Continue Panel Buttons")]
    public Button replayButton;
    public Button homeButton;

    [Header("Timing")]
    public float resultShowDelay = 2f;

    private bool isWin;

    public override void Subscribe()
    {
      base.Subscribe();

      Events.GameFinishedResult += OnGameFinished;
      Events.GameLoadHomeScene += Hide;
      Events.GameReplay += Hide;

      if (replayButton != null)
        replayButton.onClick.AddListener(OnReplay);
      if (homeButton != null)
        homeButton.onClick.AddListener(OnLoadHome);
    }

    public override void Unsubscribe()
    {
      base.Unsubscribe();

      Events.GameFinishedResult -= OnGameFinished;
      Events.GameLoadHomeScene -= Hide;
      Events.GameReplay -= Hide;

      if (replayButton != null)
        replayButton.onClick.RemoveListener(OnReplay);
      if (homeButton != null)
        homeButton.onClick.RemoveListener(OnLoadHome);
    }

    private void OnGameFinished(bool win)
    {
      isWin = win;
      EnsureHierarchyActive();
      Show();
    }

    /// <summary>
    /// 结算面板可能挂在被 CanvasManager 关掉的物体下，Show 前先把祖先激活。
    /// </summary>
    private void EnsureHierarchyActive()
    {
      Transform current = transform;
      while (current != null)
      {
        if (!current.gameObject.activeSelf)
          current.gameObject.SetActive(true);
        current = current.parent;
      }
    }

    /// <summary>
    /// 结算面板不能由联机补 HUD 打开，只响应 GameFinishedResult。
    /// </summary>
    public override void ShowForLateSpawn()
    {
    }

    protected override void OnStartShowing()
    {
      if (victoryPanel != null)
        victoryPanel.SetActive(isWin);
      if (defeatPanel != null)
        defeatPanel.SetActive(!isWin);
      if (continuePanel != null)
        continuePanel.SetActive(false);

      DelayAction(resultShowDelay, () =>
      {
        if (!gameObject.activeSelf) return;
        HideGameOverPanel();
        if (continuePanel != null)
          continuePanel.SetActive(true);
      });
    }

    private void HideGameOverPanel()
    {
      if (victoryPanel != null)
        victoryPanel.SetActive(false);
      if (defeatPanel != null)
        defeatPanel.SetActive(false);
    }

    public void OnReplay()
    {
      Events.GameReplayRequested.Call();
    }

    public void OnLoadHome()
    {
      Events.GameLoadHomeSceneRequested.Call();
    }
  }
}
