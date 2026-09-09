using UnityEngine;
using UnityEngine.UI;

using LightDev;
using LightDev.UI;
using TMPro;

namespace TPSShooter.UI
{
  public class VehicleDetection : CanvasElement
  {
    private const string GetInMessage = "按 F 上车";
    private const string GetOutMessage = "按 F 下车";

    public GameObject getInVehichleUI;
    public GameObject getOutVehicleUI;

    public override void Subscribe()
    {
      base.Subscribe();

      Events.SceneLoaded += Show;
      Events.PlayerDetectVehicle += OnPlayerDetectVehicle;
      Events.PlayerUndetectVehicle += OnPlayerUndetectVehicle;
      Events.PlayerGetInVehicle += OnPlayerGetInVehicle;
      Events.PlayerGetOutVehicle += OnPlayerGetOutVehicle;
      Events.GameFinished += Hide;
    }

    public override void Unsubscribe()
    {
      base.Unsubscribe();

      Events.SceneLoaded -= Show;
      Events.PlayerDetectVehicle -= OnPlayerDetectVehicle;
      Events.PlayerUndetectVehicle -= OnPlayerUndetectVehicle;
      Events.PlayerGetInVehicle -= OnPlayerGetInVehicle;
      Events.PlayerGetOutVehicle -= OnPlayerGetOutVehicle;
      Events.GameFinished -= Hide;
    }

    /// <summary>
    /// 载具提示挂在场景 Input 上，联机补 HUD 时也要打开容器，否则靠近载具没有提示。
    /// </summary>
    public override void ShowForLateSpawn()
    {
      Show();
    }

    protected override void OnStartShowing()
    {
      base.OnStartShowing();

      ApplyLocalizedText(getInVehichleUI, GetInMessage);
      ApplyLocalizedText(getOutVehicleUI, GetOutMessage);

      if (getInVehichleUI != null)
        getInVehichleUI.SetActive(false);
      if (getOutVehicleUI != null)
        getOutVehicleUI.SetActive(false);
    }

    /// <summary>
    /// Prefab 里写死了英文；文案改这里，避免再去改 Input 预制体。
    /// </summary>
    private static void ApplyLocalizedText(GameObject root, string message)
    {
      if (root == null)
        return;

      Text[] labels = root.GetComponentsInChildren<Text>(true);
      for (int i = 0; i < labels.Length; i++)
      {
        if (labels[i] != null)
          labels[i].text = message;
      }

      TMP_Text[] tmpLabels = root.GetComponentsInChildren<TMP_Text>(true);
      for (int i = 0; i < tmpLabels.Length; i++)
      {
        if (tmpLabels[i] != null)
          tmpLabels[i].text = message;
      }
    }

    private void OnPlayerDetectVehicle()
    {
      getInVehichleUI.SetActive(true);
    }

    private void OnPlayerUndetectVehicle()
    {
      getInVehichleUI.SetActive(false);
    }

    private void OnPlayerGetInVehicle()
    {
      getInVehichleUI.SetActive(false);
      getOutVehicleUI.SetActive(true);
    }

    private void OnPlayerGetOutVehicle()
    {
      getOutVehicleUI.SetActive(false);
    }

    public void OnUseVehicleClick() { Events.UseVehicleRequested.Call(); }
  }
}
