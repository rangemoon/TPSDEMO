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
        host.characterController.enabled = false;
        host.navmeshAgent.enabled = false;

        host.animator.SetTrigger(ZombieBehaviour.DeathHash);

        // Event
        host.onDied?.Invoke();
        Events.ZobmieKilled.Call(host);

        // Blood effects are now pooled and auto-despawn via PooledLifetime, no manual Destroy needed

        Destroy(host.gameObject, host.dieTime);
      }
    }
  }
}
