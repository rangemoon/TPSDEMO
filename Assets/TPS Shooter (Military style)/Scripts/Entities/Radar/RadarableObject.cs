using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TPSShooter.UI
{
  // Add this script to GameObject that has to be displayed on the Radar.
  public class RadarableObject : MonoBehaviour
  {
    public GameObject RadarableImagePrefab;

    private static readonly List<RadarableObject> pendingObjects = new List<RadarableObject>();

    private Radar _radar;

    // Created GameObject from prefab
    private GameObject _radarableImgObject;

    // Used by Radar when it defines local position of GameObject created from Prefab
    private RectTransform _createdRectTransform;
    private bool _initialized;
    private bool _removedFromRadar;

    public void SetRectLocalPosition(Vector3 position)
    {
      if (_createdRectTransform == null)
        return;

      _createdRectTransform.localPosition = position;
    }

    public float ImageHalfHeight { get; private set; }

    private void OnValidate()
    {
      if (RadarableImagePrefab != null && RadarableImagePrefab.GetComponent<Image>() == null)
      {
        RadarableImagePrefab = null;
        Debug.LogError("RadarableObject: RadarableImagePrefab has to have Image Component.");
      }
    }

    private void Start()
    {
      TryInitializeRadarableObject();
    }

    private void Update()
    {
      if (_initialized || _removedFromRadar)
        return;

      TryInitializeRadarableObject();
    }

    protected virtual void OnDestroy()
    {
      DestroyRadarableObject();
    }

    /// <summary>
    /// Radar HUD 可能晚于敌人激活，把还没登记的对象补登记上去。
    /// </summary>
    public static void BindPendingToRadar()
    {
      if (pendingObjects.Count == 0)
        return;

      RadarableObject[] pending = pendingObjects.ToArray();
      pendingObjects.Clear();
      for (int i = 0; i < pending.Length; i++)
      {
        if (pending[i] != null)
          pending[i].TryInitializeRadarableObject();
      }
    }

    private void TryInitializeRadarableObject()
    {
      if (_initialized || _removedFromRadar)
        return;

      if (!CanShowOnRadar())
      {
        _removedFromRadar = true;
        pendingObjects.Remove(this);
        return;
      }

      _radar = Radar.GetInstance();
      if (_radar == null || _radar.GetRadarImage() == null)
      {
        if (!pendingObjects.Contains(this))
          pendingObjects.Add(this);
        return;
      }

      pendingObjects.Remove(this);

      if (RadarableImagePrefab == null)
      {
        Debug.LogError("RadarableObject: RadarableImagePrefab is missing.", this);
        return;
      }

      _radarableImgObject = Instantiate(RadarableImagePrefab);
      _radarableImgObject.transform.SetParent(_radar.GetRadarImage().transform);
      _radarableImgObject.transform.localScale = Vector3.one;

      _createdRectTransform = _radarableImgObject.GetComponent<Image>().rectTransform;

      ImageHalfHeight = RadarableImagePrefab.GetComponent<Image>().rectTransform.rect.height / 2;

      _radar.AddRadarableObject(this);
      _initialized = true;
    }

    /// <summary>
    /// 联机反序列化可能在 Start 之前就触发死亡，图标和 Radar 都还不存在。
    /// </summary>
    protected void DestroyRadarableObject()
    {
      _removedFromRadar = true;
      pendingObjects.Remove(this);

      if (_radarableImgObject != null)
      {
        Destroy(_radarableImgObject);
        _radarableImgObject = null;
      }

      _createdRectTransform = null;

      if (_radar != null)
      {
        _radar.RemoveRadarableObject(this);
        _radar = null;
      }

      _initialized = false;
    }

    /// <summary>
    /// 子类在实体已死亡时返回 false，避免加入端把尸体登记到小地图。
    /// </summary>
    protected virtual bool CanShowOnRadar()
    {
      return true;
    }
  }
}
