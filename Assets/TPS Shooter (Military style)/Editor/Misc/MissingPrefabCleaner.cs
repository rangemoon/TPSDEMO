#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TPSShooter
{
    /// <summary>
    /// 删除场景里丢失的 Prefab 实例（例如已移除的 Finish）。
    /// </summary>
    public static class MissingPrefabCleaner
    {
        [InitializeOnLoadMethod]
        private static void AutoCleanOpenScene()
        {
            EditorSceneManager.sceneOpened += (scene, mode) =>
            {
                EditorApplication.delayCall += () => CleanOpenScene(false);
            };
            EditorApplication.delayCall += () => CleanOpenScene(false);
        }

        [MenuItem("TPS Shooter/Remove Missing Prefab Instances", false, 27)]
        private static void CleanFromMenu()
        {
            int removed = CleanOpenScene(true);
            Debug.Log("已从当前场景移除 " + removed + " 个 Missing Prefab 实例。");
        }

        [MenuItem("TPS Shooter/Remove Missing Prefabs In Game Scenes", false, 28)]
        private static void CleanAllGameScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            string currentPath = SceneManager.GetActiveScene().path;
            int total = 0;
            for (int i = 1; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(scenePath))
                    continue;

                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                int removed = CleanOpenScene(true);
                if (removed > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    total += removed;
                }
            }

            if (!string.IsNullOrEmpty(currentPath))
                EditorSceneManager.OpenScene(currentPath, OpenSceneMode.Single);

            Debug.Log("已从游戏场景移除 " + total + " 个 Missing Prefab 实例。");
        }

        private static int CleanOpenScene(bool logEach)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return 0;

            Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int removed = 0;
            for (int i = transforms.Length - 1; i >= 0; i--)
            {
                if (transforms[i] == null)
                    continue;

                GameObject go = transforms[i].gameObject;
                if (!IsMissingPrefabInstance(go))
                    continue;

                if (logEach)
                    Debug.Log("移除丢失预制体：" + go.name, go);

                Undo.DestroyObjectImmediate(go);
                removed++;
            }

            if (removed > 0)
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            return removed;
        }

        private static bool IsMissingPrefabInstance(GameObject go)
        {
            if (go == null)
                return false;

            if (go.name.Contains("Missing Prefab"))
                return true;

            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go))
                return false;

            return PrefabUtility.IsPrefabAssetMissing(go);
        }
    }
}
#endif
