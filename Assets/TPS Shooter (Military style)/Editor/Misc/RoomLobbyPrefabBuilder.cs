#if UNITY_EDITOR
using TMPro;
using TPSShooter.UI.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using LightDev.UI;
using Mirror.Discovery;

namespace TPSShooter
{
    /// <summary>
    /// 生成菜单大厅 prefab，并放进 Menu 场景的 Canvas 下，由 CanvasManager 控制显隐。
    /// </summary>
    public static class RoomLobbyPrefabBuilder
    {
        public const string PrefabPath = "Assets/TPS Shooter (Military style)/Prefabs/UI/RoomLobby.prefab";
        private const string MenuScenePath = "Assets/TPS Shooter (Military style)/Scenes/Menu.unity";
        private const string FontPath = "Assets/Fonts/DouyinSansBold SDF.asset";

        [InitializeOnLoadMethod]
        private static void EnsureOnLoad()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.delayCall += TryInjectIntoOpenScene;
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            EditorApplication.delayCall += TryInjectIntoOpenScene;
        }

        private static void TryInjectIntoOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            EnsurePrefab();
            if (Object.FindAnyObjectByType<CanvasManager>(FindObjectsInactive.Include) == null)
                return;
            if (Object.FindAnyObjectByType<LocationChoose>(FindObjectsInactive.Include) == null)
                return;

            RoomLobby existing = Object.FindAnyObjectByType<RoomLobby>(FindObjectsInactive.Include);
            if (existing != null)
            {
                BindSceneReferences(existing);
                return;
            }
            AddToOpenScene();
        }

        public static void SetupInMenuScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            string currentPath = SceneManager.GetActiveScene().path;
            Scene menu = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
            AddToOpenScene();
            EditorSceneManager.MarkSceneDirty(menu);
            EditorSceneManager.SaveScene(menu);

            if (!string.IsNullOrEmpty(currentPath) && currentPath != MenuScenePath)
                EditorSceneManager.OpenScene(currentPath, OpenSceneMode.Single);

            Debug.Log("已在 Menu 场景放入 RoomLobby 大厅。Play → 选武器 → 创建/加入房间。");
        }

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
            Debug.Log("已创建 RoomLobby prefab：" + PrefabPath);
            return prefab;
        }

        public static void AddToOpenScene()
        {
            GameObject prefab = EnsurePrefab();
            if (prefab == null)
                return;

            RoomLobby existing = Object.FindAnyObjectByType<RoomLobby>(FindObjectsInactive.Include);
            if (existing != null)
            {
                BindSceneReferences(existing);
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            CanvasManager canvasManager = Object.FindAnyObjectByType<CanvasManager>(FindObjectsInactive.Include);
            if (canvasManager == null)
            {
                Debug.LogError("当前场景没有 CanvasManager，无法放入 RoomLobby。请打开 Menu 场景。");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvasManager.gameObject.scene);
            instance.name = "RoomLobby";
            instance.transform.SetParent(canvasManager.transform, false);
            Stretch(instance.GetComponent<RectTransform>());
            Undo.RegisterCreatedObjectUndo(instance, "Add Room Lobby");

            RoomLobby lobby = instance.GetComponent<RoomLobby>();
            BindSceneReferences(lobby);
            DisableDebugHud();
            EditorSceneManager.MarkSceneDirty(instance.scene);
            Selection.activeGameObject = instance;
        }

        private static void BindSceneReferences(RoomLobby lobby)
        {
            if (lobby == null)
                return;

            SerializedObject so = new SerializedObject(lobby);
            bool changed = false;

            SerializedProperty locationProperty = so.FindProperty("locationSource");
            if (locationProperty.objectReferenceValue == null)
            {
                LocationChoose locationChoose = Object.FindAnyObjectByType<LocationChoose>(FindObjectsInactive.Include);
                if (locationChoose != null)
                {
                    locationProperty.objectReferenceValue = locationChoose;
                    changed = true;
                }
            }

            SerializedProperty discoveryProperty = so.FindProperty("networkDiscovery");
            if (discoveryProperty.objectReferenceValue == null)
            {
                NetworkDiscovery discovery = Object.FindAnyObjectByType<NetworkDiscovery>(FindObjectsInactive.Include);
                if (discovery != null)
                {
                    discoveryProperty.objectReferenceValue = discovery;
                    changed = true;
                }
            }

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(lobby.gameObject.scene);
            }

            DisableDebugHud();
        }

        private static void DisableDebugHud()
        {
            GameLanHud hud = Object.FindAnyObjectByType<GameLanHud>(FindObjectsInactive.Include);
            if (hud == null)
                return;

            SerializedObject so = new SerializedObject(hud);
            SerializedProperty enableProperty = so.FindProperty("enableDebugHud");
            if (enableProperty != null && enableProperty.boolValue)
            {
                enableProperty.boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (hud.enabled)
                hud.enabled = false;
        }

        private static void EnsureDirectory()
        {
            if (!AssetDatabase.IsValidFolder("Assets/TPS Shooter (Military style)/Prefabs/UI"))
                AssetDatabase.CreateFolder("Assets/TPS Shooter (Military style)/Prefabs", "UI");
        }

        private static GameObject BuildHierarchy(TMP_FontAsset font)
        {
            Sprite panelSprite = LoadUiSprite();
            GameObject root = CreateUiObject("RoomLobby", null);
            Stretch(root.GetComponent<RectTransform>());
            Image dim = root.AddComponent<Image>();
            dim.color = new Color(0.02f, 0.03f, 0.04f, 0.82f);
            dim.raycastTarget = true;

            RoomLobby lobby = root.AddComponent<RoomLobby>();

            Button backButton = CreateButton(root.transform, "BackButton", "返回", font, panelSprite, new Vector2(180f, 64f));
            RectTransform backRect = backButton.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0f, 1f);
            backRect.anchorMax = new Vector2(0f, 1f);
            backRect.pivot = new Vector2(0f, 1f);
            backRect.anchoredPosition = new Vector2(40f, -32f);

            TextMeshProUGUI title = CreateLabel(root.transform, "Title", "多人游戏", 56f, font);
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(720f, 72f);
            titleRect.anchoredPosition = new Vector2(0f, -28f);

            GameObject tabBar = CreateUiObject("TabBar", root.transform);
            RectTransform tabRect = tabBar.GetComponent<RectTransform>();
            tabRect.anchorMin = new Vector2(0.5f, 1f);
            tabRect.anchorMax = new Vector2(0.5f, 1f);
            tabRect.pivot = new Vector2(0.5f, 1f);
            tabRect.sizeDelta = new Vector2(640f, 72f);
            tabRect.anchoredPosition = new Vector2(0f, -110f);
            HorizontalLayoutGroup tabLayout = tabBar.AddComponent<HorizontalLayoutGroup>();
            tabLayout.spacing = 24f;
            tabLayout.childAlignment = TextAnchor.MiddleCenter;
            tabLayout.childControlWidth = true;
            tabLayout.childControlHeight = true;
            tabLayout.childForceExpandWidth = true;
            tabLayout.childForceExpandHeight = true;

            Button createTab = CreateButton(tabBar.transform, "CreateTabButton", "创建房间", font, panelSprite, new Vector2(280f, 64f));
            Button joinTab = CreateButton(tabBar.transform, "JoinTabButton", "加入房间", font, panelSprite, new Vector2(280f, 64f));

            GameObject createPanel = CreateUiObject("CreatePanel", root.transform);
            Stretch(createPanel.GetComponent<RectTransform>());
            createPanel.GetComponent<RectTransform>().offsetMin = new Vector2(80f, 80f);
            createPanel.GetComponent<RectTransform>().offsetMax = new Vector2(-80f, -200f);

            GameObject cardsRow = CreateUiObject("MapCards", createPanel.transform);
            RectTransform cardsRect = cardsRow.GetComponent<RectTransform>();
            cardsRect.anchorMin = new Vector2(0.5f, 0.55f);
            cardsRect.anchorMax = new Vector2(0.5f, 0.55f);
            cardsRect.pivot = new Vector2(0.5f, 0.5f);
            cardsRect.sizeDelta = new Vector2(1500f, 420f);
            HorizontalLayoutGroup cardsLayout = cardsRow.AddComponent<HorizontalLayoutGroup>();
            cardsLayout.spacing = 36f;
            cardsLayout.childAlignment = TextAnchor.MiddleCenter;
            cardsLayout.childControlWidth = true;
            cardsLayout.childControlHeight = true;
            cardsLayout.childForceExpandWidth = true;
            cardsLayout.childForceExpandHeight = true;

            RoomLobby.MapCard[] mapCards = new RoomLobby.MapCard[3];
            for (int i = 0; i < 3; i++)
                mapCards[i] = CreateMapCard(cardsRow.transform, "MapCard" + (i + 1), "地图 " + (i + 1), i + 1, font, panelSprite);

            TextMeshProUGUI selectedMapLabel = CreateLabel(createPanel.transform, "SelectedMapLabel", "当前地图：", 32f, font);
            RectTransform selectedRect = selectedMapLabel.rectTransform;
            selectedRect.anchorMin = new Vector2(0.5f, 0f);
            selectedRect.anchorMax = new Vector2(0.5f, 0f);
            selectedRect.pivot = new Vector2(0.5f, 0f);
            selectedRect.sizeDelta = new Vector2(800f, 48f);
            selectedRect.anchoredPosition = new Vector2(0f, 110f);

            Button createRoomButton = CreateButton(createPanel.transform, "CreateRoomButton", "创建房间并进入", font, panelSprite, new Vector2(360f, 80f));
            RectTransform createRect = createRoomButton.GetComponent<RectTransform>();
            createRect.anchorMin = new Vector2(0.5f, 0f);
            createRect.anchorMax = new Vector2(0.5f, 0f);
            createRect.pivot = new Vector2(0.5f, 0f);
            createRect.anchoredPosition = new Vector2(0f, 16f);

            GameObject joinPanel = CreateUiObject("JoinPanel", root.transform);
            Stretch(joinPanel.GetComponent<RectTransform>());
            joinPanel.GetComponent<RectTransform>().offsetMin = new Vector2(220f, 80f);
            joinPanel.GetComponent<RectTransform>().offsetMax = new Vector2(-220f, -200f);
            Image joinBg = joinPanel.AddComponent<Image>();
            joinBg.sprite = panelSprite;
            joinBg.type = Image.Type.Sliced;
            joinBg.color = new Color(0.08f, 0.1f, 0.12f, 0.92f);

            TextMeshProUGUI statusLabel = CreateLabel(joinPanel.transform, "StatusLabel", "正在搜索局域网房间…", 28f, font);
            RectTransform statusRect = statusLabel.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.sizeDelta = new Vector2(-48f, 48f);
            statusRect.anchoredPosition = new Vector2(0f, -20f);

            Button searchButton = CreateButton(joinPanel.transform, "SearchButton", "搜索房间", font, panelSprite, new Vector2(220f, 56f));
            RectTransform searchRect = searchButton.GetComponent<RectTransform>();
            searchRect.anchorMin = new Vector2(1f, 1f);
            searchRect.anchorMax = new Vector2(1f, 1f);
            searchRect.pivot = new Vector2(1f, 1f);
            searchRect.anchoredPosition = new Vector2(-24f, -16f);

            GameObject listRoot = CreateScrollList(joinPanel.transform, font, panelSprite, out RoomListEntry entryPrefab);
            entryPrefab.transform.SetParent(root.transform, false);
            entryPrefab.gameObject.SetActive(false);

            TMP_InputField addressInput = CreateInputField(joinPanel.transform, "AddressInput", "localhost", font, panelSprite);
            RectTransform addressRect = addressInput.GetComponent<RectTransform>();
            addressRect.anchorMin = new Vector2(0f, 0f);
            addressRect.anchorMax = new Vector2(1f, 0f);
            addressRect.pivot = new Vector2(0.5f, 0f);
            addressRect.sizeDelta = new Vector2(-280f, 64f);
            addressRect.anchoredPosition = new Vector2(-90f, 24f);

            Button joinAddressButton = CreateButton(joinPanel.transform, "JoinAddressButton", "加入 IP", font, panelSprite, new Vector2(180f, 64f));
            RectTransform joinAddressRect = joinAddressButton.GetComponent<RectTransform>();
            joinAddressRect.anchorMin = new Vector2(1f, 0f);
            joinAddressRect.anchorMax = new Vector2(1f, 0f);
            joinAddressRect.pivot = new Vector2(1f, 0f);
            joinAddressRect.anchoredPosition = new Vector2(-24f, 24f);

            joinPanel.SetActive(false);
            entryPrefab.gameObject.SetActive(false);

            SerializedObject so = new SerializedObject(lobby);
            so.FindProperty("createTabButton").objectReferenceValue = createTab;
            so.FindProperty("joinTabButton").objectReferenceValue = joinTab;
            so.FindProperty("createPanel").objectReferenceValue = createPanel;
            so.FindProperty("joinPanel").objectReferenceValue = joinPanel;
            so.FindProperty("createRoomButton").objectReferenceValue = createRoomButton;
            so.FindProperty("selectedMapLabel").objectReferenceValue = selectedMapLabel;
            so.FindProperty("addressInput").objectReferenceValue = addressInput;
            so.FindProperty("searchButton").objectReferenceValue = searchButton;
            so.FindProperty("joinAddressButton").objectReferenceValue = joinAddressButton;
            so.FindProperty("roomListRoot").objectReferenceValue = listRoot.transform;
            so.FindProperty("roomEntryPrefab").objectReferenceValue = entryPrefab;
            so.FindProperty("statusLabel").objectReferenceValue = statusLabel;
            so.FindProperty("backButton").objectReferenceValue = backButton;

            SerializedProperty cardsProp = so.FindProperty("mapCards");
            cardsProp.arraySize = mapCards.Length;
            for (int i = 0; i < mapCards.Length; i++)
            {
                SerializedProperty card = cardsProp.GetArrayElementAtIndex(i);
                card.FindPropertyRelative("gameObject").objectReferenceValue = mapCards[i].gameObject;
                card.FindPropertyRelative("button").objectReferenceValue = mapCards[i].button;
                card.FindPropertyRelative("image").objectReferenceValue = mapCards[i].image;
                card.FindPropertyRelative("nameLabel").objectReferenceValue = mapCards[i].nameLabel;
                card.FindPropertyRelative("selectedHighlight").objectReferenceValue = mapCards[i].selectedHighlight;
                card.FindPropertyRelative("sceneIndex").intValue = mapCards[i].sceneIndex;
                card.FindPropertyRelative("displayName").stringValue = mapCards[i].displayName;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        private static RoomLobby.MapCard CreateMapCard(Transform parent, string name, string caption, int sceneIndex, TMP_FontAsset font, Sprite sprite)
        {
            GameObject card = CreateUiObject(name, parent);
            Image background = card.AddComponent<Image>();
            background.sprite = sprite;
            background.type = Image.Type.Sliced;
            background.color = new Color(0.12f, 0.14f, 0.16f, 0.95f);
            Button button = card.AddComponent<Button>();
            button.targetGraphic = background;
            LayoutElement layout = card.AddComponent<LayoutElement>();
            layout.minWidth = 360f;
            layout.minHeight = 400f;

            GameObject highlight = CreateUiObject("SelectedHighlight", card.transform);
            Stretch(highlight.GetComponent<RectTransform>());
            Image highlightImage = highlight.AddComponent<Image>();
            highlightImage.sprite = sprite;
            highlightImage.type = Image.Type.Sliced;
            highlightImage.color = new Color(0.95f, 0.78f, 0.28f, 0.35f);
            highlight.SetActive(sceneIndex == 1);

            GameObject preview = CreateUiObject("Preview", card.transform);
            RectTransform previewRect = preview.GetComponent<RectTransform>();
            previewRect.anchorMin = new Vector2(0.08f, 0.28f);
            previewRect.anchorMax = new Vector2(0.92f, 0.92f);
            previewRect.offsetMin = Vector2.zero;
            previewRect.offsetMax = Vector2.zero;
            Image previewImage = preview.AddComponent<Image>();
            previewImage.color = new Color(0.25f, 0.28f, 0.3f, 1f);
            previewImage.preserveAspect = true;

            TextMeshProUGUI nameLabel = CreateLabel(card.transform, "Name", caption, 30f, font);
            RectTransform nameRect = nameLabel.rectTransform;
            nameRect.anchorMin = new Vector2(0.06f, 0.04f);
            nameRect.anchorMax = new Vector2(0.94f, 0.24f);
            nameRect.offsetMin = Vector2.zero;
            nameRect.offsetMax = Vector2.zero;

            return new RoomLobby.MapCard
            {
                gameObject = card,
                button = button,
                image = previewImage,
                nameLabel = nameLabel,
                selectedHighlight = highlight,
                sceneIndex = sceneIndex,
                displayName = caption
            };
        }

        private static GameObject CreateScrollList(Transform parent, TMP_FontAsset font, Sprite sprite, out RoomListEntry entryPrefab)
        {
            GameObject scrollObject = CreateUiObject("RoomList", parent);
            RectTransform scrollRectTransform = scrollObject.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = new Vector2(24f, 108f);
            scrollRectTransform.offsetMax = new Vector2(-24f, -80f);
            Image scrollImage = scrollObject.AddComponent<Image>();
            scrollImage.color = new Color(0f, 0f, 0f, 0.25f);
            ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            GameObject viewport = CreateUiObject("Viewport", scrollObject.transform);
            Stretch(viewport.GetComponent<RectTransform>());
            viewport.AddComponent<RectMask2D>();
            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);

            GameObject content = CreateUiObject("Content", viewport.transform);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 12f;
            contentLayout.padding = new RectOffset(8, 8, 8, 8);
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = false;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRect;

            GameObject entryObject = CreateUiObject("RoomEntry", content.transform);
            LayoutElement entryLayout = entryObject.AddComponent<LayoutElement>();
            entryLayout.preferredHeight = 72f;
            Image entryImage = entryObject.AddComponent<Image>();
            entryImage.sprite = sprite;
            entryImage.type = Image.Type.Sliced;
            entryImage.color = new Color(0.16f, 0.18f, 0.2f, 0.95f);
            Button entryButton = entryObject.AddComponent<Button>();
            entryButton.targetGraphic = entryImage;
            TextMeshProUGUI entryLabel = CreateLabel(entryObject.transform, "Label", "房间", 26f, font);
            Stretch(entryLabel.rectTransform);
            entryLabel.rectTransform.offsetMin = new Vector2(16f, 4f);
            entryLabel.rectTransform.offsetMax = new Vector2(-16f, -4f);
            entryLabel.alignment = TextAlignmentOptions.MidlineLeft;

            RoomListEntry entry = entryObject.AddComponent<RoomListEntry>();
            SerializedObject entrySo = new SerializedObject(entry);
            entrySo.FindProperty("joinButton").objectReferenceValue = entryButton;
            entrySo.FindProperty("label").objectReferenceValue = entryLabel;
            entrySo.ApplyModifiedPropertiesWithoutUndo();
            entryPrefab = entry;

            return content;
        }

        private static TMP_InputField CreateInputField(Transform parent, string name, string value, TMP_FontAsset font, Sprite sprite)
        {
            GameObject inputObject = CreateUiObject(name, parent);
            Image image = inputObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);
            TMP_InputField input = inputObject.AddComponent<TMP_InputField>();

            GameObject textArea = CreateUiObject("Text Area", inputObject.transform);
            Stretch(textArea.GetComponent<RectTransform>());
            textArea.GetComponent<RectTransform>().offsetMin = new Vector2(12f, 6f);
            textArea.GetComponent<RectTransform>().offsetMax = new Vector2(-12f, -6f);
            textArea.AddComponent<RectMask2D>();

            TextMeshProUGUI text = CreateLabel(textArea.transform, "Text", value, 28f, font);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = true;

            TextMeshProUGUI placeholder = CreateLabel(textArea.transform, "Placeholder", "输入 IP，本机填 localhost", 26f, font);
            Stretch(placeholder.rectTransform);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.color = new Color(1f, 1f, 1f, 0.4f);

            input.textViewport = textArea.GetComponent<RectTransform>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.text = value;
            input.caretColor = Color.white;
            return input;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            if (parent != null)
                go.transform.SetParent(parent, false);
            return go;
        }

        private static Sprite LoadUiSprite()
        {
            Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            if (sprite == null)
                sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            return sprite;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float fontSize, TMP_FontAsset font)
        {
            GameObject labelObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            label.enableWordWrapping = true;
            if (font != null)
                label.font = font;
            return label;
        }

        private static Button CreateButton(Transform parent, string name, string caption, TMP_FontAsset font, Sprite sprite, Vector2 size)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.16f, 0.18f, 0.2f, 0.95f);
            image.sprite = sprite;
            if (sprite != null)
                image.type = Image.Type.Sliced;

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            TextMeshProUGUI label = CreateLabel(buttonObject.transform, "Text", caption, 28f, font);
            Stretch(label.rectTransform);
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
