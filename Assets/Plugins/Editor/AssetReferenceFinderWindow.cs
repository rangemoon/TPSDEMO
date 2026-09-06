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
// Asset Reference Finder
// ============================================================================
public class AssetReferenceFinderWindow : EditorWindow
{
    private static readonly HashSet<string> ScannableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".unity",
        ".prefab",
        ".asset",
        ".mat",
        ".controller",
        ".overrideController",
        ".anim",
        ".playable",
        ".mask",
        ".spriteatlas",
        ".spriteatlasv2",
        ".shadergraph",
        ".shadersubgraph",
        ".vfx",
        ".terrainlayer",
        ".physicMaterial",
        ".physicsMaterial2D",
        ".inputactions",
        ".uxml",
        ".uss",
        ".asmdef",
        ".asmref",
        ".preset",
        ".lighting",
        ".renderTexture",
        ".guiskin",
        ".fontsettings"
    };

    private static readonly Regex GuidRegex = new Regex(@"\b(?:guid|m_AssetGUID):\s*([0-9a-fA-F]{32})\b", RegexOptions.Compiled);
    private static readonly Regex FileIdRegex = new Regex(@"\bfileID:\s*(-?\d+)\b", RegexOptions.Compiled);
    private static readonly Regex DocumentHeaderRegex = new Regex(@"^--- !u!(\d+) &(-?\d+)\s*$", RegexOptions.Compiled);
    private static readonly Regex GameObjectReferenceRegex = new Regex(@"\bm_GameObject:\s*\{fileID:\s*(-?\d+)\}", RegexOptions.Compiled);
    private const float FolderTreeHeight = 240f;
    private const float ResultsPanelHeight = 360f;
    private const float ResultsPanelGap = 6f;

    private DefaultAsset targetFolder;
    private UnityEngine.Object targetAsset;
    private DefaultAsset searchRootFolder;
    private Vector2 scrollPosition;
    private Vector2 targetFolderTreeScrollPosition;
    private Vector2 searchRootFolderTreeScrollPosition;
    private Vector2 resourceListScrollPosition;
    private Vector2 referenceDetailsScrollPosition;
    private TargetSelectionMode targetSelectionMode;
    private string resultFilter = string.Empty;
    private bool hideUnusedAssets = true;
    private bool separateSubAssets = true;
    private bool includeMetaFiles;
    private bool excludeSelfReferences = true;
    private bool excludeTargetFolderInternalReferences = true;
    private bool hasScanned;
    private int scannedFileCount;
    private int totalReferenceCount;
    private string lastScanMessage = string.Empty;
    private TargetAssetUsage selectedUsage;
    private GUIStyle selectedUsageRowStyle;
    private GUIStyle unselectedUsageRowStyle;
    private GUIStyle usageNameStyle;
    private readonly HashSet<string> expandedFolderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<TargetAssetUsage> usages = new List<TargetAssetUsage>();

    private enum TargetSelectionMode
    {
        Folder,
        SingleAsset
    }

    private class TargetAssetUsage
    {
        public UnityEngine.Object asset;
        public string displayName;
        public string assetPath;
        public string guid;
        public long localFileId;
        public bool matchLocalFileId;
        public readonly List<ReferenceHit> hits = new List<ReferenceHit>();
    }

    private class ReferenceHit
    {
        public string assetPath;
        public int firstLineNumber;
        public int occurrenceCount;
        public string firstLineText;
        public bool hasOwnerLocalFileId;
        public long ownerLocalFileId;
        public bool hasGameObjectLocalFileId;
        public long gameObjectLocalFileId;
    }

    [MenuItem("Tools/资源引用查找")]
    private static void Open()
    {
        AssetReferenceFinderWindow window = GetWindow<AssetReferenceFinderWindow>("资源引用查找");
        window.minSize = new Vector2(880f, 540f);
        window.Show();
    }

    /// <summary>
    /// 从 Project 右键菜单打开资源引用查找窗口，并将当前资源或文件夹设为目标。
    /// </summary>
    [MenuItem("Assets/资源引用查找", false, -1000)]
    private static void OpenFromProjectSelection()
    {
        UnityEngine.Object selected = Selection.activeObject;
        AssetReferenceFinderWindow window = GetWindow<AssetReferenceFinderWindow>("资源引用查找");
        window.minSize = new Vector2(880f, 540f);
        window.SetTargetFromProjectSelection(selected);
        window.Show();
        window.Focus();
    }

    /// <summary>
    /// 判断当前 Project 选中项是否可以作为资源引用查找目标。
    /// </summary>
    /// <returns>当前选中项是项目资源或文件夹时返回 true。</returns>
    [MenuItem("Assets/资源引用查找", true, -1000)]
    private static bool ValidateOpenFromProjectSelection()
    {
        return Selection.activeObject != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(Selection.activeObject));
    }

    /// <summary>
    /// 将 Project 中选中的资源或文件夹直接设置为当前查询目标。
    /// </summary>
    /// <param name="selected">Project 中当前选中的资源或文件夹。</param>
    private void SetTargetFromProjectSelection(UnityEngine.Object selected)
    {
        string selectedPath = AssetDatabase.GetAssetPath(selected);
        if (AssetDatabase.IsValidFolder(selectedPath))
        {
            targetSelectionMode = TargetSelectionMode.Folder;
            targetFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(selectedPath);
            ExpandToFolder(selectedPath);
        }
        else
        {
            targetSelectionMode = TargetSelectionMode.SingleAsset;
            targetAsset = selected;
        }

        usages.Clear();
        selectedUsage = null;
        hasScanned = false;
        scannedFileCount = 0;
        totalReferenceCount = 0;
        lastScanMessage = string.Empty;
        Repaint();
    }

    private void OnEnable()
    {
        if (searchRootFolder == null)
        {
            searchRootFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
        }

        expandedFolderPaths.Add("Assets");
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("资源引用查找", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "选择目标文件夹或单个目标资源后，工具会读取目标资源的 GUID，并在扫描范围内查找哪些 Unity 序列化资源引用了它们。结果支持按资源分组、隐藏未使用资源、定位目标资源和定位引用来源。",
            MessageType.Info);

        DrawFolderPickers();
        DrawScanOptions();
        DrawScanButtons();
        DrawSummary();
        DrawResults();

        EditorGUILayout.EndScrollView();
    }

    private void DrawFolderPickers()
    {
        EditorGUILayout.Space(8f);

        using (new GUILayout.HorizontalScope())
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(0f)))
            {
                DrawTargetPicker();
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(0f)))
            {
                DrawFolderTreePicker("扫描范围", ref searchRootFolder, ref searchRootFolderTreeScrollPosition, false);
            }
        }

        using (new GUILayout.HorizontalScope())
        {
            if (targetSelectionMode == TargetSelectionMode.Folder)
            {
                if (GUILayout.Button("目标使用当前选中文件夹", GUILayout.Height(24f)))
                {
                    DefaultAsset selectedFolder = GetSelectedFolderAsset();
                    if (selectedFolder != null)
                    {
                        targetFolder = selectedFolder;
                        ExpandToFolder(GetFolderPath(targetFolder));
                    }
                    else
                    {
                        ShowNotification(new GUIContent("请先在 Project 中选中一个文件夹"));
                    }
                }
            }
            else
            {
                if (GUILayout.Button("目标使用当前选中资源", GUILayout.Height(24f)))
                {
                    UnityEngine.Object selectedAsset = GetSelectedAssetObject();
                    if (selectedAsset != null)
                    {
                        targetAsset = selectedAsset;
                    }
                    else
                    {
                        ShowNotification(new GUIContent("请先在 Project 中选中一个资源文件"));
                    }
                }
            }

            if (GUILayout.Button("扫描范围设为 Assets", GUILayout.Height(24f)))
            {
                searchRootFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
                ExpandToFolder("Assets");
            }

            if (GUILayout.Button("刷新文件夹树", GUILayout.Height(24f), GUILayout.Width(100f)))
            {
                AssetDatabase.Refresh();
                Repaint();
            }
        }
    }

    private void DrawTargetPicker()
    {
        EditorGUILayout.LabelField("目标", EditorStyles.boldLabel);
        targetSelectionMode = (TargetSelectionMode)GUILayout.Toolbar(
            (int)targetSelectionMode,
            new[] { "文件夹", "单个文件" },
            GUILayout.Height(24f));

        if (targetSelectionMode == TargetSelectionMode.Folder)
        {
            DrawFolderTreePicker("目标资源文件夹", ref targetFolder, ref targetFolderTreeScrollPosition, true);
        }
        else
        {
            DrawSingleAssetPicker();
        }
    }

    private void DrawSingleAssetPicker()
    {
        using (new GUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("目标资源文件", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(targetAsset == null))
            {
                if (GUILayout.Button("定位", GUILayout.Width(52f)))
                {
                    LocateObject(targetAsset);
                }

                if (GUILayout.Button("清空", GUILayout.Width(52f)))
                {
                    targetAsset = null;
                }
            }
        }

        UnityEngine.Object previousAsset = targetAsset;
        targetAsset = EditorGUILayout.ObjectField("资源", targetAsset, typeof(UnityEngine.Object), false);
        if (targetAsset != previousAsset && targetAsset != null)
        {
            string path = AssetDatabase.GetAssetPath(targetAsset);
            if (AssetDatabase.IsValidFolder(path))
            {
                targetAsset = null;
                ShowNotification(new GUIContent("单个文件模式不能选择文件夹"));
            }
        }

        string selectedPath = GetAssetPath(targetAsset);
        EditorGUILayout.LabelField(string.IsNullOrEmpty(selectedPath) ? "未选择" : selectedPath);
        EditorGUILayout.HelpBox("单个文件模式会查找这个资源文件本身；如果开启“按子资源区分”，会分别列出该文件里的 Sprite、AnimationClip 等子资源。", MessageType.None);
    }

    private void DrawFolderTreePicker(string title, ref DefaultAsset selectedFolder, ref Vector2 treeScrollPosition, bool allowClear)
    {
        string selectedPath = GetFolderPath(selectedFolder);

        using (new GUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            if (GUILayout.Button("定位", GUILayout.Width(52f)))
            {
                LocateFolder(selectedFolder);
            }

            using (new EditorGUI.DisabledScope(!allowClear || selectedFolder == null))
            {
                if (GUILayout.Button("清空", GUILayout.Width(52f)))
                {
                    selectedFolder = null;
                }
            }
        }

        EditorGUILayout.LabelField(string.IsNullOrEmpty(selectedPath) ? "未选择" : selectedPath);

        treeScrollPosition = EditorGUILayout.BeginScrollView(treeScrollPosition, GUILayout.Height(FolderTreeHeight));
        DrawFolderTreeNode("Assets", 0, selectedPath, ref selectedFolder);
        EditorGUILayout.EndScrollView();
    }

    private void DrawFolderTreeNode(string folderPath, int indentLevel, string selectedPath, ref DefaultAsset selectedFolder)
    {
        string[] childFolders = AssetDatabase.GetSubFolders(folderPath);
        bool hasChildren = childFolders.Length > 0;
        bool isExpanded = expandedFolderPaths.Contains(folderPath);
        bool isSelected = string.Equals(folderPath, selectedPath, StringComparison.OrdinalIgnoreCase);

        Rect rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        if (isSelected)
        {
            EditorGUI.DrawRect(rowRect, new Color(0.24f, 0.49f, 0.90f, 0.26f));
        }

        Rect foldoutRect = new Rect(rowRect.x + indentLevel * 16f, rowRect.y, 16f, rowRect.height);
        if (hasChildren)
        {
            bool newExpanded = EditorGUI.Foldout(foldoutRect, isExpanded, GUIContent.none, true);
            if (newExpanded != isExpanded)
            {
                SetFolderExpanded(folderPath, newExpanded);
            }
        }

        GUIContent folderLabel = new GUIContent(EditorGUIUtility.IconContent("Folder Icon"));
        folderLabel.text = Path.GetFileName(folderPath);
        if (string.IsNullOrEmpty(folderLabel.text))
        {
            folderLabel.text = folderPath;
        }

        Rect labelRect = new Rect(rowRect.x + indentLevel * 16f + 18f, rowRect.y, rowRect.width - indentLevel * 16f - 72f, rowRect.height);
        if (GUI.Button(labelRect, folderLabel, EditorStyles.label))
        {
            selectedFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
            ExpandToFolder(folderPath);
            GUI.FocusControl(null);
        }

        Rect selectRect = new Rect(rowRect.xMax - 48f, rowRect.y, 48f, rowRect.height);
        if (GUI.Button(selectRect, "选择"))
        {
            selectedFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
            ExpandToFolder(folderPath);
            GUI.FocusControl(null);
        }

        if (!hasChildren || !expandedFolderPaths.Contains(folderPath))
        {
            return;
        }

        Array.Sort(childFolders, StringComparer.OrdinalIgnoreCase);
        foreach (string childFolder in childFolders)
        {
            DrawFolderTreeNode(childFolder, indentLevel + 1, selectedPath, ref selectedFolder);
        }
    }

    private void SetFolderExpanded(string folderPath, bool expanded)
    {
        if (expanded)
        {
            expandedFolderPaths.Add(folderPath);
        }
        else
        {
            expandedFolderPaths.Remove(folderPath);
        }
    }

    private void ExpandToFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
        {
            return;
        }

        string currentPath = folderPath.Replace("\\", "/");
        while (!string.IsNullOrEmpty(currentPath))
        {
            expandedFolderPaths.Add(currentPath);
            int slashIndex = currentPath.LastIndexOf('/');
            if (slashIndex < 0)
            {
                break;
            }

            currentPath = currentPath.Substring(0, slashIndex);
        }
    }

    private void DrawScanOptions()
    {
        EditorGUILayout.Space(8f);
        separateSubAssets = EditorGUILayout.ToggleLeft("按子资源区分 Sprite、AnimationClip 等资源", separateSubAssets);
        includeMetaFiles = EditorGUILayout.ToggleLeft("同时扫描 .meta 文件", includeMetaFiles);
        excludeSelfReferences = EditorGUILayout.ToggleLeft("排除资源文件自身的引用", excludeSelfReferences);

        using (new EditorGUI.DisabledScope(targetSelectionMode != TargetSelectionMode.Folder))
        {
            excludeTargetFolderInternalReferences = EditorGUILayout.ToggleLeft("排除目标文件夹内部引用，只查外部引用", excludeTargetFolderInternalReferences);
        }

        hideUnusedAssets = EditorGUILayout.ToggleLeft("隐藏未找到引用的资源", hideUnusedAssets);

        if (separateSubAssets)
        {
            EditorGUILayout.HelpBox(
                "开启后会尝试用 GUID + local fileID 区分同一个文件里的子资源，例如 Texture 下的多个 Sprite。少数自定义序列化格式可能只写 GUID，此时可关闭该选项做文件级查找。",
                MessageType.None);
        }

        if (targetSelectionMode == TargetSelectionMode.Folder && excludeTargetFolderInternalReferences)
        {
            EditorGUILayout.HelpBox("开启后，扫描时会跳过目标文件夹内的所有资源，只统计目标文件夹外部对它的引用。", MessageType.None);
        }
    }

    private void DrawScanButtons()
    {
        EditorGUILayout.Space(8f);

        if (GUILayout.Button("开始扫描", GUILayout.Height(32f)))
        {
            ScanReferences();
        }
    }

    private void DrawSummary()
    {
        if (!hasScanned)
        {
            return;
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(lastScanMessage, MessageType.Info);

        using (new GUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("结果过滤", GUILayout.Width(60f));
            resultFilter = EditorGUILayout.TextField(resultFilter);
        }
    }

    private void DrawResults()
    {
        if (!hasScanned)
        {
            return;
        }

        EditorGUILayout.Space(8f);

        List<TargetAssetUsage> visibleUsages = GetVisibleUsages();
        if (visibleUsages.Count == 0)
        {
            EditorGUILayout.HelpBox("没有符合当前过滤条件的资源。", MessageType.None);
            return;
        }

        if (selectedUsage == null || !visibleUsages.Contains(selectedUsage))
        {
            selectedUsage = visibleUsages[0];
        }

        DrawResultsSplitView(visibleUsages);
    }

    private void DrawResultsSplitView(List<TargetAssetUsage> visibleUsages)
    {
        float totalWidth = Mathf.Max(1f, EditorGUIUtility.currentViewWidth - 20f);
        float columnWidth = Mathf.Floor((totalWidth - ResultsPanelGap) * 0.5f);

        using (new GUILayout.HorizontalScope(GUILayout.Width(totalWidth), GUILayout.ExpandWidth(false)))
        {
            DrawResourceListPanel(visibleUsages, columnWidth);
            GUILayout.Space(ResultsPanelGap);
            DrawReferenceDetailsPanel(columnWidth);
        }
    }

    private void DrawResourceListPanel(List<TargetAssetUsage> visibleUsages, float columnWidth)
    {
        float scrollWidth = Mathf.Max(1f, columnWidth - 6f);
        float rowWidth = Mathf.Max(1f, columnWidth - 28f);

        using (new GUILayout.VerticalScope(
                   GUILayout.Width(columnWidth),
                   GUILayout.MinWidth(columnWidth),
                   GUILayout.MaxWidth(columnWidth)))
        {
            EditorGUILayout.LabelField("资源列表", EditorStyles.boldLabel, GUILayout.Width(columnWidth));

            using (new GUILayout.VerticalScope(
                       EditorStyles.helpBox,
                       GUILayout.Width(columnWidth),
                       GUILayout.MinWidth(columnWidth),
                       GUILayout.MaxWidth(columnWidth),
                       GUILayout.Height(ResultsPanelHeight)))
            {
                resourceListScrollPosition = EditorGUILayout.BeginScrollView(
                    resourceListScrollPosition,
                    GUILayout.Width(scrollWidth),
                    GUILayout.Height(ResultsPanelHeight - 8f));

                for (int i = 0; i < visibleUsages.Count; i++)
                {
                    DrawUsageRow(visibleUsages[i], i + 1, rowWidth);
                }

                EditorGUILayout.EndScrollView();
            }
        }
    }

    private void DrawReferenceDetailsPanel(float columnWidth)
    {
        float scrollWidth = Mathf.Max(1f, columnWidth - 6f);
        float contentWidth = Mathf.Max(1f, columnWidth - 28f);

        using (new GUILayout.VerticalScope(
                   GUILayout.Width(columnWidth),
                   GUILayout.MinWidth(columnWidth),
                   GUILayout.MaxWidth(columnWidth)))
        {
            EditorGUILayout.LabelField("当前资源引用", EditorStyles.boldLabel, GUILayout.Width(columnWidth));

            using (new GUILayout.VerticalScope(
                       EditorStyles.helpBox,
                       GUILayout.Width(columnWidth),
                       GUILayout.MinWidth(columnWidth),
                       GUILayout.MaxWidth(columnWidth),
                       GUILayout.Height(ResultsPanelHeight)))
            {
                referenceDetailsScrollPosition = EditorGUILayout.BeginScrollView(
                    referenceDetailsScrollPosition,
                    GUILayout.Width(scrollWidth),
                    GUILayout.Height(ResultsPanelHeight - 8f));

                DrawSelectedUsageDetails(contentWidth);

                EditorGUILayout.EndScrollView();
            }
        }
    }

    private List<TargetAssetUsage> GetVisibleUsages()
    {
        List<TargetAssetUsage> visibleUsages = new List<TargetAssetUsage>();
        foreach (TargetAssetUsage usage in usages)
        {
            if (hideUnusedAssets && usage.hits.Count == 0)
            {
                continue;
            }

            if (!PassesResultFilter(usage))
            {
                continue;
            }

            visibleUsages.Add(usage);
        }

        return visibleUsages;
    }

    private void DrawUsageRow(TargetAssetUsage usage, int displayIndex, float rowWidth)
    {
        const float locateButtonWidth = 80f;
        const float gap = 4f;
        const float horizontalPadding = 4f;
        const float verticalPadding = 3f;
        float rowHeight = EditorGUIUtility.singleLineHeight + verticalPadding * 2f;

        Rect rowRect = GUILayoutUtility.GetRect(
            rowWidth,
            rowHeight,
            GUILayout.Width(rowWidth),
            GUILayout.Height(rowHeight),
            GUILayout.ExpandWidth(false));

        GUI.Box(rowRect, GUIContent.none, EditorStyles.helpBox);

        Rect contentRect = new Rect(
            rowRect.x + horizontalPadding,
            rowRect.y + verticalPadding,
            Mathf.Max(0f, rowRect.width - horizontalPadding * 2f),
            EditorGUIUtility.singleLineHeight);

        Rect buttonRect = new Rect(
            contentRect.xMax - locateButtonWidth,
            contentRect.y,
            locateButtonWidth,
            contentRect.height);

        Rect textRect = new Rect(
            contentRect.x,
            contentRect.y,
            Mathf.Max(0f, contentRect.width - locateButtonWidth - gap),
            contentRect.height);

        bool selected = usage == selectedUsage;
        if (GUI.Button(textRect, GUIContent.none, GetUsageRowButtonStyle(selected)))
        {
            SelectUsage(usage);
        }

        DrawUsageRowText(textRect, usage, displayIndex);

        if (GUI.Button(buttonRect, "定位资源"))
        {
            LocateAsset(usage.assetPath);
        }
    }

    private void DrawSelectedUsageDetails(float contentWidth)
    {
        if (selectedUsage == null)
        {
            return;
        }

        using (new GUILayout.VerticalScope(GUILayout.Width(contentWidth), GUILayout.ExpandWidth(false)))
        {
            EditorGUILayout.LabelField(GetDisplayResourceName(selectedUsage), EditorStyles.boldLabel, GUILayout.Width(contentWidth));
            DrawSelectableSingleLine(selectedUsage.assetPath, contentWidth);
            EditorGUILayout.Space(4f);

            if (selectedUsage.hits.Count == 0)
            {
                EditorGUILayout.HelpBox("未找到引用", MessageType.None, true);
            }
            else
            {
                EditorGUILayout.LabelField("引用文件 | 行号 | 次数", EditorStyles.miniBoldLabel, GUILayout.Width(contentWidth));
                foreach (ReferenceHit hit in selectedUsage.hits)
                {
                    DrawReferenceHit(selectedUsage, hit, contentWidth);
                }
            }

            EditorGUILayout.Space(2f);
        }
    }

    private static void DrawSelectableSingleLine(string text, float width)
    {
        Rect rect = GUILayoutUtility.GetRect(
            width,
            EditorGUIUtility.singleLineHeight,
            GUILayout.Width(width),
            GUILayout.Height(EditorGUIUtility.singleLineHeight),
            GUILayout.ExpandWidth(false));

        EditorGUI.SelectableLabel(rect, text ?? string.Empty, EditorStyles.label);
    }

    private void SelectUsage(TargetAssetUsage usage)
    {
        if (selectedUsage == usage)
        {
            return;
        }

        selectedUsage = usage;
        referenceDetailsScrollPosition = Vector2.zero;
    }

    private GUIStyle GetUsageRowButtonStyle(bool selected)
    {
        if (selectedUsageRowStyle == null)
        {
            selectedUsageRowStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = GUI.skin.button.active
            };
        }

        if (unselectedUsageRowStyle == null)
        {
            unselectedUsageRowStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft
            };
        }

        return selected ? selectedUsageRowStyle : unselectedUsageRowStyle;
    }

    private GUIStyle GetUsageNameStyle()
    {
        if (usageNameStyle == null)
        {
            usageNameStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = Color.red },
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
        }

        return usageNameStyle;
    }

    private void DrawUsageRowText(Rect rowRect, TargetAssetUsage usage, int displayIndex)
    {
        const float paddingX = 7f;
        Rect contentRect = new Rect(
            rowRect.x + paddingX,
            rowRect.y + 2f,
            Mathf.Max(0f, rowRect.width - paddingX * 2f),
            EditorGUIUtility.singleLineHeight);

        GUIStyle normalStyle = EditorStyles.label;
        string prefix = "[" + displayIndex + "] ";
        Vector2 prefixSize = normalStyle.CalcSize(new GUIContent(prefix));
        GUI.Label(new Rect(contentRect.x, contentRect.y, prefixSize.x, contentRect.height), prefix, normalStyle);

        string resourceName = GetDisplayResourceName(usage);
        Vector2 nameSize = GetUsageNameStyle().CalcSize(new GUIContent(resourceName));
        float maxNameWidth = Mathf.Max(60f, contentRect.width * 0.35f);
        float nameWidth = Mathf.Min(nameSize.x, maxNameWidth);
        GUI.Label(new Rect(contentRect.x + prefixSize.x, contentRect.y, nameWidth, contentRect.height), resourceName, GetUsageNameStyle());

        string suffix = " | " + usage.assetPath + " | " + usage.hits.Count + "处引用";
        GUI.Label(new Rect(contentRect.x + prefixSize.x + nameWidth, contentRect.y, Mathf.Max(0f, contentRect.width - prefixSize.x - nameWidth), contentRect.height), suffix, normalStyle);
    }

    private void DrawReferenceHit(TargetAssetUsage usage, ReferenceHit hit, float rowWidth)
    {
        const float locateButtonWidth = 58f;
        const float gap = 4f;

        Rect rowRect = GUILayoutUtility.GetRect(
            rowWidth,
            EditorGUIUtility.singleLineHeight,
            GUILayout.Width(rowWidth),
            GUILayout.Height(EditorGUIUtility.singleLineHeight),
            GUILayout.ExpandWidth(false));

        Rect buttonRect = new Rect(
            rowRect.xMax - locateButtonWidth,
            rowRect.y,
            locateButtonWidth,
            rowRect.height);

        Rect labelRect = new Rect(
            rowRect.x,
            rowRect.y,
            Mathf.Max(0f, rowRect.width - locateButtonWidth - gap),
            rowRect.height);

        string hitLabel = BuildReferenceHitLabel(hit);
        GUIStyle clippedStyle = new GUIStyle(EditorStyles.label)
        {
            clipping = TextClipping.Clip
        };

        GUI.Label(labelRect, new GUIContent(hitLabel, hitLabel), clippedStyle);

        if (GUI.Button(buttonRect, "定位"))
        {
            LocateReferenceHit(usage, hit);
        }
    }

    private static string GetDisplayResourceName(TargetAssetUsage usage)
    {
        if (usage == null || usage.asset == null || string.IsNullOrEmpty(usage.asset.name))
        {
            return usage == null ? string.Empty : Path.GetFileNameWithoutExtension(usage.assetPath);
        }

        return usage.asset.name;
    }

    private static string BuildReferenceHitLabel(ReferenceHit hit)
    {
        string lineInfo = hit.firstLineNumber > 0 ? "行 " + hit.firstLineNumber : "行 ?";
        return string.Format(
            "{0} | {1} | {2}次",
            hit.assetPath,
            lineInfo,
            hit.occurrenceCount);
    }

    private void ScanReferences()
    {
        string targetPath = GetTargetPath();
        string searchRootPath = GetFolderPath(searchRootFolder);

        if (string.IsNullOrEmpty(targetPath))
        {
            EditorUtility.DisplayDialog("Asset Reference Finder", "请先选择目标资源文件夹或单个目标资源。", "OK");
            return;
        }

        if (string.IsNullOrEmpty(searchRootPath))
        {
            searchRootPath = "Assets";
        }

        usages.Clear();
        scannedFileCount = 0;
        totalReferenceCount = 0;
        hasScanned = false;

        List<TargetAssetUsage> targetUsages = targetSelectionMode == TargetSelectionMode.Folder
            ? CollectTargetAssets(targetPath)
            : CollectSingleTargetAsset(targetPath);

        if (targetUsages.Count == 0)
        {
            EditorUtility.DisplayDialog("Asset Reference Finder", "目标中没有可扫描的资源。", "OK");
            return;
        }

        string excludedFolderPath = targetSelectionMode == TargetSelectionMode.Folder && excludeTargetFolderInternalReferences
            ? targetPath
            : string.Empty;
        Dictionary<string, List<TargetAssetUsage>> usagesByGuid = BuildGuidMap(targetUsages);
        List<string> candidatePaths = CollectCandidatePaths(searchRootPath, excludedFolderPath);

        try
        {
            for (int i = 0; i < candidatePaths.Count; i++)
            {
                string candidatePath = candidatePaths[i];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "Asset Reference Finder",
                        "扫描中: " + candidatePath,
                        candidatePaths.Count == 0 ? 1f : (float)i / candidatePaths.Count))
                {
                    break;
                }

                ScanCandidateFile(candidatePath, usagesByGuid);
                scannedFileCount++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        targetUsages.Sort(CompareUsage);
        foreach (TargetAssetUsage usage in targetUsages)
        {
            usage.hits.Sort(CompareHit);
            totalReferenceCount += usage.hits.Count;
        }

        usages.AddRange(targetUsages);
        hasScanned = true;
        string externalOnlyMessage = string.IsNullOrEmpty(excludedFolderPath)
            ? string.Empty
            : "已排除目标文件夹内部引用。";
        lastScanMessage = string.Format(
            "扫描完成。目标资源 {0} 个，扫描文件 {1} 个，引用来源 {2} 处。{3}",
            usages.Count,
            scannedFileCount,
            totalReferenceCount,
            externalOnlyMessage);
    }

    private string GetTargetPath()
    {
        return targetSelectionMode == TargetSelectionMode.Folder
            ? GetFolderPath(targetFolder)
            : GetAssetPath(targetAsset);
    }

    private List<TargetAssetUsage> CollectTargetAssets(string folderPath)
    {
        List<TargetAssetUsage> results = new List<TargetAssetUsage>();
        HashSet<string> keys = new HashSet<string>();
        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
            {
                continue;
            }

            if (separateSubAssets && !ShouldTreatAsFileLevelTarget(assetPath))
            {
                UnityEngine.Object[] assetsAtPath = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                foreach (UnityEngine.Object asset in assetsAtPath)
                {
                    AddTargetAsset(results, keys, assetPath, asset, true);
                }
            }
            else
            {
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                AddTargetAsset(results, keys, assetPath, asset, false);
            }
        }

        return results;
    }

    private List<TargetAssetUsage> CollectSingleTargetAsset(string assetPath)
    {
        List<TargetAssetUsage> results = new List<TargetAssetUsage>();
        HashSet<string> keys = new HashSet<string>();

        if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
        {
            return results;
        }

        if (separateSubAssets && !ShouldTreatAsFileLevelTarget(assetPath))
        {
            UnityEngine.Object[] assetsAtPath = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            foreach (UnityEngine.Object asset in assetsAtPath)
            {
                AddTargetAsset(results, keys, assetPath, asset, true);
            }
        }
        else
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            AddTargetAsset(results, keys, assetPath, asset, false);
        }

        return results;
    }

    private static bool ShouldTreatAsFileLevelTarget(string assetPath)
    {
        string extension = Path.GetExtension(assetPath);
        return string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".unity", StringComparison.OrdinalIgnoreCase);
    }

    private void AddTargetAsset(
        List<TargetAssetUsage> results,
        HashSet<string> keys,
        string assetPath,
        UnityEngine.Object asset,
        bool matchLocalFileId)
    {
        if (asset == null)
        {
            return;
        }

        string guid;
        long localFileId;
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out guid, out localFileId))
        {
            return;
        }

        string key = matchLocalFileId ? guid + ":" + localFileId : guid;
        if (!keys.Add(key))
        {
            return;
        }

        TargetAssetUsage usage = new TargetAssetUsage
        {
            asset = asset,
            displayName = BuildDisplayName(asset, assetPath, localFileId, matchLocalFileId),
            assetPath = assetPath,
            guid = guid,
            localFileId = localFileId,
            matchLocalFileId = matchLocalFileId
        };

        results.Add(usage);
    }

    private static Dictionary<string, List<TargetAssetUsage>> BuildGuidMap(List<TargetAssetUsage> targetUsages)
    {
        Dictionary<string, List<TargetAssetUsage>> usagesByGuid = new Dictionary<string, List<TargetAssetUsage>>();
        foreach (TargetAssetUsage usage in targetUsages)
        {
            List<TargetAssetUsage> list;
            if (!usagesByGuid.TryGetValue(usage.guid, out list))
            {
                list = new List<TargetAssetUsage>();
                usagesByGuid.Add(usage.guid, list);
            }

            list.Add(usage);
        }

        return usagesByGuid;
    }

    private List<string> CollectCandidatePaths(string searchRootPath, string excludedFolderPath)
    {
        List<string> results = new List<string>();
        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { searchRootPath });
        bool hasExcludedFolder = !string.IsNullOrEmpty(excludedFolderPath);

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
            {
                continue;
            }

            if (hasExcludedFolder && IsPathInFolder(assetPath, excludedFolderPath))
            {
                continue;
            }

            if (CanScanAssetPath(assetPath))
            {
                results.Add(assetPath);
            }

            string metaPath = assetPath + ".meta";
            if (includeMetaFiles && File.Exists(ToAbsolutePath(metaPath)))
            {
                results.Add(metaPath);
            }
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    private static bool IsPathInFolder(string assetPath, string folderPath)
    {
        if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(folderPath))
        {
            return false;
        }

        string normalizedAssetPath = assetPath.Replace("\\", "/");
        string normalizedFolderPath = folderPath.Replace("\\", "/").TrimEnd('/');
        return string.Equals(normalizedAssetPath, normalizedFolderPath, StringComparison.OrdinalIgnoreCase)
            || normalizedAssetPath.StartsWith(normalizedFolderPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private void ScanCandidateFile(string candidatePath, Dictionary<string, List<TargetAssetUsage>> usagesByGuid)
    {
        string absolutePath = ToAbsolutePath(candidatePath);
        if (!File.Exists(absolutePath))
        {
            return;
        }

        string referencedAssetPath = candidatePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
            ? candidatePath.Substring(0, candidatePath.Length - ".meta".Length)
            : candidatePath;

        int lineNumber = 0;
        long? recentFileId = null;
        int recentFileIdLinesLeft = 0;
        bool hasCurrentDocumentLocalFileId = false;
        long currentDocumentLocalFileId = 0;
        bool hasCurrentGameObjectLocalFileId = false;
        long currentGameObjectLocalFileId = 0;

        using (StreamReader reader = new StreamReader(absolutePath))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;

                Match documentHeaderMatch = DocumentHeaderRegex.Match(line);
                if (documentHeaderMatch.Success)
                {
                    hasCurrentDocumentLocalFileId = long.TryParse(documentHeaderMatch.Groups[2].Value, out currentDocumentLocalFileId);
                    hasCurrentGameObjectLocalFileId = false;
                    currentGameObjectLocalFileId = 0;
                }

                Match gameObjectReferenceMatch = GameObjectReferenceRegex.Match(line);
                if (gameObjectReferenceMatch.Success)
                {
                    hasCurrentGameObjectLocalFileId = long.TryParse(gameObjectReferenceMatch.Groups[1].Value, out currentGameObjectLocalFileId);
                }

                long fileIdInLine;
                bool hasFileIdInLine = TryExtractFileId(line, out fileIdInLine);
                if (hasFileIdInLine)
                {
                    recentFileId = fileIdInLine;
                    recentFileIdLinesLeft = 3;
                }

                MatchCollection guidMatches = GuidRegex.Matches(line);
                foreach (Match match in guidMatches)
                {
                    string guid = match.Groups[1].Value.ToLowerInvariant();
                    List<TargetAssetUsage> matchedUsages;
                    if (!usagesByGuid.TryGetValue(guid, out matchedUsages))
                    {
                        continue;
                    }

                    long? fileIdForReference = hasFileIdInLine ? fileIdInLine : recentFileId;
                    foreach (TargetAssetUsage usage in matchedUsages)
                    {
                        if (excludeSelfReferences && string.Equals(referencedAssetPath, usage.assetPath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (usage.matchLocalFileId && (!fileIdForReference.HasValue || usage.localFileId != fileIdForReference.Value))
                        {
                            continue;
                        }

                        AddHit(
                            usage,
                            candidatePath,
                            lineNumber,
                            line.Trim(),
                            hasCurrentDocumentLocalFileId,
                            currentDocumentLocalFileId,
                            hasCurrentGameObjectLocalFileId,
                            currentGameObjectLocalFileId);
                    }
                }

                if (recentFileIdLinesLeft > 0)
                {
                    recentFileIdLinesLeft--;
                    if (recentFileIdLinesLeft == 0)
                    {
                        recentFileId = null;
                    }
                }
            }
        }
    }

    private static void AddHit(
        TargetAssetUsage usage,
        string candidatePath,
        int lineNumber,
        string lineText,
        bool hasOwnerLocalFileId,
        long ownerLocalFileId,
        bool hasGameObjectLocalFileId,
        long gameObjectLocalFileId)
    {
        ReferenceHit hit = null;
        foreach (ReferenceHit existingHit in usage.hits)
        {
            if (string.Equals(existingHit.assetPath, candidatePath, StringComparison.OrdinalIgnoreCase))
            {
                hit = existingHit;
                break;
            }
        }

        if (hit == null)
        {
            hit = new ReferenceHit
            {
                assetPath = candidatePath,
                firstLineNumber = lineNumber,
                firstLineText = lineText,
                occurrenceCount = 1,
                hasOwnerLocalFileId = hasOwnerLocalFileId,
                ownerLocalFileId = ownerLocalFileId,
                hasGameObjectLocalFileId = hasGameObjectLocalFileId,
                gameObjectLocalFileId = gameObjectLocalFileId
            };
            usage.hits.Add(hit);
        }
        else
        {
            hit.occurrenceCount++;
        }
    }

    private static bool TryExtractFileId(string line, out long fileId)
    {
        Match match = FileIdRegex.Match(line);
        if (match.Success && long.TryParse(match.Groups[1].Value, out fileId))
        {
            return true;
        }

        fileId = 0;
        return false;
    }

    private static bool CanScanAssetPath(string assetPath)
    {
        string extension = Path.GetExtension(assetPath);
        return ScannableExtensions.Contains(extension);
    }

    private static string GetFolderPath(DefaultAsset folderAsset)
    {
        if (folderAsset == null)
        {
            return string.Empty;
        }

        string path = AssetDatabase.GetAssetPath(folderAsset);
        return AssetDatabase.IsValidFolder(path) ? path : string.Empty;
    }

    private static string GetAssetPath(UnityEngine.Object asset)
    {
        if (asset == null)
        {
            return string.Empty;
        }

        string path = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
        {
            return string.Empty;
        }

        return path;
    }

    private static DefaultAsset GetSelectedFolderAsset()
    {
        UnityEngine.Object selected = Selection.activeObject;
        if (selected == null)
        {
            return null;
        }

        string path = AssetDatabase.GetAssetPath(selected);
        if (!AssetDatabase.IsValidFolder(path))
        {
            path = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(path))
            {
                path = path.Replace("\\", "/");
            }
        }

        if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
        {
            return null;
        }

        return AssetDatabase.LoadAssetAtPath<DefaultAsset>(path.Replace("\\", "/"));
    }

    private static UnityEngine.Object GetSelectedAssetObject()
    {
        UnityEngine.Object selected = Selection.activeObject;
        if (selected == null)
        {
            return null;
        }

        string path = AssetDatabase.GetAssetPath(selected);
        if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
        {
            return null;
        }

        return selected;
    }

    private static string BuildDisplayName(UnityEngine.Object asset, string assetPath, long localFileId, bool showLocalFileId)
    {
        string typeName = asset.GetType().Name;
        string name = string.IsNullOrEmpty(asset.name) ? Path.GetFileName(assetPath) : asset.name;
        if (showLocalFileId)
        {
            return string.Format("{0} ({1}, fileID:{2})", name, typeName, localFileId);
        }

        return string.Format("{0} ({1})", name, typeName);
    }

    private bool PassesResultFilter(TargetAssetUsage usage)
    {
        if (string.IsNullOrWhiteSpace(resultFilter))
        {
            return true;
        }

        string filter = resultFilter.Trim();
        if (IndexOf(usage.displayName, filter) >= 0 || IndexOf(usage.assetPath, filter) >= 0)
        {
            return true;
        }

        foreach (ReferenceHit hit in usage.hits)
        {
            if (IndexOf(hit.assetPath, filter) >= 0 || IndexOf(hit.firstLineText, filter) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int IndexOf(string source, string value)
    {
        if (string.IsNullOrEmpty(source))
        {
            return -1;
        }

        return source.IndexOf(value, StringComparison.OrdinalIgnoreCase);
    }

    private static void LocateFolder(DefaultAsset folderAsset)
    {
        LocateObject(folderAsset);
    }

    private static void LocateObject(UnityEngine.Object asset)
    {
        if (asset == null)
        {
            return;
        }

        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    private static void LocateAsset(string assetPath)
    {
        string pathToLoad = assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
            ? assetPath.Substring(0, assetPath.Length - ".meta".Length)
            : assetPath;

        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(pathToLoad);
        if (asset == null)
        {
            EditorUtility.DisplayDialog("Asset Reference Finder", "找不到资源: " + pathToLoad, "OK");
            return;
        }

        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    private static void LocateReferenceHit(TargetAssetUsage usage, ReferenceHit hit)
    {
        string pathToOpen = hit.assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
            ? hit.assetPath.Substring(0, hit.assetPath.Length - ".meta".Length)
            : hit.assetPath;

        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(pathToOpen);
        if (asset == null)
        {
            EditorUtility.DisplayDialog("Asset Reference Finder", "找不到资源: " + pathToOpen, "OK");
            return;
        }

        if (pathToOpen.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Scene scene = EditorSceneManager.OpenScene(pathToOpen);
                if (!SelectReferenceInScene(scene, usage) && !SelectSceneObject(scene, hit))
                {
                    LocateObject(asset);
                }
            }
            return;
        }

        if (pathToOpen.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            OpenPrefabAndSelectObject(pathToOpen, asset, usage, hit);
            return;
        }

        if (hit.firstLineNumber > 0)
        {
            AssetDatabase.OpenAsset(asset, hit.firstLineNumber);
        }
        else
        {
            AssetDatabase.OpenAsset(asset);
        }
    }

    private static void OpenPrefabAndSelectObject(string prefabPath, UnityEngine.Object prefabAsset, TargetAssetUsage usage, ReferenceHit hit)
    {
        AssetDatabase.OpenAsset(prefabAsset);

        EditorApplication.delayCall += () => TrySelectPrefabObject(prefabPath, prefabAsset, usage, hit, 0);
    }

    private static void TrySelectPrefabObject(string prefabPath, UnityEngine.Object prefabAsset, TargetAssetUsage usage, ReferenceHit hit, int attempt)
    {
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null
            && (SelectReferenceInHierarchy(prefabStage.prefabContentsRoot, usage) || SelectObjectInHierarchy(prefabStage.prefabContentsRoot, hit)))
        {
            return;
        }

        if (prefabStage == null && attempt < 5)
        {
            EditorApplication.delayCall += () => TrySelectPrefabObject(prefabPath, prefabAsset, usage, hit, attempt + 1);
            return;
        }

        GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabRoot != null && (SelectReferenceInHierarchy(prefabRoot, usage) || SelectObjectInHierarchy(prefabRoot, hit)))
        {
            return;
        }

        LocateObject(prefabAsset);
    }

    private static bool SelectReferenceInScene(Scene scene, TargetAssetUsage usage)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return false;
        }

        GameObject[] rootGameObjects = scene.GetRootGameObjects();
        foreach (GameObject rootGameObject in rootGameObjects)
        {
            if (SelectReferenceInHierarchy(rootGameObject, usage))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SelectReferenceInHierarchy(GameObject rootGameObject, TargetAssetUsage usage)
    {
        if (rootGameObject == null)
        {
            return false;
        }

        Transform[] transforms = rootGameObject.GetComponentsInChildren<Transform>(true);
        foreach (Transform transform in transforms)
        {
            GameObject gameObject = transform.gameObject;
            if (ObjectHasReference(gameObject, usage))
            {
                SelectLocatedObject(gameObject);
                return true;
            }

            Component[] components = gameObject.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component == null || component is Transform)
                {
                    continue;
                }

                if (ObjectHasReference(component, usage))
                {
                    SelectLocatedObject(component);
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ObjectHasReference(UnityEngine.Object sourceObject, TargetAssetUsage usage)
    {
        if (sourceObject == null)
        {
            return false;
        }

        SerializedObject serializedObject = null;
        try
        {
            serializedObject = new SerializedObject(sourceObject);
            SerializedProperty property = serializedObject.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                if (ReferenceMatchesTarget(property.objectReferenceValue, usage))
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (serializedObject != null)
            {
                serializedObject.Dispose();
            }
        }

        return false;
    }

    private static bool ReferenceMatchesTarget(UnityEngine.Object reference, TargetAssetUsage usage)
    {
        if (reference == null || usage == null)
        {
            return false;
        }

        if (reference == usage.asset)
        {
            return true;
        }

        string referencePath = AssetDatabase.GetAssetPath(reference);
        if (string.IsNullOrEmpty(referencePath) || !string.Equals(referencePath, usage.assetPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!usage.matchLocalFileId)
        {
            return true;
        }

        string referenceGuid;
        long referenceLocalFileId;
        return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reference, out referenceGuid, out referenceLocalFileId)
            && string.Equals(referenceGuid, usage.guid, StringComparison.OrdinalIgnoreCase)
            && referenceLocalFileId == usage.localFileId;
    }

    private static bool SelectSceneObject(Scene scene, ReferenceHit hit)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return false;
        }

        GameObject[] rootGameObjects = scene.GetRootGameObjects();
        foreach (GameObject rootGameObject in rootGameObjects)
        {
            if (SelectObjectInHierarchy(rootGameObject, hit))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SelectObjectInHierarchy(GameObject rootGameObject, ReferenceHit hit)
    {
        if (rootGameObject == null)
        {
            return false;
        }

        if (hit.hasGameObjectLocalFileId)
        {
            GameObject gameObject = FindGameObjectByLocalFileId(rootGameObject, hit.gameObjectLocalFileId);
            if (gameObject != null)
            {
                SelectLocatedObject(gameObject);
                return true;
            }
        }

        if (hit.hasOwnerLocalFileId)
        {
            UnityEngine.Object ownerObject = FindObjectByLocalFileId(rootGameObject, hit.ownerLocalFileId);
            if (ownerObject != null)
            {
                SelectLocatedObject(ownerObject);
                return true;
            }
        }

        return false;
    }

    private static GameObject FindGameObjectByLocalFileId(GameObject rootGameObject, long localFileId)
    {
        Transform[] transforms = rootGameObject.GetComponentsInChildren<Transform>(true);
        foreach (Transform transform in transforms)
        {
            long objectLocalFileId;
            if (TryGetLocalFileIdentifier(transform.gameObject, out objectLocalFileId) && objectLocalFileId == localFileId)
            {
                return transform.gameObject;
            }
        }

        return null;
    }

    private static UnityEngine.Object FindObjectByLocalFileId(GameObject rootGameObject, long localFileId)
    {
        GameObject gameObject = FindGameObjectByLocalFileId(rootGameObject, localFileId);
        if (gameObject != null)
        {
            return gameObject;
        }

        Transform[] transforms = rootGameObject.GetComponentsInChildren<Transform>(true);
        foreach (Transform transform in transforms)
        {
            Component[] components = transform.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component == null)
                {
                    continue;
                }

                long componentLocalFileId;
                if (TryGetLocalFileIdentifier(component, out componentLocalFileId) && componentLocalFileId == localFileId)
                {
                    return component;
                }
            }
        }

        return null;
    }

    private static bool TryGetLocalFileIdentifier(UnityEngine.Object asset, out long localFileId)
    {
        if (asset == null)
        {
            localFileId = 0;
            return false;
        }

        string guid;
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out guid, out localFileId))
        {
            return true;
        }

        ulong persistentLocalFileId = Unsupported.GetLocalIdentifierInFileForPersistentObject(asset);
        if (persistentLocalFileId == 0)
        {
            localFileId = 0;
            return false;
        }

        localFileId = unchecked((long)persistentLocalFileId);
        return true;
    }

    private static void SelectLocatedObject(UnityEngine.Object locatedObject)
    {
        Component component = locatedObject as Component;
        if (component != null)
        {
            Selection.activeGameObject = component.gameObject;
            Selection.activeObject = component;
            EditorGUIUtility.PingObject(component.gameObject);
            return;
        }

        GameObject gameObject = locatedObject as GameObject;
        if (gameObject != null)
        {
            Selection.activeGameObject = gameObject;
            EditorGUIUtility.PingObject(gameObject);
            return;
        }

        LocateObject(locatedObject);
    }

    private static string ToAbsolutePath(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, assetPath);
    }

    private static int CompareUsage(TargetAssetUsage left, TargetAssetUsage right)
    {
        int hitCompare = right.hits.Count.CompareTo(left.hits.Count);
        if (hitCompare != 0)
        {
            return hitCompare;
        }

        return string.Compare(left.assetPath, right.assetPath, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareHit(ReferenceHit left, ReferenceHit right)
    {
        return string.Compare(left.assetPath, right.assetPath, StringComparison.OrdinalIgnoreCase);
    }

}

#endif
