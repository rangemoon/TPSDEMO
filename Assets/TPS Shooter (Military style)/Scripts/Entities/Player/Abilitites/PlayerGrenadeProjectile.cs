using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using LightDev;

namespace TPSShooter
{
  public class PlayerGrenadeProjectile : MonoBehaviour
  {
    public LineRenderer lineRenderer;

    private PlayerBehaviour ownerPlayer;
    private Coroutine throwCoroutine;
    private const float timeStep = 0.06f;

    private void Awake()
    {
      ownerPlayer = GetComponent<PlayerBehaviour>();
      if (ownerPlayer == null)
        ownerPlayer = GetComponentInParent<PlayerBehaviour>();

      if (lineRenderer != null)
      {
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 0;
      }
    }

    private void OnEnable()
    {
      Events.PlayerStartGrenadeThrow += OnPlayerStartGrenadeThrow;
      Events.PlayerFinishGreandeThrow += OnPlayerFinishGreandeThrow;
    }

    private void OnDisable()
    {
      Events.PlayerStartGrenadeThrow -= OnPlayerStartGrenadeThrow;
      Events.PlayerFinishGreandeThrow -= OnPlayerFinishGreandeThrow;
      StopThrowPreview();
    }

    private void OnDestroy()
    {
      Events.PlayerStartGrenadeThrow -= OnPlayerStartGrenadeThrow;
      Events.PlayerFinishGreandeThrow -= OnPlayerFinishGreandeThrow;
    }

    private void OnPlayerStartGrenadeThrow()
    {
      // 联机时场景里被关掉的 Player 仍可能曾订阅全局事件；未激活物体不能 StartCoroutine。
      if (!CanPreview())
        return;

      StopThrowPreview();
      throwCoroutine = StartCoroutine(UpdateProjectile());
    }

    private void OnPlayerFinishGreandeThrow()
    {
      StopThrowPreview();
    }

    private bool CanPreview()
    {
      if (!isActiveAndEnabled || lineRenderer == null)
        return false;
      if (ownerPlayer == null || !ownerPlayer.IsLocalPlayer)
        return false;
      return ownerPlayer.grenadeSettings != null && ownerPlayer.grenadeSettings.GrenadePosition != null;
    }

    private void StopThrowPreview()
    {
      if (throwCoroutine != null)
      {
        StopCoroutine(throwCoroutine);
        throwCoroutine = null;
      }

      if (lineRenderer != null)
        lineRenderer.positionCount = 0;
    }

    private IEnumerator UpdateProjectile()
    {
      while (true)
      {
        if (!CanPreview())
        {
          StopThrowPreview();
          yield break;
        }

        Vector3 startPoint = ownerPlayer.grenadeSettings.GrenadePosition.position;
        Vector3 velocity = ownerPlayer.GetGreandeVelocity();
        Vector3[] positions = CalculateGrenadePositions(startPoint, velocity);

        lineRenderer.positionCount = positions.Length;
        lineRenderer.SetPositions(positions);

        yield return null;
      }
    }

    private Vector3[] CalculateGrenadePositions(Vector3 startPoint, Vector3 velocity)
    {
      List<Vector3> positions = new List<Vector3>() { startPoint };

      float time = timeStep;
      Vector3 nextPosition = CalculatePositionInTime(startPoint, velocity, time);
      LayerMask layers = ownerPlayer.weaponSettings.shootingLayers;
      RaycastHit hit;
      while (Physics.Linecast(positions[positions.Count - 1], nextPosition, out hit, layers) == false)
      {
        positions.Add(nextPosition);
        time += timeStep;
        nextPosition = CalculatePositionInTime(startPoint, velocity, time);
      }
      positions.Add(hit.point);

      return positions.ToArray();
    }

    private Vector3 CalculatePositionInTime(Vector3 startPoint, Vector3 velocity, float time)
    {
      startPoint.x += velocity.x * time;
      startPoint.z += velocity.z * time;
      startPoint.y += velocity.y * time + 0.5f * Physics.gravity.y * time * time;

      return startPoint;
    }
  }
}
