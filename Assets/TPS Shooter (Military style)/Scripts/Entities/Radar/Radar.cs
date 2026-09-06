using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using LightDev;
using LightDev.UI;

namespace TPSShooter.UI
{
  public class Radar : CanvasElement
  {
    [Header("References")]
    public Image radarImg;
    public float radarRadius = 50f;

    private List<RadarableObject> _radarableObjects = new List<RadarableObject>();
    private Transform player;
    private float _radarImageHalfHeight;

    private static Radar instance;

    public static Radar GetInstance() { return instance; }
    public Image GetRadarImage() { return radarImg; }

    private void Awake()
    {
      instance = this;
      _radarImageHalfHeight = radarImg.rectTransform.rect.height / 2;
    }

    private void Start()
    {
      RefreshPlayer();
    }

    public override void Subscribe()
    {
      Events.SceneLoaded += Show;
      Events.GamePaused += Hide;
      Events.GameResumed += Show;
      Events.PlayerDied += Hide;
    }

    public override void Unsubscribe()
    {
      Events.SceneLoaded -= Show;
      Events.GamePaused -= Hide;
      Events.GameResumed -= Show;
      Events.PlayerDied -= Hide;
    }

    private void Update()
    {
      RefreshPlayer();
      if (player == null)
        return;

      UpdateRadarableObjectsPositions();
    }

    /// <summary>
    /// 把雷达中心对齐到本机玩家；本机玩家尚未生成时保持为空。
    /// </summary>
    private void RefreshPlayer()
    {
      PlayerBehaviour localPlayer = PlayerBehaviour.GetLocalPlayer();
      if (localPlayer != null)
        player = localPlayer.transform;
    }

    private void UpdateRadarableObjectsPositions()
    {
      foreach (RadarableObject radarableObj in _radarableObjects)
      {
        Vector3 radarPos = (radarableObj.transform.position - player.position);
        float distToObject = Vector3.Distance(player.position, radarableObj.transform.position);

        if (distToObject > radarRadius)
          distToObject = _radarImageHalfHeight - radarableObj.ImageHalfHeight;
        else
          distToObject *= (_radarImageHalfHeight - radarableObj.ImageHalfHeight) / radarRadius;

        float deltaY = Mathf.Atan2(radarPos.x, radarPos.z) * Mathf.Rad2Deg - 270 - player.eulerAngles.y;
        radarPos.x = distToObject * Mathf.Cos(deltaY * Mathf.Deg2Rad) * -1;
        radarPos.y = distToObject * Mathf.Sin(deltaY * Mathf.Deg2Rad);

        radarableObj.SetRectLocalPosition(radarPos);
      }
    }

    public void AddRadarableObject(RadarableObject obj)
    {
      _radarableObjects.Add(obj);
    }

    public void RemoveRadarableObject(RadarableObject obj)
    {
      _radarableObjects.Remove(obj);
    }
  }
}
