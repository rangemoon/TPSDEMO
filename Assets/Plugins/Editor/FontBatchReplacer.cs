#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

// ============================================================================
// Font Batch Replacer
// ============================================================================
    public class FontBatchReplacer : EditorWindow
    {
        private static readonly string[] DefaultExcludedAssetFolderPaths = new string[0];

        private DefaultAsset targetFolder;
 
        private Font targetTextFont;
        private TMP_FontAsset targetTMPFontAsset;
        private Vector2 windowScroll;
        private bool autoSaveTextChanges;
        private bool openSceneWhenSelecting = true;
        private float sceneTextScrollHeight = 260f;
        private int selectedSceneTextEntryFlatIndex = -1;
        private int currentSceneTextEntryDrawIndex;
        private bool focusSelectedSceneTextEntryTextArea;
        private bool scrollSelectedSceneTextEntryIntoView;
        private readonly List<SceneTextCache> sceneTextCaches = new List<SceneTextCache>();
        private const string SceneTextEntryControlNamePrefix = "SceneTextEntry_";
        private const float SceneTextEntryScrollPadding = 8f;

        private DefaultAsset scriptSearchFolder;
        private Vector2 codeTextSearchScroll;
        private float codeTextSearchScrollHeight = 260f;
        private bool excludePackageLikeFolders = true;
        private bool skipCommentAndStringTextMatches = true;
        private bool autoSaveCodeLineChanges;
        private bool customExcludedFoldersFoldout = true;
        private readonly List<DefaultAsset> customExcludedFolders = new List<DefaultAsset>();
        private readonly List<CodeTextSearchResult> codeTextSearchResults = new List<CodeTextSearchResult>();
        private const string CustomExcludedFoldersEditorPrefsKeyPrefix = "FontBatchReplacer.CustomExcludedFolders.";

        private enum SceneTextKind
        {
            Text,
            TextMeshPro
        }

        private class SceneTextEntry
        {
            public SceneTextKind kind;
            public string scenePath;
            public string hierarchyPath;
            public string hierarchyIndexPath;
            public int componentIndex;
            public string originalText;
            public string editedText;
            public bool dirty;
            public readonly Stack<string> undoTextHistory = new Stack<string>();
            public readonly Stack<string> redoTextHistory = new Stack<string>();
        }

        private class SceneTextCache
        {
            public int buildIndex;
            public bool enabled;
            public string scenePath;
            public string sceneName;
            public Vector2 scroll;
            public readonly List<SceneTextEntry> textEntries = new List<SceneTextEntry>();
            public readonly List<SceneTextEntry> tmpEntries = new List<SceneTextEntry>();
        }

        private class CodeTextSearchResult
        {
            public string scriptPath;
            public int lineNumber;
            public int columnNumber;
            public string originalLineContent;
            public string editableLineContent;
            public bool dirty;
            public MonoScript scriptAsset;
        }
 
        [MenuItem("Tools/字体替换")]
        public static void OpenWindow()
        {
            FontBatchReplacer window = GetWindow<FontBatchReplacer>();
            window.titleContent = new GUIContent("字体替换");
            window.minSize = new Vector2(900, 520);
            window.Show();
        }
 
        private void OnEnable()
        {
            if (targetFolder == null)
            {
                targetFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
            }

            if (scriptSearchFolder == null)
            {
                scriptSearchFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
            }

            LoadCustomExcludedFolders();
        }

        private void OnDisable()
        {
            SaveCustomExcludedFolders();
        }
 
        private void OnGUI()
        {
            windowScroll = EditorGUILayout.BeginScrollView(windowScroll);
            EditorGUILayout.Space(8);
 
            EditorGUILayout.LabelField("批量替换字体工具", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "功能：\n\n" +
                "1. 批量替换当前打开场景中的 UnityEngine.UI.Text 字体。\n" +
                "2. 批量替换指定文件夹下 Prefab 中的 UnityEngine.UI.Text 字体。\n" +
                "3. 批量替换项目中所有 Scene 里的 UnityEngine.UI.Text 字体。\n\n" +
                "4. 批量替换当前打开场景中的 TextMeshProUGUI Font Asset。\n" +
                "5. 批量替换指定文件夹下 Prefab 中的 TextMeshProUGUI Font Asset。\n" +
                "6. 批量替换项目中所有 Scene 里的 TextMeshProUGUI Font Asset。\n\n" +
                "如果没有选择文件夹，则默认扫描整个 Assets 文件夹。\n" +
                "每一条替换结果都会输出到 Console。\n\n" +
                "重要：批量替换所有场景前，建议先备份项目或提交 Git。\n\n\n",
                MessageType.Info
            );
 
            EditorGUILayout.Space(8);
 
            targetFolder = DrawFolderAssetPicker(
                "Prefab目标文件夹",
                targetFolder,
                "选择 Prefab 目标文件夹",
                "Assets",
                true
            );
 
            string folderPath = GetTargetFolderPath();
            EditorGUILayout.LabelField("当前 Prefab 扫描路径", folderPath);
 
            EditorGUILayout.Space(12);
 
            DrawTextReplacementGUI();
 
            EditorGUILayout.Space(12);
 
            DrawTMPReplacementGUI();

            EditorGUILayout.Space(14);
            DrawSceneTextEditorGUI();

            EditorGUILayout.Space(14);
            DrawCodeTextSearchGUI();
            EditorGUILayout.EndScrollView();
        }
 
        private void DrawTextReplacementGUI()
        {
            EditorGUILayout.LabelField("Unity UI Text 字体替换", EditorStyles.boldLabel);
 
            targetTextFont = EditorGUILayout.ObjectField(
                "目标 Font",
                targetTextFont,
                typeof(Font),
                false
            ) as Font;
 
            using (new EditorGUI.DisabledScope(targetTextFont == null))
            {
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("1. 替换当前场景 Text 字体", GUILayout.Height(32)))
                    {
                        ReplaceSceneTextFont();
                    }
 
                    if (GUILayout.Button("2. 替换文件夹下 Prefab Text 字体", GUILayout.Height(32)))
                    {
                        ReplacePrefabTextFont();
                    }
                }
 
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("5. 替换所有场景 Text 字体", GUILayout.Height(32)))
                    {
                        ReplaceAllScenesTextFont();
                    }
                }
            }
 
            if (targetTextFont == null)
            {
                EditorGUILayout.HelpBox("请选择一个目标 Font。", MessageType.Warning);
            }
        }
 
        private void DrawTMPReplacementGUI()
        {
            EditorGUILayout.LabelField("TextMeshProUGUI Font Asset 替换", EditorStyles.boldLabel);
 
            targetTMPFontAsset = EditorGUILayout.ObjectField(
                "目标 TMP Font Asset",
                targetTMPFontAsset,
                typeof(TMP_FontAsset),
                false
            ) as TMP_FontAsset;
 
            using (new EditorGUI.DisabledScope(targetTMPFontAsset == null))
            {
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("3. 替换当前场景 TMP Font Asset", GUILayout.Height(32)))
                    {
                        ReplaceSceneTMPFontAsset();
                    }
 
                    if (GUILayout.Button("4. 替换文件夹下 Prefab TMP Font Asset", GUILayout.Height(32)))
                    {
                        ReplacePrefabTMPFontAsset();
                    }
                }
 
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("6. 替换所有场景 TMP Font Asset", GUILayout.Height(32)))
                    {
                        ReplaceAllScenesTMPFontAsset();
                    }
                }
            }
 
            if (targetTMPFontAsset == null)
            {
                EditorGUILayout.HelpBox("请选择一个目标 TMP Font Asset。", MessageType.Warning);
            }
        }
 
        private void DrawSceneTextEditorGUI()
        {
            EditorGUILayout.LabelField("场景文本编辑", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "按 Build Settings 顺序扫描场景。每个场景先显示 Text，再显示 TextMeshPro。未勾选自动保存时，文本更改会先缓存，点击“应用所有更改”后统一写入场景。",
                MessageType.Info
            );

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("刷新 Build Settings 场景文本", GUILayout.Height(28)))
                {
                    RefreshSceneTextCache();
                }

                using (new EditorGUI.DisabledScope(GetDirtySceneTextEntryCount() == 0))
                {
                    if (GUILayout.Button("应用所有更改", GUILayout.Height(28), GUILayout.Width(150)))
                    {
                        ApplyAllSceneTextChanges(true);
                    }
                }
            }

            using (new GUILayout.HorizontalScope())
            {
                autoSaveTextChanges = EditorGUILayout.ToggleLeft("修改后自动保存", autoSaveTextChanges, GUILayout.Width(150));
                openSceneWhenSelecting = EditorGUILayout.ToggleLeft("定位对象时切换场景", openSceneWhenSelecting, GUILayout.Width(170));
                EditorGUILayout.LabelField("场景滚动高度", GUILayout.Width(90));
                sceneTextScrollHeight = EditorGUILayout.Slider(sceneTextScrollHeight, 160f, 520f);
            }

            EditorGUILayout.LabelField(string.Format("场景数量: {0}    未应用修改: {1}", sceneTextCaches.Count, GetDirtySceneTextEntryCount()));

            if (sceneTextCaches.Count == 0)
            {
                EditorGUILayout.HelpBox("还没有缓存场景文本。请点击“刷新 Build Settings 场景文本”。", MessageType.Warning);
                return;
            }

            HandleSceneTextUndoShortcuts();
            HandleSceneTextKeyboardNavigation();
            currentSceneTextEntryDrawIndex = 0;
            for (int i = 0; i < sceneTextCaches.Count; i++)
            {
                DrawSceneTextCache(sceneTextCaches[i]);
                EditorGUILayout.Space(12);
            }

        }

        private void DrawSceneTextCache(SceneTextCache sceneCache)
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string state = sceneCache.enabled ? "启用" : "未启用";
                EditorGUILayout.LabelField(string.Format("#{0}  {1}  ({2})", sceneCache.buildIndex, sceneCache.sceneName, state), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(sceneCache.scenePath, EditorStyles.miniLabel);
                EditorGUILayout.LabelField(string.Format("Text: {0}    TextMeshPro: {1}", sceneCache.textEntries.Count, sceneCache.tmpEntries.Count));

                sceneCache.scroll = EditorGUILayout.BeginScrollView(sceneCache.scroll, GUILayout.Height(sceneTextScrollHeight));
                DrawSceneTextSection("Text", sceneCache.textEntries, sceneCache);
                EditorGUILayout.Space(14);
                DrawSceneTextSection("TextMeshPro", sceneCache.tmpEntries, sceneCache);
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSceneTextSection(string title, List<SceneTextEntry> entries, SceneTextCache sceneCache)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (entries.Count == 0)
            {
                EditorGUILayout.LabelField("无", EditorStyles.miniLabel);
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                DrawSceneTextEntry(entries[i], sceneCache, i + 1);
            }
        }

        private void DrawSceneTextEntry(SceneTextEntry entry, SceneTextCache sceneCache, int displayIndex)
        {
            int entryFlatIndex = currentSceneTextEntryDrawIndex++;
            bool selected = entryFlatIndex == selectedSceneTextEntryFlatIndex;
            Color previousBackgroundColor = GUI.backgroundColor;
            if (selected)
            {
                GUI.backgroundColor = new Color(0.65f, 0.8f, 1f, 1f);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUI.backgroundColor = previousBackgroundColor;
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(string.Format("{0}{1}. {2}", entry.dirty ? "* " : string.Empty, displayIndex, entry.hierarchyPath), EditorStyles.miniLabel);

                    if (GUILayout.Button("定位", GUILayout.Width(70)))
                    {
                        selectedSceneTextEntryFlatIndex = entryFlatIndex;
                        SelectSceneTextEntry(entry);
                    }

                    using (new EditorGUI.DisabledScope(!entry.dirty))
                    {
                        if (GUILayout.Button("应用", GUILayout.Width(65)))
                        {
                            ApplySingleSceneTextEntry(entry, true);
                        }
                    }
                }

                string controlName = GetSceneTextEntryControlName(entryFlatIndex);
                EditorGUI.BeginChangeCheck();
                GUI.SetNextControlName(controlName);
                if (focusSelectedSceneTextEntryTextArea && selected)
                {
                    FocusSceneTextEntryTextArea(controlName);
                }

                string newText = EditorGUILayout.TextArea(entry.editedText ?? string.Empty, GUILayout.MinHeight(36));
                if (focusSelectedSceneTextEntryTextArea && selected)
                {
                    FocusSceneTextEntryTextArea(controlName);
                }

                if (GUI.GetNameOfFocusedControl() == controlName)
                {
                    selectedSceneTextEntryFlatIndex = entryFlatIndex;
                    focusSelectedSceneTextEntryTextArea = false;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    if (newText != entry.editedText)
                    {
                        entry.undoTextHistory.Push(entry.editedText ?? string.Empty);
                        entry.redoTextHistory.Clear();
                    }

                    entry.editedText = newText;
                    entry.dirty = entry.editedText != entry.originalText;

                    if (autoSaveTextChanges && entry.dirty)
                    {
                        ApplySingleSceneTextEntry(entry, true);
                    }
                }
            }

            GUI.backgroundColor = previousBackgroundColor;
            if (selected && scrollSelectedSceneTextEntryIntoView && Event.current != null && Event.current.type == EventType.Repaint)
            {
                ScrollSceneTextEntryRectIntoView(sceneCache, GUILayoutUtility.GetLastRect());
                scrollSelectedSceneTextEntryIntoView = false;
            }
        }

        private void FocusSceneTextEntryTextArea(string controlName)
        {
            GUI.FocusControl(controlName);
            EditorGUI.FocusTextInControl(controlName);
            EditorGUIUtility.editingTextField = true;
        }

        private void HandleSceneTextUndoShortcuts()
        {
            Event currentEvent = Event.current;
            if (currentEvent == null || currentEvent.type != EventType.KeyDown)
            {
                return;
            }

            if (!currentEvent.control && !currentEvent.command)
            {
                return;
            }

            string focusedControlName = GUI.GetNameOfFocusedControl();
            if (string.IsNullOrEmpty(focusedControlName) || !focusedControlName.StartsWith(SceneTextEntryControlNamePrefix))
            {
                return;
            }

            bool handled = false;
            if (currentEvent.keyCode == KeyCode.Z)
            {
                handled = currentEvent.shift ? RedoSelectedSceneTextEntry() : UndoSelectedSceneTextEntry();
            }
            else if (currentEvent.keyCode == KeyCode.Y)
            {
                handled = RedoSelectedSceneTextEntry();
            }

            if (handled)
            {
                currentEvent.Use();
            }
        }

        private bool UndoSelectedSceneTextEntry()
        {
            SceneTextEntry entry;
            if (!TryGetSceneTextEntryByFlatIndex(selectedSceneTextEntryFlatIndex, out entry) || entry.undoTextHistory.Count == 0)
            {
                return false;
            }

            entry.redoTextHistory.Push(entry.editedText ?? string.Empty);
            entry.editedText = entry.undoTextHistory.Pop();
            entry.dirty = entry.editedText != entry.originalText;
            focusSelectedSceneTextEntryTextArea = true;
            Repaint();
            return true;
        }

        private bool RedoSelectedSceneTextEntry()
        {
            SceneTextEntry entry;
            if (!TryGetSceneTextEntryByFlatIndex(selectedSceneTextEntryFlatIndex, out entry) || entry.redoTextHistory.Count == 0)
            {
                return false;
            }

            entry.undoTextHistory.Push(entry.editedText ?? string.Empty);
            entry.editedText = entry.redoTextHistory.Pop();
            entry.dirty = entry.editedText != entry.originalText;
            focusSelectedSceneTextEntryTextArea = true;
            Repaint();
            return true;
        }

        private void HandleSceneTextKeyboardNavigation()
        {
            Event currentEvent = Event.current;
            if (currentEvent == null || currentEvent.type != EventType.KeyDown)
            {
                return;
            }

            int moveDirection = 0;
            if (currentEvent.keyCode == KeyCode.DownArrow)
            {
                moveDirection = 1;
            }
            else if (currentEvent.keyCode == KeyCode.UpArrow)
            {
                moveDirection = -1;
            }

            if (moveDirection == 0)
            {
                return;
            }

            string focusedControlName = GUI.GetNameOfFocusedControl();
            if (!string.IsNullOrEmpty(focusedControlName) && !focusedControlName.StartsWith(SceneTextEntryControlNamePrefix))
            {
                return;
            }

            MoveSelectedSceneTextEntry(moveDirection);
            currentEvent.Use();
        }

        private void MoveSelectedSceneTextEntry(int moveDirection)
        {
            int entryCount = GetSceneTextEntryCount();
            if (entryCount <= 0)
            {
                selectedSceneTextEntryFlatIndex = -1;
                return;
            }

            if (selectedSceneTextEntryFlatIndex < 0)
            {
                selectedSceneTextEntryFlatIndex = moveDirection >= 0 ? 0 : entryCount - 1;
            }
            else
            {
                selectedSceneTextEntryFlatIndex = Mathf.Clamp(selectedSceneTextEntryFlatIndex + moveDirection, 0, entryCount - 1);
            }

            focusSelectedSceneTextEntryTextArea = true;
            scrollSelectedSceneTextEntryIntoView = true;
            Repaint();
        }

        private void ScrollSceneTextEntryRectIntoView(SceneTextCache sceneCache, Rect entryRect)
        {
            if (sceneCache == null)
            {
                return;
            }

            float oldScrollY = sceneCache.scroll.y;
            float targetScrollY = oldScrollY;
            float visibleTopY = oldScrollY + SceneTextEntryScrollPadding;
            float visibleBottomY = oldScrollY + sceneTextScrollHeight - SceneTextEntryScrollPadding;

            if (entryRect.yMin < visibleTopY)
            {
                targetScrollY = Mathf.Max(0f, entryRect.yMin - SceneTextEntryScrollPadding);
            }
            else if (entryRect.yMax > visibleBottomY)
            {
                targetScrollY = Mathf.Max(0f, entryRect.yMax - sceneTextScrollHeight + SceneTextEntryScrollPadding);
            }

            if (!Mathf.Approximately(oldScrollY, targetScrollY))
            {
                sceneCache.scroll.y = targetScrollY;
                Repaint();
            }
        }

        private int GetSceneTextEntryCount()
        {
            int count = 0;
            for (int i = 0; i < sceneTextCaches.Count; i++)
            {
                count += sceneTextCaches[i].textEntries.Count;
                count += sceneTextCaches[i].tmpEntries.Count;
            }

            return count;
        }

        private bool TryGetSceneTextEntryByFlatIndex(int flatIndex, out SceneTextEntry entry)
        {
            entry = null;
            if (flatIndex < 0)
            {
                return false;
            }

            int currentIndex = 0;
            for (int i = 0; i < sceneTextCaches.Count; i++)
            {
                if (TryGetSceneTextEntryByFlatIndex(sceneTextCaches[i].textEntries, flatIndex, ref currentIndex, out entry))
                {
                    return true;
                }

                if (TryGetSceneTextEntryByFlatIndex(sceneTextCaches[i].tmpEntries, flatIndex, ref currentIndex, out entry))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetSceneTextEntryByFlatIndex(List<SceneTextEntry> entries, int flatIndex, ref int currentIndex, out SceneTextEntry entry)
        {
            entry = null;
            for (int i = 0; i < entries.Count; i++)
            {
                if (currentIndex == flatIndex)
                {
                    entry = entries[i];
                    return true;
                }

                currentIndex++;
            }

            return false;
        }

        private string GetSceneTextEntryControlName(int entryFlatIndex)
        {
            return SceneTextEntryControlNamePrefix + entryFlatIndex;
        }

        private void RefreshSceneTextCache()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            sceneTextCaches.Clear();
            List<string> openedScenePaths = GetOpenedScenePaths();
            string activeScenePath = SceneManager.GetActiveScene().path;
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;

            try
            {
                for (int i = 0; i < buildScenes.Length; i++)
                {
                    string scenePath = buildScenes[i].path;
                    if (string.IsNullOrEmpty(scenePath) || !File.Exists(scenePath))
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar("场景文本编辑", "正在扫描 " + scenePath, (float)i / buildScenes.Length);
                    Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    SceneTextCache sceneCache = new SceneTextCache();
                    sceneCache.buildIndex = i;
                    sceneCache.enabled = buildScenes[i].enabled;
                    sceneCache.scenePath = scenePath;
                    sceneCache.sceneName = Path.GetFileNameWithoutExtension(scenePath);
                    CollectSceneTextEntries(scene, sceneCache);
                    sceneTextCaches.Add(sceneCache);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                RestoreOpenedScenes(openedScenePaths, activeScenePath);
            }

            selectedSceneTextEntryFlatIndex = GetSceneTextEntryCount() > 0 ? 0 : -1;
            focusSelectedSceneTextEntryTextArea = selectedSceneTextEntryFlatIndex >= 0;
            scrollSelectedSceneTextEntryIntoView = selectedSceneTextEntryFlatIndex >= 0;
            Repaint();
            Debug.Log("[FontBatchReplacer] 场景文本扫描完成。场景数量: " + sceneTextCaches.Count);
        }

        private void CollectSceneTextEntries(Scene scene, SceneTextCache sceneCache)
        {
            GameObject[] rootObjects = scene.GetRootGameObjects();
            for (int i = 0; i < rootObjects.Length; i++)
            {
                Text[] texts = rootObjects[i].GetComponentsInChildren<Text>(true);
                for (int j = 0; j < texts.Length; j++)
                {
                    sceneCache.textEntries.Add(CreateSceneTextEntry(texts[j], SceneTextKind.Text, sceneCache.scenePath));
                }
            }

            for (int i = 0; i < rootObjects.Length; i++)
            {
                TMP_Text[] tmpTexts = rootObjects[i].GetComponentsInChildren<TMP_Text>(true);
                for (int j = 0; j < tmpTexts.Length; j++)
                {
                    sceneCache.tmpEntries.Add(CreateSceneTextEntry(tmpTexts[j], SceneTextKind.TextMeshPro, sceneCache.scenePath));
                }
            }
        }

        private SceneTextEntry CreateSceneTextEntry(Component component, SceneTextKind kind, string scenePath)
        {
            string textValue = string.Empty;
            Text uiText = component as Text;
            if (uiText != null)
            {
                textValue = uiText.text;
            }
            TMP_Text tmpText = component as TMP_Text;
            if (tmpText != null)
            {
                textValue = tmpText.text;
            }

            SceneTextEntry entry = new SceneTextEntry();
            entry.kind = kind;
            entry.scenePath = scenePath;
            entry.hierarchyPath = GetHierarchyPath(component.transform);
            entry.hierarchyIndexPath = GetHierarchyIndexPath(component.transform);
            entry.componentIndex = GetComponentIndex(component);
            entry.originalText = textValue ?? string.Empty;
            entry.editedText = entry.originalText;
            entry.dirty = false;
            return entry;
        }

        private int GetComponentIndex(Component component)
        {
            Component[] components;
            if (component is Text)
            {
                components = component.gameObject.GetComponents<Text>();
            }
            else
            {
                components = component.gameObject.GetComponents<TMP_Text>();
            }

            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == component)
                {
                    return i;
                }
            }

            return 0;
        }

        private int GetDirtySceneTextEntryCount()
        {
            int count = 0;
            for (int i = 0; i < sceneTextCaches.Count; i++)
            {
                count += GetDirtySceneTextEntryCount(sceneTextCaches[i].textEntries);
                count += GetDirtySceneTextEntryCount(sceneTextCaches[i].tmpEntries);
            }

            return count;
        }

        private int GetDirtySceneTextEntryCount(List<SceneTextEntry> entries)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].dirty)
                {
                    count++;
                }
            }

            return count;
        }

        private void ApplyAllSceneTextChanges(bool saveScenes)
        {
            if (GetDirtySceneTextEntryCount() == 0)
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            List<string> openedScenePaths = GetOpenedScenePaths();
            string activeScenePath = SceneManager.GetActiveScene().path;
            int appliedCount = 0;

            try
            {
                for (int i = 0; i < sceneTextCaches.Count; i++)
                {
                    SceneTextCache sceneCache = sceneTextCaches[i];
                    if (GetDirtySceneTextEntryCount(sceneCache.textEntries) == 0 && GetDirtySceneTextEntryCount(sceneCache.tmpEntries) == 0)
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar("场景文本编辑", "正在应用 " + sceneCache.scenePath, (float)i / sceneTextCaches.Count);
                    Scene scene = EditorSceneManager.OpenScene(sceneCache.scenePath, OpenSceneMode.Single);
                    appliedCount += ApplySceneTextEntriesToLoadedScene(scene, sceneCache.textEntries);
                    appliedCount += ApplySceneTextEntriesToLoadedScene(scene, sceneCache.tmpEntries);

                    if (saveScenes)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                RestoreOpenedScenes(openedScenePaths, activeScenePath);
            }

            Debug.Log("[FontBatchReplacer] 场景文本更改应用完成。应用数量: " + appliedCount);
        }

        private void ApplySingleSceneTextEntry(SceneTextEntry entry, bool saveScene)
        {
            if (entry == null || !entry.dirty)
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            List<string> openedScenePaths = GetOpenedScenePaths();
            string activeScenePath = SceneManager.GetActiveScene().path;

            try
            {
                Scene scene = EditorSceneManager.OpenScene(entry.scenePath, OpenSceneMode.Single);
                if (ApplySceneTextEntryToLoadedScene(scene, entry) && saveScene)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                RestoreOpenedScenes(openedScenePaths, activeScenePath);
            }
        }

        private int ApplySceneTextEntriesToLoadedScene(Scene scene, List<SceneTextEntry> entries)
        {
            int appliedCount = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].dirty && ApplySceneTextEntryToLoadedScene(scene, entries[i]))
                {
                    appliedCount++;
                }
            }

            return appliedCount;
        }

        private bool ApplySceneTextEntryToLoadedScene(Scene scene, SceneTextEntry entry)
        {
            Component component = FindSceneTextComponent(scene, entry);
            if (component == null)
            {
                Debug.LogWarning("[FontBatchReplacer] 找不到文本组件: " + entry.scenePath + " | " + entry.hierarchyPath);
                return false;
            }

            Undo.RecordObject(component, "Edit Scene Text");
            Text uiText = component as Text;
            if (uiText != null)
            {
                uiText.text = entry.editedText;
            }
            else
            {
                TMP_Text tmpText = component as TMP_Text;
                if (tmpText != null)
                {
                    tmpText.text = entry.editedText;
                }
            }

            EditorUtility.SetDirty(component);
            entry.originalText = entry.editedText;
            entry.dirty = false;
            entry.undoTextHistory.Clear();
            entry.redoTextHistory.Clear();
            return true;
        }

        private void SelectSceneTextEntry(SceneTextEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            Scene scene = GetLoadedSceneByPath(entry.scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (!openSceneWhenSelecting)
                {
                    Debug.LogWarning("[FontBatchReplacer] 场景未打开: " + entry.scenePath);
                    return;
                }

                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return;
                }

                scene = EditorSceneManager.OpenScene(entry.scenePath, OpenSceneMode.Single);
            }

            Component component = FindSceneTextComponent(scene, entry);
            if (component == null)
            {
                Debug.LogWarning("[FontBatchReplacer] 找不到文本组件: " + entry.scenePath + " | " + entry.hierarchyPath);
                return;
            }

            Selection.activeGameObject = component.gameObject;
            EditorGUIUtility.PingObject(component.gameObject);
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }
        }

        private Scene GetLoadedSceneByPath(string scenePath)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && scene.path == scenePath)
                {
                    return scene;
                }
            }

            return new Scene();
        }

        private Component FindSceneTextComponent(Scene scene, SceneTextEntry entry)
        {
            GameObject gameObject = FindGameObjectByHierarchyIndexPath(scene, entry.hierarchyIndexPath);
            if (gameObject == null)
            {
                gameObject = FindGameObjectByHierarchyPath(scene, entry.hierarchyPath);
            }

            if (gameObject == null)
            {
                return null;
            }

            if (entry.kind == SceneTextKind.Text)
            {
                Text[] components = gameObject.GetComponents<Text>();
                return entry.componentIndex >= 0 && entry.componentIndex < components.Length ? components[entry.componentIndex] : null;
            }

            TMP_Text[] tmpComponents = gameObject.GetComponents<TMP_Text>();
            return entry.componentIndex >= 0 && entry.componentIndex < tmpComponents.Length ? tmpComponents[entry.componentIndex] : null;
        }

        private GameObject FindGameObjectByHierarchyPath(Scene scene, string hierarchyPath)
        {
            if (string.IsNullOrEmpty(hierarchyPath))
            {
                return null;
            }

            string[] names = hierarchyPath.Split('/');
            if (names.Length == 0)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            Transform current = null;
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == names[0])
                {
                    current = roots[i].transform;
                    break;
                }
            }

            if (current == null)
            {
                return null;
            }

            for (int i = 1; i < names.Length; i++)
            {
                current = FindDirectChild(current, names[i]);
                if (current == null)
                {
                    return null;
                }
            }

            return current.gameObject;
        }

        private GameObject FindGameObjectByHierarchyIndexPath(Scene scene, string hierarchyIndexPath)
        {
            if (string.IsNullOrEmpty(hierarchyIndexPath))
            {
                return null;
            }

            string[] indexValues = hierarchyIndexPath.Split('/');
            if (indexValues.Length == 0)
            {
                return null;
            }

            if (!int.TryParse(indexValues[0], out int rootIndex))
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            if (rootIndex < 0 || rootIndex >= roots.Length)
            {
                return null;
            }

            Transform current = roots[rootIndex].transform;
            for (int i = 1; i < indexValues.Length; i++)
            {
                if (!int.TryParse(indexValues[i], out int siblingIndex))
                {
                    return null;
                }

                if (siblingIndex < 0 || siblingIndex >= current.childCount)
                {
                    return null;
                }

                current = current.GetChild(siblingIndex);
            }

            return current.gameObject;
        }

        private Transform FindDirectChild(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// 功能 1：
        /// 替换当前打开场景中的 UnityEngine.UI.Text 字体。
        /// </summary>
        private void ReplaceSceneTextFont()
        {
            if (targetTextFont == null)
            {
                Debug.LogError("[FontBatchReplacer] 目标 Font 为空，无法替换。");
                return;
            }
 
            Text[] texts = Resources.FindObjectsOfTypeAll<Text>();
 
            int replaceCount = 0;
            string targetFontName = targetTextFont.name;
 
            foreach (Text text in texts)
            {
                if (text == null)
                {
                    continue;
                }
 
                if (!IsSceneObject(text.gameObject))
                {
                    continue;
                }

                Font oldFont = text.font;
 
                if (oldFont == targetTextFont)
                {
                    continue;
                }
 
                Undo.RecordObject(text, "Replace UI Text Font");
 
                text.font = targetTextFont;
                EditorUtility.SetDirty(text);
 
                replaceCount++;
 
                Debug.Log(
                    $"[FontBatchReplacer] 当前场景 Text 替换成功 | " +
                    $"Scene: {text.gameObject.scene.name} | " +
                    $"Object: {GetHierarchyPath(text.transform)} | " +
                    $"Old Font: {(oldFont != null ? oldFont.name : "None")} | " +
                    $"New Font: {targetFontName}",
                    text
                );
            }
 
            if (replaceCount > 0)
            {
                MarkAllOpenScenesDirty();
            }
 
            Debug.Log($"[FontBatchReplacer] 当前打开场景 Text 字体替换完成，共替换 {replaceCount} 个组件。");
        }
 
        /// <summary>
        /// 功能 2：
        /// 替换指定文件夹下 Prefab 中的 UnityEngine.UI.Text 字体。
        /// 没有选择文件夹时默认 Assets。
        /// </summary>
        private void ReplacePrefabTextFont()
        {
            if (targetTextFont == null)
            {
                Debug.LogError("[FontBatchReplacer] 目标 Font 为空，无法替换。");
                return;
            }
 
            string folderPath = GetTargetFolderPath();
            if (!Directory.Exists(folderPath))
            {
                Debug.LogError($"[FontBatchReplacer] 目标文件夹不存在：{folderPath}");
                return;
            }
 
            string[] prefabPaths = GetPrefabPaths(folderPath);
 
            int totalReplaceCount = 0;
            int changedPrefabCount = 0;
 
            foreach (string prefabPath in prefabPaths)
            {
                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
 
                bool prefabChanged = false;
                int prefabReplaceCount = 0;
 
                try
                {
                    Text[] texts = prefabRoot.GetComponentsInChildren<Text>(true);
 
                    foreach (Text text in texts)
                    {
                        if (text == null)
                        {
                            continue;
                        }
 
                        Font oldFont = text.font;
 
                        if (oldFont == targetTextFont)
                        {
                            continue;
                        }
 
                        text.font = targetTextFont;
                        EditorUtility.SetDirty(text);
 
                        prefabChanged = true;
                        prefabReplaceCount++;
                        totalReplaceCount++;
 
                        Debug.Log(
                            $"[FontBatchReplacer] Prefab Text 替换成功 | " +
                            $"Prefab: {prefabPath} | " +
                            $"Object: {GetHierarchyPath(text.transform)} | " +
                            $"Old Font: {(oldFont != null ? oldFont.name : "None")} | " +
                            $"New Font: {targetTextFont.name}",
                            text
                        );
                    }
 
                    if (prefabChanged)
                    {
                        PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                        changedPrefabCount++;
 
                        Debug.Log(
                            $"[FontBatchReplacer] Prefab 保存成功 | " +
                            $"Prefab: {prefabPath} | " +
                            $"本 Prefab 替换 Text 组件数量: {prefabReplaceCount}"
                        );
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
 
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
 
            Debug.Log(
                $"[FontBatchReplacer] 文件夹 Prefab Text 字体替换完成 | " +
                $"扫描路径: {folderPath} | " +
                $"Prefab 数量: {prefabPaths.Length} | " +
                $"发生修改的 Prefab 数量: {changedPrefabCount} | " +
                $"总替换组件数量: {totalReplaceCount}"
            );
        }
 
        /// <summary>
        /// 功能 3：
        /// 替换项目中所有 Scene 里的 UnityEngine.UI.Text 字体。
        /// </summary>
        private void ReplaceAllScenesTextFont()
        {
            if (targetTextFont == null)
            {
                Debug.LogError("[FontBatchReplacer] 目标 Font 为空，无法替换。");
                return;
            }
 
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[FontBatchReplacer] 用户取消保存当前场景，已终止替换所有场景 Text 字体。");
                return;
            }
 
            List<string> openedScenePaths = GetOpenedScenePaths();
            string activeScenePath = SceneManager.GetActiveScene().path;
 
            string[] scenePaths = GetAllScenePaths();
 
            int totalReplaceCount = 0;
            int changedSceneCount = 0;
 
            try
            {
                foreach (string scenePath in scenePaths)
                {
                    Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
 
                    int sceneReplaceCount = ReplaceTextFontInLoadedScene(scene, targetTextFont);
 
                    if (sceneReplaceCount > 0)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
 
                        changedSceneCount++;
                        totalReplaceCount += sceneReplaceCount;
 
                        Debug.Log(
                            $"[FontBatchReplacer] Scene 保存成功 | " +
                            $"Scene: {scenePath} | " +
                            $"本场景替换 Text 组件数量: {sceneReplaceCount}"
                        );
                    }
                    else
                    {
                        Debug.Log(
                            $"[FontBatchReplacer] Scene 无需替换 Text 字体 | " +
                            $"Scene: {scenePath}"
                        );
                    }
                }
            }
            finally
            {
                RestoreOpenedScenes(openedScenePaths, activeScenePath);
            }
 
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
 
            Debug.Log(
                $"[FontBatchReplacer] 所有场景 Text 字体替换完成 | " +
                $"Scene 数量: {scenePaths.Length} | " +
                $"发生修改的 Scene 数量: {changedSceneCount} | " +
                $"总替换组件数量: {totalReplaceCount}"
            );
        }
 
        /// <summary>
        /// 功能 4：
        /// 替换当前打开场景中的 TextMeshProUGUI Font Asset。
        /// </summary>
        private void ReplaceSceneTMPFontAsset()
        {
            if (targetTMPFontAsset == null)
            {
                Debug.LogError("[FontBatchReplacer] 目标 TMP Font Asset 为空，无法替换。");
                return;
            }
 
            TextMeshProUGUI[] tmpTexts = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
 
            int replaceCount = 0;
            string targetFontAssetName = targetTMPFontAsset.name;
 
            foreach (TextMeshProUGUI tmpText in tmpTexts)
            {
                if (tmpText == null)
                {
                    continue;
                }
 
                if (!IsSceneObject(tmpText.gameObject))
                {
                    continue;
                }

                TMP_FontAsset oldFontAsset = tmpText.font;
 
                if (oldFontAsset == targetTMPFontAsset)
                {
                    continue;
                }
 
                Undo.RecordObject(tmpText, "Replace TextMeshProUGUI Font Asset");
 
                tmpText.font = targetTMPFontAsset;
                EditorUtility.SetDirty(tmpText);
 
                replaceCount++;
 
                Debug.Log(
                    $"[FontBatchReplacer] 当前场景 TextMeshProUGUI 替换成功 | " +
                    $"Scene: {tmpText.gameObject.scene.name} | " +
                    $"Object: {GetHierarchyPath(tmpText.transform)} | " +
                    $"Old TMP Font Asset: {(oldFontAsset != null ? oldFontAsset.name : "None")} | " +
                    $"New TMP Font Asset: {targetFontAssetName}",
                    tmpText
                );
            }
 
            if (replaceCount > 0)
            {
                MarkAllOpenScenesDirty();
            }
 
            Debug.Log($"[FontBatchReplacer] 当前打开场景 TextMeshProUGUI Font Asset 替换完成，共替换 {replaceCount} 个组件。");
        }
 
        /// <summary>
        /// 功能 5：
        /// 替换指定文件夹下 Prefab 中的 TextMeshProUGUI Font Asset。
        /// 没有选择文件夹时默认 Assets。
        /// </summary>
        private void ReplacePrefabTMPFontAsset()
        {
            if (targetTMPFontAsset == null)
            {
                Debug.LogError("[FontBatchReplacer] 目标 TMP Font Asset 为空，无法替换。");
                return;
            }
 
            string folderPath = GetTargetFolderPath();
            if (!Directory.Exists(folderPath))
            {
                Debug.LogError($"[FontBatchReplacer] 目标文件夹不存在：{folderPath}");
                return;
            }
 
            string[] prefabPaths = GetPrefabPaths(folderPath);
 
            int totalReplaceCount = 0;
            int changedPrefabCount = 0;
 
            foreach (string prefabPath in prefabPaths)
            {
                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
 
                bool prefabChanged = false;
                int prefabReplaceCount = 0;
 
                try
                {
                    TextMeshProUGUI[] tmpTexts = prefabRoot.GetComponentsInChildren<TextMeshProUGUI>(true);
 
                    foreach (TextMeshProUGUI tmpText in tmpTexts)
                    {
                        if (tmpText == null)
                        {
                            continue;
                        }
 
                        TMP_FontAsset oldFontAsset = tmpText.font;
 
                        if (oldFontAsset == targetTMPFontAsset)
                        {
                            continue;
                        }
 
                        tmpText.font = targetTMPFontAsset;
                        EditorUtility.SetDirty(tmpText);
 
                        prefabChanged = true;
                        prefabReplaceCount++;
                        totalReplaceCount++;
 
                        Debug.Log(
                            $"[FontBatchReplacer] Prefab TextMeshProUGUI 替换成功 | " +
                            $"Prefab: {prefabPath} | " +
                            $"Object: {GetHierarchyPath(tmpText.transform)} | " +
                            $"Old TMP Font Asset: {(oldFontAsset != null ? oldFontAsset.name : "None")} | " +
                            $"New TMP Font Asset: {targetTMPFontAsset.name}",
                            tmpText
                        );
                    }
 
                    if (prefabChanged)
                    {
                        PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                        changedPrefabCount++;
 
                        Debug.Log(
                            $"[FontBatchReplacer] Prefab 保存成功 | " +
                            $"Prefab: {prefabPath} | " +
                            $"本 Prefab 替换 TextMeshProUGUI 组件数量: {prefabReplaceCount}"
                        );
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
 
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
 
            Debug.Log(
                $"[FontBatchReplacer] 文件夹 Prefab TextMeshProUGUI Font Asset 替换完成 | " +
                $"扫描路径: {folderPath} | " +
                $"Prefab 数量: {prefabPaths.Length} | " +
                $"发生修改的 Prefab 数量: {changedPrefabCount} | " +
                $"总替换组件数量: {totalReplaceCount}"
            );
        }
 
        /// <summary>
        /// 功能 6：
        /// 替换项目中所有 Scene 里的 TextMeshProUGUI Font Asset。
        /// </summary>
        private void ReplaceAllScenesTMPFontAsset()
        {
            if (targetTMPFontAsset == null)
            {
                Debug.LogError("[FontBatchReplacer] 目标 TMP Font Asset 为空，无法替换。");
                return;
            }
 
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[FontBatchReplacer] 用户取消保存当前场景，已终止替换所有场景 TMP Font Asset。");
                return;
            }
 
            List<string> openedScenePaths = GetOpenedScenePaths();
            string activeScenePath = SceneManager.GetActiveScene().path;
 
            string[] scenePaths = GetAllScenePaths();
 
            int totalReplaceCount = 0;
            int changedSceneCount = 0;
 
            try
            {
                foreach (string scenePath in scenePaths)
                {
                    Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
 
                    int sceneReplaceCount = ReplaceTMPFontAssetInLoadedScene(scene, targetTMPFontAsset);
 
                    if (sceneReplaceCount > 0)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
 
                        changedSceneCount++;
                        totalReplaceCount += sceneReplaceCount;
 
                        Debug.Log(
                            $"[FontBatchReplacer] Scene 保存成功 | " +
                            $"Scene: {scenePath} | " +
                            $"本场景替换 TextMeshProUGUI 组件数量: {sceneReplaceCount}"
                        );
                    }
                    else
                    {
                        Debug.Log(
                            $"[FontBatchReplacer] Scene 无需替换 TextMeshProUGUI Font Asset | " +
                            $"Scene: {scenePath}"
                        );
                    }
                }
            }
            finally
            {
                RestoreOpenedScenes(openedScenePaths, activeScenePath);
            }
 
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
 
            Debug.Log(
                $"[FontBatchReplacer] 所有场景 TextMeshProUGUI Font Asset 替换完成 | " +
                $"Scene 数量: {scenePaths.Length} | " +
                $"发生修改的 Scene 数量: {changedSceneCount} | " +
                $"总替换组件数量: {totalReplaceCount}"
            );
        }
 
        /// <summary>
        /// 替换一个已经打开的 Scene 中的 Text 字体。
        /// </summary>
        private int ReplaceTextFontInLoadedScene(Scene scene, Font newFont)
        {
            int replaceCount = 0;
 
            GameObject[] rootObjects = scene.GetRootGameObjects();
 
            foreach (GameObject rootObject in rootObjects)
            {
                Text[] texts = rootObject.GetComponentsInChildren<Text>(true);
 
                foreach (Text text in texts)
                {
                    if (text == null)
                    {
                        continue;
                    }

                    Font oldFont = text.font;
 
                    if (oldFont == newFont)
                    {
                        continue;
                    }
 
                    text.font = newFont;
                    EditorUtility.SetDirty(text);
 
                    replaceCount++;
 
                    Debug.Log(
                        $"[FontBatchReplacer] 所有场景 Text 替换成功 | " +
                        $"Scene: {scene.path} | " +
                        $"Object: {GetHierarchyPath(text.transform)} | " +
                        $"Old Font: {(oldFont != null ? oldFont.name : "None")} | " +
                        $"New Font: {newFont.name}",
                        text
                    );
                }
            }
 
            return replaceCount;
        }
 
        /// <summary>
        /// 替换一个已经打开的 Scene 中的 TextMeshProUGUI Font Asset。
        /// </summary>
        private int ReplaceTMPFontAssetInLoadedScene(Scene scene, TMP_FontAsset newTMPFontAsset)
        {
            int replaceCount = 0;
 
            GameObject[] rootObjects = scene.GetRootGameObjects();
 
            foreach (GameObject rootObject in rootObjects)
            {
                TextMeshProUGUI[] tmpTexts = rootObject.GetComponentsInChildren<TextMeshProUGUI>(true);
 
                foreach (TextMeshProUGUI tmpText in tmpTexts)
                {
                    if (tmpText == null)
                    {
                        continue;
                    }

                    TMP_FontAsset oldFontAsset = tmpText.font;
 
                    if (oldFontAsset == newTMPFontAsset)
                    {
                        continue;
                    }
 
                    tmpText.font = newTMPFontAsset;
                    EditorUtility.SetDirty(tmpText);
 
                    replaceCount++;
 
                    Debug.Log(
                        $"[FontBatchReplacer] 所有场景 TextMeshProUGUI 替换成功 | " +
                        $"Scene: {scene.path} | " +
                        $"Object: {GetHierarchyPath(tmpText.transform)} | " +
                        $"Old TMP Font Asset: {(oldFontAsset != null ? oldFontAsset.name : "None")} | " +
                        $"New TMP Font Asset: {newTMPFontAsset.name}",
                        tmpText
                    );
                }
            }
 
            return replaceCount;
        }
 
        /// <summary>
        /// 获取项目中所有 Scene 路径。
        /// </summary>
        private string[] GetAllScenePaths()
        {
            List<string> scenePaths = new List<string>();
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
 
            for (int i = 0; i < buildScenes.Length; i++)
            {
                string path = buildScenes[i].path;
 
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".unity") && File.Exists(path))
                {
                    scenePaths.Add(path);
                }
            }
 
            return scenePaths.ToArray();
        }
 
        /// <summary>
        /// 记录当前已经打开的 Scene 路径。
        /// </summary>
        private List<string> GetOpenedScenePaths()
        {
            List<string> openedScenePaths = new List<string>();
 
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
 
                if (scene.IsValid() && !string.IsNullOrEmpty(scene.path))
                {
                    openedScenePaths.Add(scene.path);
                }
            }
 
            return openedScenePaths;
        }
 
        /// <summary>
        /// 恢复批量处理前打开的 Scene。
        /// </summary>
        private void RestoreOpenedScenes(List<string> openedScenePaths, string activeScenePath)
        {
            if (openedScenePaths == null || openedScenePaths.Count == 0)
            {
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                return;
            }
 
            for (int i = 0; i < openedScenePaths.Count; i++)
            {
                string scenePath = openedScenePaths[i];
 
                if (string.IsNullOrEmpty(scenePath))
                {
                    continue;
                }
 
                OpenSceneMode openMode = i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive;
                Scene openedScene = EditorSceneManager.OpenScene(scenePath, openMode);
 
                if (!string.IsNullOrEmpty(activeScenePath) && scenePath == activeScenePath)
                {
                    SceneManager.SetActiveScene(openedScene);
                }
            }
        }
 
        /// <summary>
        /// 获取目标文件夹路径。
        /// 如果没有选择文件夹，则默认 Assets。
        /// </summary>
        private string GetTargetFolderPath()
        {
            if (targetFolder == null)
            {
                return "Assets";
            }
 
            string path = AssetDatabase.GetAssetPath(targetFolder);
 
            if (string.IsNullOrEmpty(path))
            {
                return "Assets";
            }
 
            if (!Directory.Exists(path))
            {
                return "Assets";
            }
 
            return path;
        }
 
        /// <summary>
        /// 获取指定文件夹下所有 Prefab 路径。
        /// </summary>
        private string[] GetPrefabPaths(string folderPath)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
 
            List<string> prefabPaths = new List<string>();
 
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
 
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab") && !IsPathInDefaultExcludedAssetFolders(path))
                {
                    prefabPaths.Add(path);
                }
            }
 
            return prefabPaths.ToArray();
        }
 
        /// <summary>
        /// 判断对象是否属于当前打开的场景。
        /// 用于排除 Project 里的 Prefab Asset、内置资源等。
        /// </summary>
        private bool IsSceneObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return false;
            }
 
            Scene scene = gameObject.scene;
 
            if (!scene.IsValid())
            {
                return false;
            }
 
            if (!scene.isLoaded)
            {
                return false;
            }
 
            return true;
        }

        /// <summary>
        /// 获取物体在层级窗口中的完整路径。
        /// </summary>
        private string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return "Null";
            }
 
            string path = transform.name;
            Transform current = transform.parent;
 
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
 
            return path;
        }

        private string GetHierarchyIndexPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            Scene scene = transform.gameObject.scene;
            if (!scene.IsValid())
            {
                return string.Empty;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            int rootIndex = -1;
            Transform root = transform.root;
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && roots[i].transform == root)
                {
                    rootIndex = i;
                    break;
                }
            }

            if (rootIndex < 0)
            {
                return string.Empty;
            }

            List<int> siblingIndexes = new List<int>();
            Transform current = transform;
            while (current != null && current.parent != null)
            {
                siblingIndexes.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            siblingIndexes.Reverse();

            string path = rootIndex.ToString();
            for (int i = 0; i < siblingIndexes.Count; i++)
            {
                path += "/" + siblingIndexes[i];
            }

            return path;
        }
 
        private void DrawCodeTextSearchGUI()
        {
            EditorGUILayout.LabelField("代码 .text 查找", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "用于查找玩家自己写的 C# 脚本里访问文本内容的 .text 代码。\n" +
                "默认只扫描 Assets 下的 MonoScript，不会扫描 Packages 或 Library。\n" +
                "匹配规则使用 .text 后面必须是单词边界，因此不会把 .texture、.textColor、.textMesh 等一起查出来。\n" +
                "可以在结果里直接修改这一行代码。点击保存后会写回 .cs 文件，并通过 AssetDatabase.ImportAsset 触发 Unity 重新导入脚本，Unity 通常会自动重新编译。",
                MessageType.Info
            );

            scriptSearchFolder = DrawFolderAssetPicker(
                "代码扫描文件夹",
                scriptSearchFolder,
                "选择代码扫描文件夹",
                "Assets",
                true
            );

            string scriptFolderPath = GetScriptSearchFolderPath();
            EditorGUILayout.LabelField("当前代码扫描路径", scriptFolderPath);

            using (new GUILayout.HorizontalScope())
            {
                excludePackageLikeFolders = EditorGUILayout.ToggleLeft("排除常见插件/包文件夹", excludePackageLikeFolders, GUILayout.Width(180));
                skipCommentAndStringTextMatches = EditorGUILayout.ToggleLeft("跳过注释和字符串中的 .text", skipCommentAndStringTextMatches, GUILayout.Width(210));
                autoSaveCodeLineChanges = EditorGUILayout.ToggleLeft("修改代码行后自动保存", autoSaveCodeLineChanges, GUILayout.Width(170));
                EditorGUILayout.LabelField("结果滚动高度", GUILayout.Width(90));
                codeTextSearchScrollHeight = EditorGUILayout.Slider(codeTextSearchScrollHeight, 140f, 520f);
            }

            DrawCustomExcludedFoldersGUI();

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("查找代码中的 .text", GUILayout.Height(30)))
                {
                    RefreshCodeTextSearchResults();
                }

                using (new EditorGUI.DisabledScope(GetDirtyCodeTextSearchResultCount() == 0))
                {
                    if (GUILayout.Button("保存所有修改", GUILayout.Height(30), GUILayout.Width(120)))
                    {
                        SaveAllDirtyCodeTextSearchResults();
                    }
                }

                using (new EditorGUI.DisabledScope(codeTextSearchResults.Count == 0))
                {
                    if (GUILayout.Button("清空结果", GUILayout.Height(30), GUILayout.Width(100)))
                    {
                        codeTextSearchResults.Clear();
                    }
                }
            }

            EditorGUILayout.LabelField("查找结果数量: " + codeTextSearchResults.Count + "    未保存修改: " + GetDirtyCodeTextSearchResultCount());

            if (codeTextSearchResults.Count == 0)
            {
                EditorGUILayout.HelpBox("还没有查找结果。点击“查找代码中的 .text”后会在这里显示脚本路径、行号和对应代码。", MessageType.Warning);
                return;
            }

            codeTextSearchScroll = EditorGUILayout.BeginScrollView(codeTextSearchScroll, GUILayout.Height(codeTextSearchScrollHeight));
            for (int i = 0; i < codeTextSearchResults.Count; i++)
            {
                DrawCodeTextSearchResult(codeTextSearchResults[i]);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawCodeTextSearchResult(CodeTextSearchResult result)
        {
            if (result == null)
            {
                return;
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string dirtyPrefix = result.dirty ? "* " : string.Empty;
                EditorGUILayout.LabelField(dirtyPrefix + result.scriptPath, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("行号: " + result.lineNumber + "    列号: " + result.columnNumber, EditorStyles.miniLabel);

                EditorGUI.BeginChangeCheck();
                string newLineContent = EditorGUILayout.TextArea(result.editableLineContent ?? string.Empty, GUILayout.MinHeight(36));
                if (EditorGUI.EndChangeCheck())
                {
                    result.editableLineContent = NormalizeSingleEditableCodeLine(newLineContent);
                    result.dirty = result.editableLineContent != result.originalLineContent;

                    if (autoSaveCodeLineChanges && result.dirty)
                    {
                        SaveCodeTextSearchResultLine(result, true);
                    }
                }

                using (new GUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(result.scriptAsset == null))
                    {
                        if (GUILayout.Button("选中脚本", GUILayout.Width(90)))
                        {
                            SelectCodeTextSearchScript(result);
                        }

                        if (GUILayout.Button("打开到行", GUILayout.Width(90)))
                        {
                            OpenCodeTextSearchScriptAtLine(result);
                        }
                    }

                    using (new EditorGUI.DisabledScope(!result.dirty))
                    {
                        if (GUILayout.Button("保存本行", GUILayout.Width(90)))
                        {
                            SaveCodeTextSearchResultLine(result, true);
                        }
                    }

                    if (GUILayout.Button("复制路径", GUILayout.Width(80)))
                    {
                        EditorGUIUtility.systemCopyBuffer = result.scriptPath;
                        Debug.Log("[FontBatchReplacer] 已复制脚本路径: " + result.scriptPath);
                    }

                    if (result.dirty)
                    {
                        EditorGUILayout.LabelField("未保存", EditorStyles.miniLabel, GUILayout.Width(55));
                    }
                }
            }
        }

        private void DrawCustomExcludedFoldersGUI()
        {
            customExcludedFoldersFoldout = EditorGUILayout.Foldout(customExcludedFoldersFoldout, "自定义排除文件夹", true);
            if (!customExcludedFoldersFoldout)
            {
                return;
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox(
                    "这里可以手动添加不想扫描的文件夹。扫描 .text 时，只要脚本位于这些文件夹或其子文件夹内，就会被跳过。\n" +
                    "点击“选择...”会打开系统文件夹选择窗口，可以像正常文件管理器一样展开子文件夹，而不是 Unity 的扁平资源列表。\n" +
                    "建议选择 Assets 下的文件夹，例如 Assets/Plugins、Assets/SDK等。",
                    MessageType.Info
                );

                if (customExcludedFolders.Count == 0)
                {
                    EditorGUILayout.LabelField("当前没有自定义排除文件夹。", EditorStyles.miniLabel);
                }

                for (int i = 0; i < customExcludedFolders.Count; i++)
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        DefaultAsset previousFolder = customExcludedFolders[i];
                        customExcludedFolders[i] = DrawFolderAssetPicker(
                            "排除文件夹 " + (i + 1),
                            customExcludedFolders[i],
                            "选择要排除的文件夹",
                            "Assets",
                            true
                        );
                        if (previousFolder != customExcludedFolders[i])
                        {
                            SaveCustomExcludedFolders();
                        }

                        string excludedPath = GetValidCustomExcludedFolderPath(customExcludedFolders[i]);
                        if (string.IsNullOrEmpty(excludedPath))
                        {
                            EditorGUILayout.HelpBox("请通过“选择...”选择 Assets 下真实存在的文件夹。普通文件或无效路径不会生效。", MessageType.Warning);
                        }
                        else
                        {
                            EditorGUILayout.LabelField("生效路径: " + excludedPath, EditorStyles.miniLabel);
                        }

                        using (new GUILayout.HorizontalScope())
                        {
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("移除这一项", GUILayout.Width(100)))
                            {
                                customExcludedFolders.RemoveAt(i);
                                SaveCustomExcludedFolders();
                                i--;
                            }
                        }
                    }
                }

                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("添加排除文件夹", GUILayout.Height(26)))
                    {
                        DefaultAsset selectedFolder = SelectFolderAssetWithSystemPanel("选择要排除的文件夹", "Assets");
                        if (selectedFolder != null)
                        {
                            customExcludedFolders.Add(selectedFolder);
                            SaveCustomExcludedFolders();
                        }
                    }

                    if (GUILayout.Button("添加空行", GUILayout.Height(26), GUILayout.Width(90)))
                    {
                        customExcludedFolders.Add(null);
                        SaveCustomExcludedFolders();
                    }

                    using (new EditorGUI.DisabledScope(customExcludedFolders.Count == 0))
                    {
                        if (GUILayout.Button("清空排除文件夹", GUILayout.Height(26), GUILayout.Width(130)))
                        {
                            customExcludedFolders.Clear();
                            SaveCustomExcludedFolders();
                        }
                    }
                }
            }
        }

        private void RefreshCodeTextSearchResults()
        {
            codeTextSearchResults.Clear();

            string folderPath = GetScriptSearchFolderPath();
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                Debug.LogError("[FontBatchReplacer] 代码扫描文件夹不存在: " + folderPath);
                return;
            }

            string[] scriptGuids = AssetDatabase.FindAssets("t:MonoScript", new[] { folderPath });
            Regex textRegex = new Regex(@"\.text\b");
            int scannedScriptCount = 0;

            try
            {
                for (int i = 0; i < scriptGuids.Length; i++)
                {
                    string scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuids[i]);
                    if (!IsUserCodeScriptPath(scriptPath))
                    {
                        continue;
                    }

                    EditorUtility.DisplayProgressBar("代码 .text 查找", "正在扫描 " + scriptPath, scriptGuids.Length == 0 ? 1f : (float)i / scriptGuids.Length);
                    scannedScriptCount++;
                    CollectCodeTextMatches(scriptPath, textRegex);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log(
                "[FontBatchReplacer] 代码 .text 查找完成 | " +
                "扫描脚本数量: " + scannedScriptCount + " | " +
                "匹配数量: " + codeTextSearchResults.Count
            );

            Repaint();
        }

        private void CollectCodeTextMatches(string scriptPath, Regex textRegex)
        {
            if (string.IsNullOrEmpty(scriptPath) || textRegex == null || !File.Exists(scriptPath))
            {
                return;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(scriptPath);
            }
            catch (IOException exception)
            {
                Debug.LogWarning("[FontBatchReplacer] 读取脚本失败: " + scriptPath + " | " + exception.Message);
                return;
            }

            MonoScript scriptAsset = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            bool insideBlockComment = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string originalLine = lines[i] ?? string.Empty;
                string lineForMatch = skipCommentAndStringTextMatches
                    ? MaskCommentsAndStrings(originalLine, ref insideBlockComment)
                    : originalLine;

                MatchCollection matches = textRegex.Matches(lineForMatch);
                if (matches.Count == 0)
                {
                    continue;
                }

                for (int j = 0; j < matches.Count; j++)
                {
                    CodeTextSearchResult result = new CodeTextSearchResult();
                    result.scriptPath = scriptPath;
                    result.lineNumber = i + 1;
                    result.columnNumber = matches[j].Index + 1;
                    result.originalLineContent = originalLine;
                    result.editableLineContent = originalLine;
                    result.dirty = false;
                    result.scriptAsset = scriptAsset;
                    codeTextSearchResults.Add(result);

                    Debug.Log(
                        "[FontBatchReplacer] 找到 .text | " +
                        "Script: " + scriptPath + " | " +
                        "Line: " + result.lineNumber + " | " +
                        "Column: " + result.columnNumber + " | " +
                        "Code: " + result.editableLineContent.Trim(),
                        scriptAsset
                    );
                }
            }
        }

        private string MaskCommentsAndStrings(string line, ref bool insideBlockComment)
        {
            if (line == null)
            {
                return string.Empty;
            }

            char[] chars = line.ToCharArray();
            bool insideString = false;
            bool insideChar = false;
            bool verbatimString = false;

            for (int i = 0; i < chars.Length; i++)
            {
                char current = chars[i];
                char next = i + 1 < chars.Length ? chars[i + 1] : '\0';
                char previous = i > 0 ? chars[i - 1] : '\0';

                if (insideBlockComment)
                {
                    chars[i] = ' ';
                    if (current == '*' && next == '/')
                    {
                        chars[i + 1] = ' ';
                        i++;
                        insideBlockComment = false;
                    }
                    continue;
                }

                if (insideString)
                {
                    chars[i] = ' ';
                    if (verbatimString)
                    {
                        if (current == '"' && next == '"')
                        {
                            chars[i + 1] = ' ';
                            i++;
                        }
                        else if (current == '"')
                        {
                            insideString = false;
                            verbatimString = false;
                        }
                    }
                    else if (current == '"' && !IsEscaped(line, i))
                    {
                        insideString = false;
                    }
                    continue;
                }

                if (insideChar)
                {
                    chars[i] = ' ';
                    if (current == '\'' && !IsEscaped(line, i))
                    {
                        insideChar = false;
                    }
                    continue;
                }

                if (current == '/' && next == '/')
                {
                    for (int j = i; j < chars.Length; j++)
                    {
                        chars[j] = ' ';
                    }
                    break;
                }

                if (current == '/' && next == '*')
                {
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    i++;
                    insideBlockComment = true;
                    continue;
                }

                bool isVerbatimStart = current == '"' && (previous == '@' || (previous == '$' && i >= 2 && line[i - 2] == '@') || (previous == '@' && i >= 2 && line[i - 2] == '$'));
                if (current == '"')
                {
                    chars[i] = ' ';
                    insideString = true;
                    verbatimString = isVerbatimStart;
                    continue;
                }

                if (current == '\'')
                {
                    chars[i] = ' ';
                    insideChar = true;
                }
            }

            return new string(chars);
        }

        private bool IsEscaped(string line, int index)
        {
            int slashCount = 0;
            for (int i = index - 1; i >= 0; i--)
            {
                if (line[i] == '\\')
                {
                    slashCount++;
                }
                else
                {
                    break;
                }
            }

            return slashCount % 2 == 1;
        }

        private int GetDirtyCodeTextSearchResultCount()
        {
            int count = 0;
            for (int i = 0; i < codeTextSearchResults.Count; i++)
            {
                if (codeTextSearchResults[i] != null && codeTextSearchResults[i].dirty)
                {
                    count++;
                }
            }

            return count;
        }

        private void SaveAllDirtyCodeTextSearchResults()
        {
            int savedCount = 0;
            for (int i = 0; i < codeTextSearchResults.Count; i++)
            {
                CodeTextSearchResult result = codeTextSearchResults[i];
                if (result != null && result.dirty)
                {
                    if (SaveCodeTextSearchResultLine(result, false))
                    {
                        savedCount++;
                    }
                }
            }

            AssetDatabase.Refresh();
            Debug.Log("[FontBatchReplacer] 代码行修改保存完成，保存数量: " + savedCount + "。Unity 通常会在脚本导入后自动重新编译。");
            Repaint();
        }

        private bool SaveCodeTextSearchResultLine(CodeTextSearchResult result, bool refreshAssetDatabase)
        {
            if (result == null || !result.dirty)
            {
                return false;
            }

            if (string.IsNullOrEmpty(result.scriptPath) || !File.Exists(result.scriptPath))
            {
                Debug.LogWarning("[FontBatchReplacer] 保存失败，脚本不存在: " + result.scriptPath);
                return false;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(result.scriptPath);
            }
            catch (IOException exception)
            {
                Debug.LogWarning("[FontBatchReplacer] 保存失败，读取脚本失败: " + result.scriptPath + " | " + exception.Message);
                return false;
            }

            int lineIndex = result.lineNumber - 1;
            if (lineIndex < 0 || lineIndex >= lines.Length)
            {
                Debug.LogWarning("[FontBatchReplacer] 保存失败，行号已经失效。请重新查找 .text。Script: " + result.scriptPath + " | Line: " + result.lineNumber);
                return false;
            }

            string newLineContent = NormalizeSingleEditableCodeLine(result.editableLineContent);
            lines[lineIndex] = newLineContent;

            try
            {
                File.WriteAllLines(result.scriptPath, lines);
            }
            catch (IOException exception)
            {
                Debug.LogWarning("[FontBatchReplacer] 保存失败，写入脚本失败: " + result.scriptPath + " | " + exception.Message);
                return false;
            }

            UpdateCachedCodeTextSearchResultsForLine(result.scriptPath, result.lineNumber, newLineContent);

            MonoScript scriptAsset = AssetDatabase.LoadAssetAtPath<MonoScript>(result.scriptPath);
            if (scriptAsset != null)
            {
                EditorUtility.SetDirty(scriptAsset);
            }

            AssetDatabase.ImportAsset(result.scriptPath);
            if (refreshAssetDatabase)
            {
                AssetDatabase.Refresh();
            }

            Debug.Log(
                "[FontBatchReplacer] 已保存代码行 | " +
                "Script: " + result.scriptPath + " | " +
                "Line: " + result.lineNumber + " | " +
                "Code: " + newLineContent.Trim(),
                scriptAsset
            );

            Repaint();
            return true;
        }

        private void UpdateCachedCodeTextSearchResultsForLine(string scriptPath, int lineNumber, string newLineContent)
        {
            for (int i = 0; i < codeTextSearchResults.Count; i++)
            {
                CodeTextSearchResult cachedResult = codeTextSearchResults[i];
                if (cachedResult == null)
                {
                    continue;
                }

                if (cachedResult.scriptPath == scriptPath && cachedResult.lineNumber == lineNumber)
                {
                    cachedResult.originalLineContent = newLineContent;
                    cachedResult.editableLineContent = newLineContent;
                    cachedResult.dirty = false;
                }
            }
        }

        private string NormalizeSingleEditableCodeLine(string line)
        {
            if (line == null)
            {
                return string.Empty;
            }

            return line.Replace("\r", " ").Replace("\n", " ");
        }

        private void SelectCodeTextSearchScript(CodeTextSearchResult result)
        {
            if (result == null || result.scriptAsset == null)
            {
                return;
            }

            Selection.activeObject = result.scriptAsset;
            EditorGUIUtility.PingObject(result.scriptAsset);
        }

        private void OpenCodeTextSearchScriptAtLine(CodeTextSearchResult result)
        {
            if (result == null || result.scriptAsset == null)
            {
                return;
            }

            Selection.activeObject = result.scriptAsset;
            EditorGUIUtility.PingObject(result.scriptAsset);
            AssetDatabase.OpenAsset(result.scriptAsset, result.lineNumber);
        }

        private DefaultAsset DrawFolderAssetPicker(string label, DefaultAsset currentFolder, string panelTitle, string fallbackRelativePath, bool allowClear)
        {
            string validPath = GetValidAssetFolderPath(currentFolder);
            string displayPath = string.IsNullOrEmpty(validPath) ? "未选择" : validPath;

            using (new GUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(105));

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(displayPath);
                }

                if (GUILayout.Button("选择...", GUILayout.Width(70)))
                {
                    string initialPath = string.IsNullOrEmpty(validPath) ? fallbackRelativePath : validPath;
                    DefaultAsset selectedFolder = SelectFolderAssetWithSystemPanel(panelTitle, initialPath);
                    if (selectedFolder != null)
                    {
                        currentFolder = selectedFolder;
                    }
                }

                using (new EditorGUI.DisabledScope(currentFolder == null))
                {
                    if (GUILayout.Button("定位", GUILayout.Width(50)))
                    {
                        Selection.activeObject = currentFolder;
                        EditorGUIUtility.PingObject(currentFolder);
                    }
                }

                if (allowClear && GUILayout.Button("清空", GUILayout.Width(50)))
                {
                    currentFolder = null;
                }
            }

            return currentFolder;
        }

        private DefaultAsset SelectFolderAssetWithSystemPanel(string title, string initialRelativePath)
        {
            string initialAbsolutePath = GetInitialFolderAbsolutePath(initialRelativePath);
            string selectedAbsolutePath = EditorUtility.OpenFolderPanel(title, initialAbsolutePath, string.Empty);
            if (string.IsNullOrEmpty(selectedAbsolutePath))
            {
                return null;
            }

            string assetPath = ConvertAbsoluteFolderPathToAssetPath(selectedAbsolutePath);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning("[FontBatchReplacer] 请选择当前 Unity 工程 Assets 目录下的文件夹: " + selectedAbsolutePath);
                return null;
            }

            DefaultAsset folderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetPath);
            if (folderAsset == null)
            {
                AssetDatabase.Refresh();
                folderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetPath);
            }

            if (folderAsset == null)
            {
                Debug.LogWarning("[FontBatchReplacer] 无法加载选择的文件夹，请确认它位于 Assets 下并已被 Unity 导入: " + assetPath);
            }

            return folderAsset;
        }

        private string GetInitialFolderAbsolutePath(string relativeAssetPath)
        {
            string fallbackPath = NormalizeFullPath(Application.dataPath);
            string normalizedRelativePath = NormalizeAssetPath(relativeAssetPath).TrimEnd('/');
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return fallbackPath;
            }

            string lowerRelativePath = normalizedRelativePath.ToLowerInvariant();
            if (lowerRelativePath == "assets")
            {
                return fallbackPath;
            }

            if (!lowerRelativePath.StartsWith("assets/"))
            {
                return fallbackPath;
            }

            string projectRoot = NormalizeFullPath(Directory.GetParent(Application.dataPath).FullName);
            string absolutePath = NormalizeFullPath(Path.Combine(projectRoot, normalizedRelativePath));
            return Directory.Exists(absolutePath) ? absolutePath : fallbackPath;
        }

        private string ConvertAbsoluteFolderPathToAssetPath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
            {
                return string.Empty;
            }

            string selectedPath = NormalizeFullPath(absolutePath);
            string assetsPath = NormalizeFullPath(Application.dataPath);
            string projectRoot = NormalizeFullPath(Directory.GetParent(Application.dataPath).FullName);
            string selectedLower = selectedPath.ToLowerInvariant();
            string assetsLower = assetsPath.ToLowerInvariant();
            string projectLower = projectRoot.ToLowerInvariant();

            if (selectedLower == assetsLower)
            {
                return "Assets";
            }

            if (selectedLower.StartsWith(assetsLower + "/"))
            {
                return "Assets/" + selectedPath.Substring(assetsPath.Length + 1);
            }

            if (selectedLower.StartsWith(projectLower + "/assets/"))
            {
                return selectedPath.Substring(projectRoot.Length + 1);
            }

            return string.Empty;
        }

        private string GetValidAssetFolderPath(DefaultAsset folderAsset)
        {
            if (folderAsset == null)
            {
                return string.Empty;
            }

            string path = AssetDatabase.GetAssetPath(folderAsset);
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            path = NormalizeAssetPath(path).TrimEnd('/');
            string lowerPath = path.ToLowerInvariant();
            if (lowerPath != "assets" && !lowerPath.StartsWith("assets/"))
            {
                return string.Empty;
            }

            if (!Directory.Exists(path))
            {
                return string.Empty;
            }

            return path;
        }

        private string NormalizeFullPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            return NormalizeAssetPath(Path.GetFullPath(path)).TrimEnd('/');
        }

        private string GetScriptSearchFolderPath()
        {
            string path = GetValidAssetFolderPath(scriptSearchFolder);
            return string.IsNullOrEmpty(path) ? "Assets" : path;
        }

        private bool IsUserCodeScriptPath(string scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath))
            {
                return false;
            }

            string normalizedPath = scriptPath.Replace("\\", "/");
            string lowerPath = normalizedPath.ToLowerInvariant();

            if (!lowerPath.StartsWith("assets/"))
            {
                return false;
            }

            if (!lowerPath.EndsWith(".cs"))
            {
                return false;
            }

            if (IsPathInDefaultExcludedAssetFolders(normalizedPath))
            {
                return false;
            }

            if (IsPathInCustomExcludedFolders(normalizedPath))
            {
                return false;
            }

            if (!excludePackageLikeFolders)
            {
                return true;
            }

            string[] ignoredFragments =
            {
                "/plugins/",
                "/plugin/",
                "/packages/",
                "/package/",
                "/textmesh pro/",
                "/tmpro/",
                "/standard assets/",
                "/assetstoretools/",
                "/external/",
                "/externals/",
                "/vendor/",
                "/vendors/",
                "/sdk/",
                "/sdks/",
                "/buildreport/",
                "/editor/",
                "/asset usage finder/",
                "/playerprefseditor/",
                "/vivo-game-sdk/",
                "oppo-game-sdk",
                "qg-game-sdk",
                "spine-new",
            };

            for (int i = 0; i < ignoredFragments.Length; i++)
            {
                if (lowerPath.Contains(ignoredFragments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsPathInDefaultExcludedAssetFolders(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string normalizedPath = NormalizeAssetPath(assetPath).TrimEnd('/').ToLowerInvariant();
            for (int i = 0; i < DefaultExcludedAssetFolderPaths.Length; i++)
            {
                string excludedPath = NormalizeAssetPath(DefaultExcludedAssetFolderPaths[i]).TrimEnd('/').ToLowerInvariant();
                if (normalizedPath == excludedPath || normalizedPath.StartsWith(excludedPath + "/"))
                {
                    return true;
                }
            }

            return false;
        }

        private void LoadCustomExcludedFolders()
        {
            string editorPrefsKey = GetCustomExcludedFoldersEditorPrefsKey();
            if (!EditorPrefs.HasKey(editorPrefsKey))
            {
                return;
            }

            customExcludedFolders.Clear();
            string savedPathsText = EditorPrefs.GetString(editorPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(savedPathsText))
            {
                return;
            }

            string[] savedPaths = savedPathsText.Split('\n');
            for (int i = 0; i < savedPaths.Length; i++)
            {
                string savedPath = NormalizeAssetPath(savedPaths[i]).Trim();
                if (string.IsNullOrEmpty(savedPath))
                {
                    continue;
                }

                DefaultAsset folderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(savedPath);
                if (folderAsset != null && !string.IsNullOrEmpty(GetValidCustomExcludedFolderPath(folderAsset)))
                {
                    customExcludedFolders.Add(folderAsset);
                }
            }
        }

        private void SaveCustomExcludedFolders()
        {
            List<string> validPaths = new List<string>();
            for (int i = 0; i < customExcludedFolders.Count; i++)
            {
                string validPath = GetValidCustomExcludedFolderPath(customExcludedFolders[i]);
                if (string.IsNullOrEmpty(validPath) || validPaths.Contains(validPath))
                {
                    continue;
                }

                validPaths.Add(validPath);
            }

            EditorPrefs.SetString(GetCustomExcludedFoldersEditorPrefsKey(), string.Join("\n", validPaths.ToArray()));
        }

        private string GetCustomExcludedFoldersEditorPrefsKey()
        {
            return CustomExcludedFoldersEditorPrefsKeyPrefix + NormalizeAssetPath(Application.dataPath).ToLowerInvariant();
        }

        private bool IsPathInCustomExcludedFolders(string scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath) || customExcludedFolders.Count == 0)
            {
                return false;
            }

            string normalizedScriptPath = NormalizeAssetPath(scriptPath).ToLowerInvariant();
            for (int i = 0; i < customExcludedFolders.Count; i++)
            {
                string excludedPath = GetValidCustomExcludedFolderPath(customExcludedFolders[i]);
                if (string.IsNullOrEmpty(excludedPath))
                {
                    continue;
                }

                string normalizedExcludedPath = NormalizeAssetPath(excludedPath).TrimEnd('/').ToLowerInvariant();
                if (normalizedScriptPath == normalizedExcludedPath || normalizedScriptPath.StartsWith(normalizedExcludedPath + "/"))
                {
                    return true;
                }
            }

            return false;
        }

        private string GetValidCustomExcludedFolderPath(DefaultAsset folderAsset)
        {
            return GetValidAssetFolderPath(folderAsset);
        }

        private string NormalizeAssetPath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace("\\", "/");
        }

        /// <summary>
        /// 标记所有打开的场景为已修改。
        /// </summary>
        private void MarkAllOpenScenesDirty()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) 
            {
                Scene scene = SceneManager.GetSceneAt(i);
 
                if (scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }
        }
    }

#endif
