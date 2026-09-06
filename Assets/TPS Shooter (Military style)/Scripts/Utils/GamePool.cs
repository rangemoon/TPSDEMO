using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace TPSShooter
{
    /// <summary>
    /// Lightweight generic object pool for GameObjects.
    /// Usage:
    ///   var obj = GamePool.Spawn(prefab, position, rotation, parent);
    ///   GamePool.Despawn(obj);
    ///   GamePool.WarmUp(prefab, count);  // pre-create objects to avoid first-frame hitches
    /// Call GamePool.ClearAll() on scene unload to release pooled objects.
    /// </summary>
    public static class GamePool
    {
        private static readonly Dictionary<int, ObjectPool<GameObject>> _pools = new Dictionary<int, ObjectPool<GameObject>>();
        private static readonly Dictionary<GameObject, int> _objToPool = new Dictionary<GameObject, int>();

        /// <summary>
        /// Spawns an object from pool, or creates a new one if pool is empty.
        /// </summary>
        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            int key = prefab.GetInstanceID();
            var pool = GetOrCreatePool(key, prefab);

            GameObject obj = pool.Get();
            obj.transform.SetPositionAndRotation(position, rotation);
            if (parent != null) obj.transform.SetParent(parent);
            return obj;
        }

        /// <summary>
        /// Returns an object to pool. Falls back to Destroy if object is not from a pool.
        /// </summary>
        public static void Despawn(GameObject obj)
        {
            if (obj == null) return;

            if (_objToPool.TryGetValue(obj, out int key) && _pools.TryGetValue(key, out var pool))
            {
                pool.Release(obj);
            }
            else
            {
                Object.Destroy(obj);
            }
        }

        /// <summary>
        /// Pre-creates objects and puts them into the pool so the first Spawn calls
        /// don't need to Instantiate (avoids first-frame hitches).
        /// Call during scene loading / initialization, NOT during gameplay.
        /// </summary>
        public static void WarmUp(GameObject prefab, int count)
        {
            if (prefab == null || count <= 0) return;

            int key = prefab.GetInstanceID();
            var pool = GetOrCreatePool(key, prefab);

            for (int i = 0; i < count; i++)
            {
                GameObject obj = pool.Get();
                obj.SetActive(false);
                pool.Release(obj);
            }
        }

        /// <summary>
        /// Clears all pools and destroys pooled objects. Call on scene unload.
        /// </summary>
        public static void ClearAll()
        {
            foreach (var pool in _pools.Values)
            {
                pool.Clear();
            }
            _pools.Clear();
            _objToPool.Clear();
        }

        private static ObjectPool<GameObject> GetOrCreatePool(int key, GameObject prefab)
        {
            if (!_pools.TryGetValue(key, out var pool))
            {
                pool = new ObjectPool<GameObject>(
                  createFunc: () => Object.Instantiate(prefab),
                  actionOnGet: obj =>
                  {
                      obj.SetActive(true);
                      _objToPool[obj] = key;
                  },
                  actionOnRelease: obj =>
                  {
                      obj.SetActive(false);
                  },
                  defaultCapacity: 20,
                  maxSize: 200
                );
                _pools[key] = pool;
            }
            return pool;
        }
    }
}
