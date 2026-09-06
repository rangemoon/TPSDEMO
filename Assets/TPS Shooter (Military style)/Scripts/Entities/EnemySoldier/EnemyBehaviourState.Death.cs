using UnityEngine;

using LightDev;

namespace TPSShooter
{
  public partial class EnemyBehaviour
  {
    private class DeathState : EnemyBehaviourState
    {
      public DeathState(EnemyBehaviour host) : base(host)
      {
      }

      public override void OnEnter()
      {
        host.characterController.enabled = false;
        host.navmeshAgent.enabled = false;

#if UNITY_ANDROID || UNITY_IOS
        // Mobile: skip ragdoll physics, always use death animation for performance
        PlayDeathAnimation();
#else
        if (host.DeathSettings.IsRagdolled)
        {
          host.animator.enabled = false;

          // Use cached rigidbodies instead of GetComponentsInChildren
          foreach (Rigidbody b in host.cachedRigidbodies)
            b.isKinematic = false;
        }
        else
        {
          PlayDeathAnimation();
        }
#endif

        // unattach gameObjects
        for (int i = 0; i < host.DeathSettings.Items.Length; i++)
        {
          host.DeathSettings.Items[i].parent = null;
          var rb = host.DeathSettings.Items[i].GetComponent<Rigidbody>();
          if (rb != null) rb.isKinematic = false;
          Destroy(host.DeathSettings.Items[i].gameObject, host.DeathSettings.EnemyDieTime);
        }

        // Event
        host.onDied?.Invoke();
        Events.EnemyKilled.Call(host);

        // Blood effects are now pooled and auto-despawn via PooledLifetime, no manual Destroy needed

        Destroy(host.gameObject, host.DeathSettings.EnemyDieTime);
      }

      private void PlayDeathAnimation()
      {
        host.SetForwardAnimatorParameter(0);
        host.SetStrafeAnimatorParameter(0);
        host.animator.SetTrigger(host.AnimatorParameters.DieHash);
      }
    }
  }
}
