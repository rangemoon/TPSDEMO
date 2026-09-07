#if UNITY_EDITOR
using TMPro;
using TPSShooter.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TPSShooter
{
    /// <summary>
    /// 生成可编辑的 MatchOverlay prefab，并放进游戏场景。字体在 prefab 上改，不要再代码拼 UI。
    /// </summary>
    public static class MatchOverlayPrefabBuilder
    {
        public const string PrefabPath = "Assets/TPS Shooter (Military style)/Prefabs/UI/MatchOverlay.prefab";
        private const string FontPath = "Assets/Fonts/DouyinSansBold SDF.asset";

        [InitializeOnLoadMethod]
        private static void EnsurePrefabOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                EnsurePrefab();
                if (Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include) == null)
                    return;
                if (Object.FindAnyObjectByType<MatchOverlayUI>(FindObjectsInactive.Include) != null)
                    return;
                AddToOpenScene();
            };
        }

        [MenuItem("TPS Shooter/Create Match Overlay Prefab", false, 25)]
        public static GameObject EnsurePrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
                return existing;

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            GameObject root = BuildHierarchy(font);
            EnsureDirectory();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("已创建 MatchOverlay prefab：" + PrefabPath + "。请在 Inspector 里改字体和文案。");
            return prefab;
        }

        public static void AddToOpenScene()
        {
            GameObject prefab = EnsurePrefab();
            if (prefab == null)
                return;

            MatchOverlayUI existing = Object.FindAnyObjectByType<MatchOverlayUI>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("当前场景已有 MatchOverlay。");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "MatchOverlay";
            Undo.RegisterCreatedObjectUndo(instance, "Add Match Overlay");
            EditorSceneManager.MarkSceneDirty(instance.scene);
            Selection.activeGameObject = instance;
        }

        public static void SetupInGameScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            GameObject prefab = EnsurePrefab();
            if (prefab == null)
                return;

            string currentPath = SceneManager.GetActiveScene().path;
            int placed = 0;

            for (int i = 1; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(scenePath))
                    continue;

                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                if (Object.FindAnyObjectByType<MatchOverlayUI>(FindObjectsInactive.Include) != null)
                    continue;
                if (Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include) == null)
                    continue;

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = "MatchOverlay";
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                placed++;
            }

            if (!string.IsNullOrEmpty(currentPath))
                EditorSceneManager.OpenScene(currentPath, OpenSceneMode.Single);

            Debug.Log("已在 " + placed + " 个游戏场景放入 MatchOverlay。");
        }

        private static void EnsureDirectory()
        {
            if (!AssetDatabase.IsValidFolder("Assets/TPS Shooter (Military style)/Prefabs/UI"))
                AssetDatabase.CreateFolder("Assets/TPS Shooter (Military style)/Prefabs", "UI");
        }

        private static GameObject BuildHierarchy(TMP_FontAsset font)
        {
            GameObject root = new GameObject("MatchOverlay");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();

            Sprite panelSprite = LoadUiSprite();

            GameObject waitRoot = CreatePanel(root.transform, "WaitPanel", new Color(0f, 0f, 0f, 0.45f), panelSprite);
            TextMeshProUGUI waitLabel = CreateLabel(waitRoot.transform, "WaitLabel", "等待其他玩家结束游戏", 48f, font);

            GameObject resultRoot = CreatePanel(root.transform, "ResultPanel", new Color(0f, 0f, 0f, 0.65f), panelSprite);
            TextMeshProUGUI resultLabel = CreateLabel(resultRoot.transform, "ResultLabel", string.Empty, 72f, font);

            GameObject continueRoot = new GameObject("ContinueButtons", typeof(RectTransform));
            continueRoot.transform.SetParent(resultRoot.transform, false);
            RectTransform continueRect = continueRoot.GetComponent<RectTransform>();
            Stretch(continueRect);
            continueRect.offsetMin = new Vector2(0f, 80f);
            continueRect.offsetMax = new Vector2(0f, -220f);

            HorizontalLayoutGroup layout = continueRoot.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 40f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            Button replayButton = CreateButton(continueRoot.transform, "ReplayButton", "重新开始", font, panelSprite);
            Button homeButton = CreateButton(continueRoot.transform, "HomeButton", "返回主页", font, panelSprite);

            waitRoot.SetActive(false);
            continueRoot.SetActive(false);
            resultRoot.SetActive(false);

            MatchOverlayUI overlay = root.AddComponent<MatchOverlayUI>();
            overlay.waitRoot = waitRoot;
            overlay.waitLabel = waitLabel;
            overlay.resultRoot = resultRoot;
            overlay.resultLabel = resultLabel;
            overlay.continueRoot = continueRoot;
            overlay.replayButton = replayButton;
            overlay.homeButton = homeButton;
            return root;
        }

        private static Sprite LoadUiSprite()
        {
            Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            if (sprite == null)
                sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            return sprite;
        }

        private static GameObject CreatePanel(Transform parent, string name, Color color, Sprite sprite)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(parent, false);
            Image image = panel.GetComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            if (sprite != null)
                image.type = Image.Type.Sliced;
            Stretch(panel.GetComponent<RectTransform>());
            return panel;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float fontSize, TMP_FontAsset font)
        {
            GameObject labelObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            Stretch(rect);
            rect.offsetMin = new Vector2(40f, 40f);
            rect.offsetMax = new Vector2(-40f, -40f);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.fontStyle = FontStyles.Normal;
            label.enableWordWrapping = true;
            label.raycastTarget = false;
            if (font != null)
                label.font = font;
            return label;
        }

        private static Button CreateButton(Transform parent, string name, string caption, TMP_FontAsset font, Sprite sprite)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(280f, 72f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
            image.sprite = sprite;
            if (sprite != null)
                image.type = Image.Type.Sliced;

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(buttonObject.transform, false);
            Stretch(textObject.GetComponent<RectTransform>());
            TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
            label.text = caption;
            label.fontSize = 32f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.fontStyle = FontStyles.Normal;
            label.raycastTarget = false;
            if (font != null)
                label.font = font;

            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
#endif
