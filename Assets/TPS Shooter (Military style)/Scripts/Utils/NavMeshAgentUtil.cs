using UnityEngine.AI;

namespace TPSShooter
{
    /// <summary>
    /// NavMeshAgent 未放上网格或已禁用时调用 Stop/Resume 会报错，联机客户端会先关掉 Agent。
    /// </summary>
    public static class NavMeshAgentUtil
    {
        /// <summary>
        /// 仅在 Agent 已启用且在 NavMesh 上时停止。
        /// </summary>
        /// <param name="agent">敌人身上的 NavMeshAgent。</param>
        public static void StopIfReady(NavMeshAgent agent)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                return;

            agent.isStopped = true;
            agent.velocity = UnityEngine.Vector3.zero;
        }

        /// <summary>
        /// 仅在 Agent 已启用且在 NavMesh 上时恢复。
        /// </summary>
        /// <param name="agent">敌人身上的 NavMeshAgent。</param>
        public static void ResumeIfReady(NavMeshAgent agent)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                return;

            agent.isStopped = false;
        }
    }
}
