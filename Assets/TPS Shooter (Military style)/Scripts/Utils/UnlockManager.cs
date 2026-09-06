using UnityEngine;

namespace TPSShooter
{
    /// <summary>
    /// Manages unlock state for weapons and locations.
    /// Persists via PlayerPrefs. Follows SaveLoad pattern.
    /// </summary>
    public static class UnlockManager
    {
        private const string WeaponUnlockPrefix = "WeaponUnlock_";
        private const string LocationUnlockPrefix = "LocationUnlock_";

        // Default unlocked items
        private static readonly string[] DefaultUnlockedWeapons = { "自动步枪" };
        private static readonly string[] DefaultUnlockedLocations = { "2" };

        // ---------- Weapon ----------

        /// <summary>
        /// Returns true if the weapon with the given tag is unlocked.
        /// If never explicitly set, checks against default list.
        /// </summary>
        public static bool IsWeaponUnlocked(string weaponTag)
        {
            string key = WeaponUnlockPrefix + weaponTag;
            if (PlayerPrefs.HasKey(key))
            {
                return PlayerPrefs.GetInt(key, 0) == 1;
            }
            for (int i = 0; i < DefaultUnlockedWeapons.Length; i++)
            {
                if (DefaultUnlockedWeapons[i] == weaponTag) return true;
            }
            return false;
        }

        /// <summary>
        /// Unlocks the weapon with the given tag and saves immediately.
        /// </summary>
        public static void UnlockWeapon(string weaponTag)
        {
            PlayerPrefs.SetInt(WeaponUnlockPrefix + weaponTag, 1);
            PlayerPrefs.Save();
        }

        // ---------- Location ----------

        /// <summary>
        /// Returns true if the location with the given scene index is unlocked.
        /// </summary>
        public static bool IsLocationUnlocked(int sceneIndex)
        {
            string key = LocationUnlockPrefix + sceneIndex.ToString();
            if (PlayerPrefs.HasKey(key))
            {
                return PlayerPrefs.GetInt(key, 0) == 1;
            }
            for (int i = 0; i < DefaultUnlockedLocations.Length; i++)
            {
                if (DefaultUnlockedLocations[i] == sceneIndex.ToString()) return true;
            }
            return false;
        }

        /// <summary>
        /// Unlocks the location with the given scene index and saves immediately.
        /// </summary>
        public static void UnlockLocation(int sceneIndex)
        {
            PlayerPrefs.SetInt(LocationUnlockPrefix + sceneIndex.ToString(), 1);
            PlayerPrefs.Save();
        }
    }
}
