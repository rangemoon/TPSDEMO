using System.Collections.Generic;
using UnityEngine;

namespace TPSShooter
{
    /// <summary>
    /// 场景级 XZ 网格：用地面射线和胶囊检测标记可行走格，供 A* 搜索。
    /// 不依赖 Unity NavMesh。每个关卡放一个即可；没有时 PathAgent 会自动建一个。
    /// </summary>
    public class PathfindingGrid : MonoBehaviour
    {
        public static PathfindingGrid Instance { get; private set; }

        [SerializeField] private float cellSize = 2f;
        [SerializeField] private float agentRadius = 0.4f;
        [SerializeField] private float agentHeight = 1.8f;
        [SerializeField] private float maxSlope = 50f;
        [SerializeField] private float stepHeight = 0.75f;
        [SerializeField] private int maxSearchNodes = 6000;
        [SerializeField] private bool fitToSceneColliders = true;
        [SerializeField] private Vector3 manualCenter;
        [SerializeField] private Vector2 manualSize = new Vector2(160f, 160f);
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField] private bool drawGizmos;

        private bool[] walkable;
        private float[] cellHeight;
        private int width;
        private int depth;
        private Vector3 origin;
        private bool built;

        private int[] searchG;
        private int[] searchParent;
        private int[] searchClosedStamp;
        private int[] searchSeenStamp;
        private int searchStamp;
        private readonly MinHeap openHeap = new MinHeap();
        private readonly List<Vector3> rawPath = new List<Vector3>(64);
        private readonly Collider[] overlapBuffer = new Collider[16];
        private readonly RaycastHit[] groundHits = new RaycastHit[12];

        private static readonly int[] NeighborDx = { -1, 0, 1, -1, 1, -1, 0, 1 };
        private static readonly int[] NeighborDz = { -1, -1, -1, 0, 0, 1, 1, 1 };

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Build();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// 取得场景网格；关卡里没有组件时运行时创建一个（用碰撞体包围盒）。
        /// </summary>
        public static PathfindingGrid EnsureInstance()
        {
            if (Instance != null)
                return Instance;

            if (!Application.isPlaying)
                return null;

            PathfindingGrid existing = FindFirstObjectByType<PathfindingGrid>(FindObjectsInactive.Exclude);
            if (existing != null)
            {
                Instance = existing;
                if (!existing.built)
                    existing.Build();
                return existing;
            }

            GameObject owner = new GameObject("PathfindingGrid");
            PathfindingGrid grid = owner.AddComponent<PathfindingGrid>();
            return grid;
        }

        /// <summary>
        /// 按当前包围盒重新扫描可行走格。
        /// </summary>
        public void Build()
        {
            Bounds bounds = ResolveBounds();
            origin = new Vector3(bounds.min.x, bounds.min.y, bounds.min.z);
            width = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / cellSize));
            depth = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / cellSize));

            int count = width * depth;
            walkable = new bool[count];
            cellHeight = new float[count];
            searchG = new int[count];
            searchParent = new int[count];
            searchClosedStamp = new int[count];
            searchSeenStamp = new int[count];
            searchStamp = 1;

            float rayStartY = bounds.max.y + 8f;
            float rayDistance = bounds.size.y + 16f;

            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = ToIndex(x, z);
                    Vector3 sample = CellCenter(x, z, bounds.center.y);
                    sample.y = rayStartY;

                    if (!TrySampleGround(sample, rayDistance, out RaycastHit hit))
                    {
                        walkable[index] = false;
                        continue;
                    }

                    if (Vector3.Angle(hit.normal, Vector3.up) > maxSlope)
                    {
                        walkable[index] = false;
                        continue;
                    }

                    if (IsBlockedByObstacle(hit.point))
                    {
                        walkable[index] = false;
                        continue;
                    }

                    walkable[index] = true;
                    cellHeight[index] = hit.point.y;
                }
            }

            built = true;
        }

        /// <summary>
        /// A* 搜索。结果写入 <paramref name="result"/>（世界坐标路点，含终点）。找不到路时返回 false。
        /// </summary>
        /// <param name="start">起点。</param>
        /// <param name="end">终点。</param>
        /// <param name="result">输出路点，会被清空。</param>
        public bool FindPath(Vector3 start, Vector3 end, List<Vector3> result)
        {
            result.Clear();
            if (!built)
                Build();

            int startIndex = FindNearestWalkable(start);
            int endIndex = FindNearestWalkable(end);
            if (startIndex < 0 || endIndex < 0)
                return false;

            if (startIndex == endIndex)
            {
                result.Add(WorldFromIndex(endIndex));
                return true;
            }

            searchStamp++;
            if (searchStamp == int.MaxValue)
            {
                System.Array.Clear(searchClosedStamp, 0, searchClosedStamp.Length);
                System.Array.Clear(searchSeenStamp, 0, searchSeenStamp.Length);
                searchStamp = 1;
            }

            searchSeenStamp[startIndex] = searchStamp;

            openHeap.Clear();
            searchG[startIndex] = 0;
            searchParent[startIndex] = startIndex;
            openHeap.Push(startIndex, Heuristic(startIndex, endIndex));

            int visited = 0;
            int found = -1;

            while (openHeap.Count > 0 && visited < maxSearchNodes)
            {
                int current = openHeap.Pop();
                if (searchClosedStamp[current] == searchStamp)
                    continue;

                searchClosedStamp[current] = searchStamp;
                visited++;

                if (current == endIndex)
                {
                    found = current;
                    break;
                }

                int cx = current % width;
                int cz = current / width;

                for (int i = 0; i < 8; i++)
                {
                    int nx = cx + NeighborDx[i];
                    int nz = cz + NeighborDz[i];
                    if (nx < 0 || nz < 0 || nx >= width || nz >= depth)
                        continue;

                    int neighbor = ToIndex(nx, nz);
                    if (!walkable[neighbor] || searchClosedStamp[neighbor] == searchStamp)
                        continue;

                    bool diagonal = NeighborDx[i] != 0 && NeighborDz[i] != 0;
                    if (diagonal && (!IsWalkable(cx, nz) || !IsWalkable(nx, cz)))
                        continue;

                    if (Mathf.Abs(cellHeight[neighbor] - cellHeight[current]) > stepHeight)
                        continue;

                    int stepCost = diagonal ? 14 : 10;
                    int tentativeG = searchG[current] + stepCost;
                    bool seen = searchSeenStamp[neighbor] == searchStamp;
                    if (seen && tentativeG >= searchG[neighbor])
                        continue;

                    searchSeenStamp[neighbor] = searchStamp;
                    searchG[neighbor] = tentativeG;
                    searchParent[neighbor] = current;
                    openHeap.Push(neighbor, tentativeG + Heuristic(neighbor, endIndex));
                }
            }

            if (found < 0)
                return false;

            rawPath.Clear();
            int node = found;
            while (true)
            {
                rawPath.Add(WorldFromIndex(node));
                if (node == startIndex)
                    break;
                node = searchParent[node];
            }

            rawPath.Reverse();
            SimplifyPath(rawPath, result);
            if (result.Count == 0)
                result.Add(end);
            else
                result[result.Count - 1] = new Vector3(end.x, result[result.Count - 1].y, end.z);

            return true;
        }

        /// <summary>
        /// 最近可行走格的世界坐标；没有则返回原点。
        /// </summary>
        public Vector3 ClampToWalkable(Vector3 position)
        {
            int index = FindNearestWalkable(position);
            if (index < 0)
                return position;

            return WorldFromIndex(index);
        }

        private Bounds ResolveBounds()
        {
            if (!fitToSceneColliders)
            {
                Vector3 size = new Vector3(Mathf.Max(8f, manualSize.x), 40f, Mathf.Max(8f, manualSize.y));
                return new Bounds(manualCenter, size);
            }

            bool hasBounds = false;
            Bounds bounds = new Bounds(transform.position, Vector3.one * 8f);

            Terrain[] terrains = FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain terrain = terrains[i];
                if (terrain == null || terrain.terrainData == null)
                    continue;

                Vector3 size = terrain.terrainData.size;
                Vector3 center = terrain.transform.position + size * 0.5f;
                Encapsulate(ref bounds, ref hasBounds, new Bounds(center, size));
            }

            Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;
                if (collider is CharacterController)
                    continue;
                if (collider.GetComponent<PathAgent>() != null)
                    continue;

                Encapsulate(ref bounds, ref hasBounds, collider.bounds);
            }

            if (!hasBounds)
            {
                Vector3 size = new Vector3(Mathf.Max(8f, manualSize.x), 40f, Mathf.Max(8f, manualSize.y));
                return new Bounds(manualCenter == Vector3.zero ? transform.position : manualCenter, size);
            }

            bounds.Expand(new Vector3(cellSize * 2f, 8f, cellSize * 2f));
            return bounds;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool hasBounds, Bounds extra)
        {
            if (!hasBounds)
            {
                bounds = extra;
                hasBounds = true;
                return;
            }

            bounds.Encapsulate(extra);
        }

        private bool IsBlockedByObstacle(Vector3 groundPoint)
        {
            Vector3 bottom = groundPoint + Vector3.up * (agentRadius + 0.08f);
            Vector3 top = groundPoint + Vector3.up * Mathf.Max(agentRadius + 0.1f, agentHeight - agentRadius);
            int hits = Physics.OverlapCapsuleNonAlloc(bottom, top, agentRadius, overlapBuffer, obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                Collider collider = overlapBuffer[i];
                if (collider == null || collider.isTrigger)
                    continue;
                if (collider is TerrainCollider)
                    continue;
                if (collider is CharacterController)
                    continue;
                if (collider.GetComponent<PathAgent>() != null)
                    continue;
                if (collider.GetComponent<PlayerBehaviour>() != null)
                    continue;

                return true;
            }

            return false;
        }

        /// <summary>
        /// 取最近的静态地面，忽略玩家/敌人碰撞，避免出生点把格子抬到角色头顶。
        /// </summary>
        private bool TrySampleGround(Vector3 originPoint, float rayDistance, out RaycastHit best)
        {
            best = default;
            int hits = Physics.RaycastNonAlloc(originPoint, Vector3.down, groundHits, rayDistance, groundMask, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            bool found = false;
            for (int i = 0; i < hits; i++)
            {
                RaycastHit hit = groundHits[i];
                if (hit.collider == null)
                    continue;
                if (ShouldIgnoreBakeCollider(hit.collider))
                    continue;
                if (hit.distance >= bestDistance)
                    continue;

                bestDistance = hit.distance;
                best = hit;
                found = true;
            }

            return found;
        }

        private static bool ShouldIgnoreBakeCollider(Collider collider)
        {
            if (collider is CharacterController)
                return true;
            if (collider.GetComponent<PathAgent>() != null)
                return true;
            if (collider.GetComponent<PlayerBehaviour>() != null)
                return true;
            return false;
        }

        private int FindNearestWalkable(Vector3 position)
        {
            int startX = Mathf.Clamp(Mathf.FloorToInt((position.x - origin.x) / cellSize), 0, width - 1);
            int startZ = Mathf.Clamp(Mathf.FloorToInt((position.z - origin.z) / cellSize), 0, depth - 1);
            int startIndex = ToIndex(startX, startZ);
            if (walkable[startIndex])
                return startIndex;

            int maxRadius = Mathf.Max(width, depth);
            for (int radius = 1; radius <= maxRadius; radius++)
            {
                int best = -1;
                float bestSqr = float.MaxValue;
                for (int z = startZ - radius; z <= startZ + radius; z++)
                {
                    for (int x = startX - radius; x <= startX + radius; x++)
                    {
                        if (x < 0 || z < 0 || x >= width || z >= depth)
                            continue;
                        if (Mathf.Abs(x - startX) != radius && Mathf.Abs(z - startZ) != radius)
                            continue;

                        int index = ToIndex(x, z);
                        if (!walkable[index])
                            continue;

                        float sqr = (WorldFromIndex(index) - position).sqrMagnitude;
                        if (sqr < bestSqr)
                        {
                            bestSqr = sqr;
                            best = index;
                        }
                    }
                }

                if (best >= 0)
                    return best;
            }

            return -1;
        }

        private void SimplifyPath(List<Vector3> source, List<Vector3> destination)
        {
            destination.Clear();
            if (source.Count == 0)
                return;

            int i = 0;
            destination.Add(source[0]);
            while (i < source.Count - 1)
            {
                int best = i + 1;
                for (int j = source.Count - 1; j > i + 1; j--)
                {
                    if (HasLineOfCells(source[i], source[j]))
                    {
                        best = j;
                        break;
                    }
                }

                destination.Add(source[best]);
                i = best;
            }
        }

        private bool HasLineOfCells(Vector3 from, Vector3 to)
        {
            float distance = Vector3.Distance(Flat(from), Flat(to));
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / (cellSize * 0.5f)));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 point = Vector3.Lerp(from, to, i / (float)steps);
                int x = Mathf.FloorToInt((point.x - origin.x) / cellSize);
                int z = Mathf.FloorToInt((point.z - origin.z) / cellSize);
                if (!IsWalkable(x, z))
                    return false;
            }

            return true;
        }

        private static Vector3 Flat(Vector3 value)
        {
            return new Vector3(value.x, 0f, value.z);
        }

        private int Heuristic(int from, int to)
        {
            int dx = Mathf.Abs(from % width - to % width);
            int dz = Mathf.Abs(from / width - to / width);
            int min = Mathf.Min(dx, dz);
            int max = Mathf.Max(dx, dz);
            return 14 * min + 10 * (max - min);
        }

        private bool IsWalkable(int x, int z)
        {
            if (x < 0 || z < 0 || x >= width || z >= depth)
                return false;
            return walkable[ToIndex(x, z)];
        }

        private int ToIndex(int x, int z)
        {
            return x + z * width;
        }

        private Vector3 CellCenter(int x, int z, float y)
        {
            return new Vector3(origin.x + (x + 0.5f) * cellSize, y, origin.z + (z + 0.5f) * cellSize);
        }

        private Vector3 WorldFromIndex(int index)
        {
            int x = index % width;
            int z = index / width;
            return new Vector3(origin.x + (x + 0.5f) * cellSize, cellHeight[index], origin.z + (z + 0.5f) * cellSize);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || walkable == null)
                return;

            Gizmos.color = new Color(0.2f, 0.8f, 0.3f, 0.25f);
            int drawn = 0;
            for (int i = 0; i < walkable.Length && drawn < 2500; i++)
            {
                if (!walkable[i])
                    continue;

                Gizmos.DrawCube(WorldFromIndex(i) + Vector3.up * 0.05f, new Vector3(cellSize * 0.85f, 0.04f, cellSize * 0.85f));
                drawn++;
            }
        }

        private sealed class MinHeap
        {
            private readonly List<int> nodes = new List<int>(256);
            private readonly List<int> keys = new List<int>(256);

            public int Count { get { return nodes.Count; } }

            public void Clear()
            {
                nodes.Clear();
                keys.Clear();
            }

            public void Push(int node, int key)
            {
                nodes.Add(node);
                keys.Add(key);
                BubbleUp(nodes.Count - 1);
            }

            public int Pop()
            {
                int result = nodes[0];
                int last = nodes.Count - 1;
                nodes[0] = nodes[last];
                keys[0] = keys[last];
                nodes.RemoveAt(last);
                keys.RemoveAt(last);
                if (nodes.Count > 0)
                    BubbleDown(0);
                return result;
            }

            private void BubbleUp(int index)
            {
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (keys[index] >= keys[parent])
                        break;
                    Swap(index, parent);
                    index = parent;
                }
            }

            private void BubbleDown(int index)
            {
                while (true)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    int smallest = index;
                    if (left < keys.Count && keys[left] < keys[smallest])
                        smallest = left;
                    if (right < keys.Count && keys[right] < keys[smallest])
                        smallest = right;
                    if (smallest == index)
                        break;
                    Swap(index, smallest);
                    index = smallest;
                }
            }

            private void Swap(int a, int b)
            {
                int node = nodes[a];
                nodes[a] = nodes[b];
                nodes[b] = node;
                int key = keys[a];
                keys[a] = keys[b];
                keys[b] = key;
            }
        }
    }
}
