using UnityEngine;

using LightDev;

namespace TPSShooter
{
  public partial class ZombieBehaviour
  {
    private class DeathState : ZombieBehaviourState
    {
      public DeathState(ZombieBehaviour host) : base(host)
      {
      }

      public override void OnEnter()
      {
        if (host.characterController != null)
          host.characterController.enabled = false;
        if (host.pathAgent != null)
          host.pathAgent.enabled = false;

        if (host.animator != null)
          host.animator.SetTrigger(ZombieBehaviour.DeathHash);

        // Event
        host.onDied?.Invoke();
        Events.ZobmieKilled.Call(host);

        // Blood effects are now pooled and auto-despawn via PooledLifetime, no manual Destroy needed

        host.ScheduleDespawn(host.dieTime);
      }
    }
  }
}
