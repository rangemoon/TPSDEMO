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

      replayButton.onClick.AddListener(OnReplay);
      homeButton.onClick.AddListener(OnLoadHome);
    }

    public override void Unsubscribe()
    {
      base.Unsubscribe();

      Events.GameFinishedResult -= OnGameFinished;
      Events.GameLoadHomeScene -= Hide;
      Events.GameReplay -= Hide;

      replayButton.onClick.RemoveListener(OnReplay);
      homeButton.onClick.RemoveListener(OnLoadHome);
    }

    private void OnGameFinished(bool win)
    {
      isWin = win;
      Show();
    }

    protected override void OnStartShowing()
    {
      victoryPanel.SetActive(isWin);
      defeatPanel.SetActive(!isWin);
      continuePanel.SetActive(false);

      Invoke("HideGameOverPanel", resultShowDelay);

      DelayAction(resultShowDelay, () =>
      {
        if (!gameObject.activeSelf) return;
        continuePanel.SetActive(true);
      });
    }

    private void HideGameOverPanel()
    {
      victoryPanel.SetActive(false);
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
