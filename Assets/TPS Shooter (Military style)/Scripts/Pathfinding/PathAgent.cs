using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace TPSShooter
{
    /// <summary>
    /// 用网格 A* 替代 NavMeshAgent：状态机仍用 speed / destination / steeringTarget 这套接口。
    /// 水平移动走 CharacterController；联机客户端应关掉本组件。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(80)]
    [RequireComponent(typeof(CharacterController))]
    public class PathAgent : MonoBehaviour
    {
        public float speed = 5f;
        public float acceleration = 20f;
        public bool autoBraking;

        [SerializeField] private float waypointReachDistance = 0.55f;
        [SerializeField] private float repathInterval = 0.28f;

        private CharacterController characterController;
        private readonly List<Vector3> path = new List<Vector3>(32);
        private int waypointIndex;
        private float currentSpeed;
        private float nextRepathTime;
        private bool hasDestination;
        private Vector3 requestedDestination;
        private bool stopped = true;

        /// <summary>
        /// 当前寻路终点；赋值等价于 SetDestination。
        /// </summary>
        public Vector3 destination
        {
            get { return requestedDestination; }
            set { SetDestination(value); }
        }

        /// <summary>
        /// 下一个路点，供转向使用。没有路时看向终点。
        /// </summary>
        public Vector3 steeringTarget
        {
            get
            {
                Vector3 look = requestedDestination;
                if (waypointIndex < path.Count)
                    look = path[waypointIndex];
                look.y = transform.position.y;
                return look;
            }
        }

        /// <summary>
        /// 为 true 时停在原地，不清除已有路径。
        /// </summary>
        public bool isStopped
        {
            get { return stopped; }
            set
            {
                stopped = value;
                if (stopped)
                    currentSpeed = 0f;
            }
        }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            if (Application.isPlaying)
                DisableLegacyNavMeshAgent();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            PathfindingGrid.EnsureInstance();
        }

        /// <summary>
        /// 请求走到世界坐标。会钳到最近可行走格并刷新路径。
        /// </summary>
        /// <param name="worldPosition">目标点。</param>
        public void SetDestination(Vector3 worldPosition)
        {
            requestedDestination = worldPosition;
            hasDestination = true;
            stopped = false;
            TryRepath(true);
        }

        /// <summary>
        /// 停止移动。未启用时直接返回，避免联机客户端报错。
        /// </summary>
        public void StopIfReady()
        {
            if (!isActiveAndEnabled)
                return;

            isStopped = true;
        }

        /// <summary>
        /// 恢复沿当前终点移动。
        /// </summary>
        public void ResumeIfReady()
        {
            if (!isActiveAndEnabled)
                return;

            isStopped = false;
            if (hasDestination)
                TryRepath(false);
        }

        private void Update()
        {
            if (GameManager.IsGamePaused || GameManager.IsGameFinished)
                return;
            if (GameNetwork.IsClientOnly)
                return;
            if (stopped || !hasDestination)
                return;
            if (characterController == null || !characterController.enabled)
                return;

            TryRepath(false);
            FollowPath();
        }

        private void TryRepath(bool force)
        {
            if (!hasDestination)
                return;
            if (!force && Time.time < nextRepathTime)
                return;

            PathfindingGrid grid = PathfindingGrid.EnsureInstance();
            if (grid == null)
                return;

            nextRepathTime = Time.time + repathInterval;
            path.Clear();
            waypointIndex = 0;
            grid.FindPath(transform.position, requestedDestination, path);
        }

        private void FollowPath()
        {
            if (path.Count == 0)
            {
                currentSpeed = 0f;
                return;
            }

            if (waypointIndex >= path.Count)
                waypointIndex = path.Count - 1;

            Vector3 target = path[waypointIndex];

            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (distance <= waypointReachDistance)
            {
                if (waypointIndex < path.Count - 1)
                {
                    waypointIndex++;
                    return;
                }

                currentSpeed = 0f;
                return;
            }

            float desired = speed;
            if (autoBraking)
            {
                Vector3 toEnd = requestedDestination - transform.position;
                toEnd.y = 0f;
                desired = Mathf.Min(speed, toEnd.magnitude / 0.35f);
            }

            currentSpeed = Mathf.MoveTowards(currentSpeed, desired, acceleration * Time.deltaTime);
            Vector3 motion = toTarget.normalized * currentSpeed * Time.deltaTime;
            if (characterController.isGrounded)
                motion.y = -2f * Time.deltaTime;
            characterController.Move(motion);
        }

        /// <summary>
        /// 旧 Prefab 上可能还挂着 NavMeshAgent，关掉以免和 A* 抢位移。
        /// </summary>
        private void DisableLegacyNavMeshAgent()
        {
            NavMeshAgent navMeshAgent = GetComponent<NavMeshAgent>();
            if (navMeshAgent == null)
                return;

            navMeshAgent.enabled = false;
        }
    }
}
