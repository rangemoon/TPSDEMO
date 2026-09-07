using System.Collections.Generic;
using UnityEngine;

namespace TPSShooter
{
    /// <summary>
    /// 联机/单机共用的敌人注册表：登记士兵与僵尸，供 Host 结算胜负时统计存活敌人。
    /// </summary>
    public static class EnemyRegistry
    {
        private static readonly List<EnemyBehaviour> soldiers = new List<EnemyBehaviour>();
        private static readonly List<ZombieBehaviour> zombies = new List<ZombieBehaviour>();

        /// <summary>
        /// 登记士兵敌人；重复登记会被忽略。
        /// </summary>
        /// <param name="enemy">要登记的士兵。</param>
        public static void Register(EnemyBehaviour enemy)
        {
            if (enemy == null || soldiers.Contains(enemy))
                return;

            soldiers.Add(enemy);
        }

        /// <summary>
        /// 从注册表移除士兵。
        /// </summary>
        /// <param name="enemy">要移除的士兵。</param>
        public static void Unregister(EnemyBehaviour enemy)
        {
            if (enemy == null)
                return;

            soldiers.Remove(enemy);
        }

        /// <summary>
        /// 登记僵尸敌人；重复登记会被忽略。
        /// </summary>
        /// <param name="zombie">要登记的僵尸。</param>
        public static void Register(ZombieBehaviour zombie)
        {
            if (zombie == null || zombies.Contains(zombie))
                return;

            zombies.Add(zombie);
        }

        /// <summary>
        /// 从注册表移除僵尸。
        /// </summary>
        /// <param name="zombie">要移除的僵尸。</param>
        public static void Unregister(ZombieBehaviour zombie)
        {
            if (zombie == null)
                return;

            zombies.Remove(zombie);
        }

        /// <summary>
        /// 是否仍有至少一名存活且激活的敌人（士兵或僵尸）。
        /// </summary>
        public static bool HasAliveEnemy()
        {
            for (int i = soldiers.Count - 1; i >= 0; i--)
            {
                EnemyBehaviour enemy = soldiers[i];
                if (enemy == null)
                {
                    soldiers.RemoveAt(i);
                    continue;
                }

                if (enemy.gameObject.activeInHierarchy && enemy.IsAlive())
                    return true;
            }

            for (int i = zombies.Count - 1; i >= 0; i--)
            {
                ZombieBehaviour zombie = zombies[i];
                if (zombie == null)
                {
                    zombies.RemoveAt(i);
                    continue;
                }

                if (zombie.gameObject.activeInHierarchy && zombie.IsAlive())
                    return true;
            }

            return false;
        }
    }
}
