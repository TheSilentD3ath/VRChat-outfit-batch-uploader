// ============================================================
//  VRC Outfit Batch Uploader
//  Window: Tools > Shiro > Outfit Batch Uploader
//
//  Automates uploading multiple VRChat avatar outfits that live
//  as children of a single "Outfits" parent in your scene.
//  For each outfit the tool switches tags (Untagged / EditorOnly),
//  sets the PipelineManager blueprintId, applies per-outfit
//  blendshape overrides, and triggers the VRC SDK build + upload.
//
//  Settings are saved in EditorPrefs and persist across sessions.
// ============================================================

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Core;
using VRC.SDK3A.Editor;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRC.SDKBase.Editor;
using VRC.SDKBase.Editor.Api;   // VRCApi, VRCAvatar

namespace ShiroTools
{
    public class OutfitBatchUploader : EditorWindow
    {
        // ---- Constants ----
        private const string PREFS_PREFIX        = "ShiroOutfitUploader_";
        private const string PREFS_PARENT_NAME   = "ShiroOutfitUploader_OutfitsParentName";
        private const string PREFS_SOUND_ENABLED = "ShiroOutfitUploader_SoundEnabled";
        private const string DEFAULT_PARENT_NAME = "Outfits";
        private const string SOUND_ASSET_PATH    = "Assets/ShiroTools/Editor/Sounds/UI Confirm Sound.mp3";

        private const string SESSION_BATCH_ACTIVE = "Shiro_BatchActive";
        private const string SESSION_BATCH_QUEUE  = "Shiro_BatchQueue";
        private const string SESSION_BATCH_TOTAL  = "Shiro_BatchTotal";
        private const string SESSION_BATCH_INDEX  = "Shiro_BatchIndex";
        private const string SESSION_SKIPPED      = "Shiro_BatchSkipped";
        private const string SESSION_INITIAL_PLATFORM = "Shiro_InitialPlatform";
        private const string SESSION_FINAL_STATUS_MSG = "Shiro_FinalStatusMsg";
        private const string SESSION_FINAL_STATUS_TYPE = "Shiro_FinalStatusType";
        private const string SESSION_PLAY_SOUND_ON_WAKE = "Shiro_PlaySoundOnWake";
        private const string SESSION_BATCH_VERSION = "Shiro_BatchVersion";

        // ---- State ----
        [SerializeField] private GameObject _avatarRoot;
        private List<GameObject>     _avatarsInScene   = new List<GameObject>();
        [SerializeField] private SkinnedMeshRenderer _skinRenderer;
        private GameObject           _outfitsParent;
        private List<OutfitEntry>    _outfits          = new List<OutfitEntry>();
        private string               _outfitsParentName = DEFAULT_PARENT_NAME;
        private Vector2              _scroll;
        private bool               _soundEnabled;
        private bool               _isBatchUploading;
        private int                _batchIndex;
        private int                _batchTotal;
        private float              _batchSubProgress;
        private string             _avatarVersion    = "";
        private string             _statusMessage    = "";
        private MessageType        _statusType       = MessageType.Info;
        private CancellationTokenSource _cts;

        // ---- Styles (lazy init) ----
        private GUIStyle _headerStyle;
        private GUIStyle _activeRowStyle;
        private GUIStyle _inactiveRowStyle;
        private bool     _stylesInited;

        // ============================================================
        [MenuItem("Tools/Shiro/Outfit Batch Uploader")]
        public static void ShowWindow()
        {
            var w = GetWindow<OutfitBatchUploader>("Outfit Uploader");
            w.minSize = new Vector2(440, 340);
        }

        // ============================================================
        private void OnEnable()
        {
            _outfitsParentName = EditorPrefs.GetString(PREFS_PARENT_NAME, DEFAULT_PARENT_NAME);
            _soundEnabled      = EditorPrefs.GetBool(PREFS_SOUND_ENABLED, true);
            ScanScene();
            EditorSceneManager.sceneOpened += OnSceneOpened;

            // Resume batch if we just woke up from a Domain Reload (e.g. after a platform switch)
            if (SessionState.GetBool(SESSION_BATCH_ACTIVE, false))
            {
                _isBatchUploading = true;
                EditorApplication.update += HandleResumeBatch;
            }
            // Check for a finished batch status after a domain reload
            else if (SessionState.GetBool(SESSION_PLAY_SOUND_ON_WAKE, false) || !string.IsNullOrEmpty(SessionState.GetString(SESSION_FINAL_STATUS_MSG, "")))
            {
                EditorApplication.update += HandleFinishedBatch;
            }
        }

        private void OnDisable()
        {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
        }

        private void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            ScanScene();
            Repaint();
        }

        private void HandleResumeBatch()
        {
            // Wait until Unity is fully settled after the Domain Reload
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            
            // Wait for VRC SDK Builder to re-initialize
            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out _)) return;

            // NEW: Wait for user to be logged in before resuming
            if (!APIUser.IsLoggedIn)
            {
                SetStatus("Waiting for VRChat SDK login...", MessageType.Info);
                Repaint();
                return;
            }

            EditorApplication.update -= HandleResumeBatch;
            _cts = new CancellationTokenSource();
            _ = ProcessBatchQueueAsync();
        }

        private void HandleFinishedBatch()
        {
            // Wait until Unity is fully settled after the Domain Reload
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            EditorApplication.update -= HandleFinishedBatch;

            string finalStatus = SessionState.GetString(SESSION_FINAL_STATUS_MSG, "");
            if (!string.IsNullOrEmpty(finalStatus))
            {
                MessageType finalType = (MessageType)SessionState.GetInt(SESSION_FINAL_STATUS_TYPE, (int)MessageType.Info);
                SetStatus(finalStatus, finalType);
                SessionState.EraseString(SESSION_FINAL_STATUS_MSG);
                SessionState.EraseInt(SESSION_FINAL_STATUS_TYPE);
            }

            if (SessionState.GetBool(SESSION_PLAY_SOUND_ON_WAKE, false))
            {
                PlayConfirmSound();
                SessionState.EraseBool(SESSION_PLAY_SOUND_ON_WAKE);
            }
    
            Repaint();
        }


        // ============================================================
        //  Scene scanning
        // ============================================================

        /// <summary>
        /// Full scene scan: refreshes the avatar dropdown list.
        /// If only one avatar exists it is auto-selected.
        /// If the previously selected avatar is still in the scene it stays selected.
        /// </summary>
        private void ScanScene()
        {
            _outfitsParent = null;
            _outfits.Clear();

            _avatarsInScene = FindObjectsOfType<VRCAvatarDescriptor>()
                .Select(d => d.gameObject)
                .ToList();

            // Keep previous selection if still valid, otherwise auto-select if unique
            if (_avatarRoot == null || !_avatarsInScene.Contains(_avatarRoot))
                _avatarRoot = _avatarsInScene.Count == 1 ? _avatarsInScene[0] : null;

            if (_avatarRoot != null)
            {
                AutoDetectSkin();
                RebuildOutfitList();
                LoadAvatarVersion();
            }
        }

        /// <summary>Finds the first SkinnedMeshRenderer that is a direct child of the avatar root
        /// and has blendshapes — typically the body/skin mesh.</summary>
        private void AutoDetectSkin()
        {
            // If already set and saved via SerializeField, don't overwrite
            if (_skinRenderer != null) return; 
            if (_avatarRoot == null) { _skinRenderer = null; return; }

            // Prefer a direct child with blendshapes named "Body" or similar
            foreach (Transform child in _avatarRoot.transform)
            {
                var smr = child.GetComponent<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
                {
                    _skinRenderer = smr;
                    return;
                }
            }
            // Fallback: any descendant with blendshapes
            foreach (var smr in _avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
                {
                    _skinRenderer = smr;
                    return;
                }
            }
            _skinRenderer = null;
        }

        /// <summary>
        /// Rebuilds the outfit list from the currently selected avatar root.
        /// Call this whenever the avatar selection or outfits-parent-name changes.
        /// </summary>
        private void RebuildOutfitList()
        {
            _outfitsParent = null;
            _outfits.Clear();

            if (_avatarRoot == null) return;

            var outfitsTransform = FindDeepChild(_avatarRoot.transform, _outfitsParentName);
            if (outfitsTransform == null) return;
            _outfitsParent = outfitsTransform.gameObject;

            // Scope prefs key by avatar name so two avatars with same outfit names don't clash
            string avatarKey = _avatarRoot.name;
            string projKey = Hash128.Compute(Application.dataPath).ToString();
            
            foreach (Transform child in _outfitsParent.transform)
            {
                string prefKey = PREFS_PREFIX + avatarKey + "_" + child.gameObject.name;
                var entry = new OutfitEntry
                {
                    Go             = child.gameObject,
                    Name           = child.gameObject.name,
                    BlueprintId    = EditorPrefs.GetString(prefKey, ""),
                    IncludeInBatch = EditorPrefs.GetBool(prefKey + "_batch", true),
                    BuildWindows   = EditorPrefs.GetBool(prefKey + "_" + projKey + "_Win", true),
                    BuildAndroid   = EditorPrefs.GetBool(prefKey + "_" + projKey + "_And", false),
                    BuildIOS       = EditorPrefs.GetBool(prefKey + "_" + projKey + "_iOS", false),
                    PrefsKey       = prefKey
                };
                LoadBlendShapes(entry);
                _outfits.Add(entry);
            }
        }

        private static Transform FindDeepChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                var found = FindDeepChild(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void LoadAvatarVersion()
        {
            _avatarVersion = "";
            string mainId = GetMainBlueprintId();
            if (!string.IsNullOrEmpty(mainId))
            {
                _avatarVersion = AvatarVersionManager.GetVersion(mainId);
            }
        }

        private string GetMainBlueprintId()
        {
            if (_avatarRoot == null) return null;
            var pm = _avatarRoot.GetComponent<PipelineManager>();
            if (pm != null && !string.IsNullOrWhiteSpace(pm.blueprintId))
            {
                return pm.blueprintId;
            }
            return null;
        }

        // ============================================================
        //  GUI
        // ============================================================
        private void OnGUI()
        {
            InitStyles();

            // ---- Header ----
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("VRC Outfit Batch Uploader", _headerStyle);
            EditorGUILayout.Space(4);

            DrawTopBar();
            EditorGUILayout.Space(4);
            DrawSeparator();

            if (_outfitsParent == null)
            {
                DrawNoOutfitsMessage();
                return;
            }

            // ---- Outfit list ----
            EditorGUILayout.LabelField(
                $"Outfits ({_outfits.Count})  —  parent: \"{_outfitsParent.name}\"",
                EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _outfits.Count; i++)
                DrawOutfitRow(_outfits[i]);
            EditorGUILayout.EndScrollView();

            DrawSeparator();
            DrawBatchSection();
            EditorGUILayout.Space(4);
        }

        // ---- Top bar ----
        private void DrawTopBar()
        {
            // Row 1: Avatar object field + refresh
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Avatar root:", GUILayout.Width(82));

                EditorGUI.BeginChangeCheck();
                var picked = (GameObject)EditorGUILayout.ObjectField(
                    _avatarRoot, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck())
                {
                    // Validate: must have a VRCAvatarDescriptor
                    if (picked != null && picked.GetComponentInChildren<VRCAvatarDescriptor>() == null)
                    {
                        Debug.LogWarning("[OutfitBatchUploader] Selected object has no VRCAvatarDescriptor.");
                    }
                    else
                    {
                        _avatarRoot = picked;
                        AutoDetectSkin();
                        RebuildOutfitList();
                        LoadAvatarVersion();
                    }
                }

                if (GUILayout.Button("↺", EditorStyles.miniButton, GUILayout.Width(24)))
                    ScanScene();
            }

            // Row 2: Quick-select buttons if multiple avatars are in the scene
            if (_avatarsInScene.Count > 1)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Quick pick:", GUILayout.Width(82));
                    foreach (var av in _avatarsInScene)
                    {
                        bool isCurrent = av == _avatarRoot;
                        using (new EditorGUI.DisabledScope(isCurrent))
                        {
                            if (GUILayout.Button(av.name, EditorStyles.miniButton))
                            {
                                _avatarRoot = av;
                                AutoDetectSkin();
                                RebuildOutfitList();
                                LoadAvatarVersion();
                            }
                        }
                    }
                }
            }

            // Row 3: Skin mesh (SkinnedMeshRenderer with blendshapes)
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Avatar skin:", GUILayout.Width(82));
                EditorGUI.BeginChangeCheck();
                _skinRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                    _skinRenderer, typeof(SkinnedMeshRenderer), true);
                if (EditorGUI.EndChangeCheck() && _skinRenderer != null)
                {
                    // Show blendshape count as confirmation
                    int bsCount = _skinRenderer.sharedMesh != null ? _skinRenderer.sharedMesh.blendShapeCount : 0;
                    SetStatus($"Skin: {_skinRenderer.name}  ({bsCount} blendshapes)", MessageType.Info);
                }
            }

            // Row 4: Outfits parent name
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Outfits parent:", GUILayout.Width(82));
                EditorGUI.BeginChangeCheck();
                _outfitsParentName = EditorGUILayout.TextField(_outfitsParentName);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetString(PREFS_PARENT_NAME, _outfitsParentName);
                    RebuildOutfitList();
                }
            }

            // Row 5: Version
            string mainId = GetMainBlueprintId();
            if (!string.IsNullOrEmpty(mainId))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Base Version:", GUILayout.Width(82));
                    EditorGUI.BeginChangeCheck();
                    _avatarVersion = EditorGUILayout.TextField(_avatarVersion);
                    if (EditorGUI.EndChangeCheck())
                    {
                        AvatarVersionManager.SetVersion(mainId, _avatarVersion);
                    }
                }
            }
        }

        // ---- No outfits message ----
        private void DrawNoOutfitsMessage()
        {
            EditorGUILayout.Space(8);
            if (_avatarRoot == null)
                EditorGUILayout.HelpBox(
                    _avatarsInScene.Count == 0
                        ? "No avatar with a VRCAvatarDescriptor found in the scene.\nOpen your avatar scene and click ↺."
                        : "Select an avatar root in the field above.",
                    MessageType.Warning);
            else
                EditorGUILayout.HelpBox(
                    $"No child named \"{_outfitsParentName}\" found under \"{_avatarRoot.name}\".\n" +
                    "Check the outfits parent name above, or drag the correct parent object directly into the field.",
                    MessageType.Warning);
        }

        // ---- Per-outfit row ----
        private void DrawOutfitRow(OutfitEntry entry)
        {
            if (entry.Go == null) return;
            bool isActive = entry.Go.CompareTag("Untagged");

            var rowStyle = isActive ? _activeRowStyle : _inactiveRowStyle;
            using (new EditorGUILayout.VerticalScope(rowStyle))
            {
                // Row 1: name + status + buttons
                using (new EditorGUILayout.HorizontalScope())
                {
                    string icon = isActive ? "●" : "○";
                    EditorGUILayout.LabelField($"{icon}  {entry.Name}", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));

                    string tagText = entry.Go.tag;
                    var tagColor   = isActive ? Color.green : Color.gray;
                    var oldColor   = GUI.color;
                    GUI.color      = tagColor;
                    EditorGUILayout.LabelField(tagText, GUILayout.Width(80));
                    GUI.color      = oldColor;

                    using (new EditorGUI.DisabledScope(_isBatchUploading))
                    {
                        if (GUILayout.Button("Select", GUILayout.Width(60)))
                            ActivateOutfit(entry);

                        if (GUILayout.Button("Ping", GUILayout.Width(44)))
                        {
                            EditorGUIUtility.PingObject(entry.Go);
                            Selection.activeGameObject = entry.Go;
                        }
                    }
                }

                // Row 2: Blueprint ID + Upload button
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Blueprint ID:", GUILayout.Width(82));
                    EditorGUI.BeginChangeCheck();
                    entry.BlueprintId = EditorGUILayout.TextField(entry.BlueprintId ?? "", GUILayout.ExpandWidth(true));
                    if (EditorGUI.EndChangeCheck())
                        EditorPrefs.SetString(entry.PrefsKey, entry.BlueprintId);

                    using (new EditorGUI.DisabledScope(_isBatchUploading || string.IsNullOrWhiteSpace(entry.BlueprintId)))
                    {
                        if (GUILayout.Button("Upload", GUILayout.Width(56)))
                        {
                            ActivateOutfit(entry);
                            _ = StartBatchAsync(new List<OutfitEntry> { entry });
                        }
                    }
                }

                // Row 3: batch include toggle
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    entry.IncludeInBatch = EditorGUILayout.ToggleLeft("Include in batch upload", entry.IncludeInBatch, GUILayout.Width(160));
                    if (EditorGUI.EndChangeCheck())
                        EditorPrefs.SetBool(entry.PrefsKey + "_batch", entry.IncludeInBatch);
                    
                    GUILayout.FlexibleSpace();
                    
                    EditorGUI.BeginChangeCheck();
                    entry.BuildWindows = EditorGUILayout.ToggleLeft("Win", entry.BuildWindows, GUILayout.Width(45));
                    entry.BuildAndroid = EditorGUILayout.ToggleLeft("And", entry.BuildAndroid, GUILayout.Width(45));
                    entry.BuildIOS     = EditorGUILayout.ToggleLeft("iOS", entry.BuildIOS, GUILayout.Width(40));
                    if (EditorGUI.EndChangeCheck())
                    {
                        string projKey = Hash128.Compute(Application.dataPath).ToString();
                        EditorPrefs.SetBool(entry.PrefsKey + "_" + projKey + "_Win", entry.BuildWindows);
                        EditorPrefs.SetBool(entry.PrefsKey + "_" + projKey + "_And", entry.BuildAndroid);
                        EditorPrefs.SetBool(entry.PrefsKey + "_" + projKey + "_iOS", entry.BuildIOS);
                    }
                }

                // Row 4: blendshape foldout
                DrawBlendShapeFoldout(entry);
            }
            EditorGUILayout.Space(2);
        }

        // ---- Blendshape foldout ----
        private void DrawBlendShapeFoldout(OutfitEntry entry)
        {
            int configuredCount = entry.BlendShapes.Count;
            string foldoutLabel = configuredCount > 0
                ? $"Blendshapes  ({configuredCount} overrides)"
                : "Blendshapes";

            entry.BlendShapeExpanded = EditorGUILayout.Foldout(
                entry.BlendShapeExpanded, foldoutLabel, true, EditorStyles.foldout);

            if (!entry.BlendShapeExpanded) return;

            if (_skinRenderer == null || _skinRenderer.sharedMesh == null)
            {
                EditorGUILayout.HelpBox(
                    "No skin mesh selected. Pick a SkinnedMeshRenderer in the 'Avatar skin' field above.",
                    MessageType.Info);
                return;
            }

            var mesh    = _skinRenderer.sharedMesh;
            int bsCount = mesh.blendShapeCount;

            // Search bar
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Search:", GUILayout.Width(58));
                entry.BlendShapeSearch = EditorGUILayout.TextField(entry.BlendShapeSearch ?? "");
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    entry.BlendShapeSearch = "";
                    GUI.FocusControl(null);
                }
            }

            // "Capture" convenience button
            if (GUILayout.Button("Capture current skin values as overrides", EditorStyles.miniButton))
            {
                for (int i = 0; i < bsCount; i++)
                {
                    float w = _skinRenderer.GetBlendShapeWeight(i);
                    if (w > 0f)
                        entry.BlendShapes[mesh.GetBlendShapeName(i)] = w;
                }
                SaveBlendShapes(entry);
                Repaint();
            }

            EditorGUILayout.Space(2);

            string filter = (entry.BlendShapeSearch ?? "").ToLower();
            bool   dirty  = false;

            for (int i = 0; i < bsCount; i++)
            {
                string bsName = mesh.GetBlendShapeName(i);

                if (!string.IsNullOrEmpty(filter) && !bsName.ToLower().Contains(filter))
                    continue;

                bool  isPinned   = entry.BlendShapes.TryGetValue(bsName, out float storedVal);
                float displayVal = isPinned ? storedVal : _skinRenderer.GetBlendShapeWeight(i);

                // Draw toggle + slider on the same row without IndentLevelScope
                // (IndentLevelScope shifts visuals but not click rects, causing misses)
                Rect rowRect = EditorGUILayout.GetControlRect(false, 18f);

                // Checkbox — 20px on the left
                Rect toggleRect = new Rect(rowRect.x, rowRect.y, 20f, rowRect.height);
                bool nowPinned  = GUI.Toggle(toggleRect, isPinned, GUIContent.none);
                if (nowPinned != isPinned)
                {
                    if (nowPinned) entry.BlendShapes[bsName] = displayVal;
                    else           entry.BlendShapes.Remove(bsName);
                    dirty = true;
                }

                // Slider fills the rest of the row
                Rect sliderRect = new Rect(rowRect.x + 22f, rowRect.y, rowRect.width - 22f, rowRect.height);
                using (new EditorGUI.DisabledScope(!nowPinned))
                {
                    float newVal = GUI.HorizontalSlider(
                        new Rect(sliderRect.x + sliderRect.width - 120f, sliderRect.y + 2f, 100f, sliderRect.height - 4f),
                        displayVal, 0f, 100f);

                    // Label with name + value
                    GUI.Label(new Rect(sliderRect.x, sliderRect.y, sliderRect.width - 124f, sliderRect.height),
                        bsName, EditorStyles.label);
                    GUI.Label(new Rect(sliderRect.x + sliderRect.width - 18f, sliderRect.y, 18f, sliderRect.height),
                        Mathf.RoundToInt(displayVal).ToString(), EditorStyles.miniLabel);

                    if (nowPinned && Math.Abs(newVal - displayVal) > 0.001f)
                    {
                        entry.BlendShapes[bsName] = newVal;
                        dirty = true;
                    }
                }
            }

            if (dirty) { SaveBlendShapes(entry); Repaint(); }

            // Clear all button
            if (configuredCount > 0)
            {
                EditorGUILayout.Space(2);
                if (GUILayout.Button("Clear all overrides", EditorStyles.miniButton))
                {
                    entry.BlendShapes.Clear();
                    SaveBlendShapes(entry);
                    Repaint();
                }
            }
        }

        // ---- Batch section ----
        private void DrawBatchSection()
        {
            int ready = _outfits.Count(o => o.IncludeInBatch && !string.IsNullOrWhiteSpace(o.BlueprintId));

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Batch Upload", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                _soundEnabled = EditorGUILayout.ToggleLeft("🔔 Sound when done", _soundEnabled, GUILayout.Width(140));
                if (EditorGUI.EndChangeCheck())
                    EditorPrefs.SetBool(PREFS_SOUND_ENABLED, _soundEnabled);
            }
            EditorGUILayout.LabelField(
                $"{ready} outfit(s) ready  (have a Blueprint ID + \"Include in batch\" checked)",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (!_isBatchUploading)
                {
                    using (new EditorGUI.DisabledScope(ready == 0))
                    {
                        Color oldColor = GUI.backgroundColor;
                        bool isSuccess = _statusMessage.StartsWith("Queue complete") && _statusType == MessageType.Info;
                        if (isSuccess) GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);

                        if (GUILayout.Button($"Batch Upload All ({ready})", GUILayout.Height(30)))
                        {
                            var batch = _outfits.Where(o => o.IncludeInBatch && !string.IsNullOrWhiteSpace(o.BlueprintId)).ToList();
                            _ = StartBatchAsync(batch);
                        }

                        GUI.backgroundColor = oldColor;
                    }
                }
                else
                {
                    float progress = _batchTotal > 0 ? ((float)_batchIndex + _batchSubProgress) / _batchTotal : 0f;
                    Rect r = EditorGUILayout.GetControlRect(GUILayout.Height(30), GUILayout.ExpandWidth(true));

                    // Determine current platform from queue to tint progress bar
                    VRCPlatform currentPlat = GetCurrentPlatform();
                    string queueStr = SessionState.GetString(SESSION_BATCH_QUEUE, "");
                    if (!string.IsNullOrEmpty(queueStr))
                    {
                        string[] parts = queueStr.Split('\n')[0].Split('|');
                        if (parts.Length >= 3 && Enum.TryParse(parts[2], out VRCPlatform parsedPlat))
                            currentPlat = parsedPlat;
                    }

                    Color oldColor = GUI.color;
                    if (currentPlat == VRCPlatform.Android) GUI.color = new Color(0.65f, 1.0f, 0.65f); // Brighter Green
                    if (currentPlat == VRCPlatform.iOS)     GUI.color = new Color(0.8f, 0.85f, 0.9f);   // Light Silver-Blue
                    if (currentPlat == VRCPlatform.Windows) GUI.color = new Color(0.65f, 0.85f, 1.0f); // Brighter Blue

                    EditorGUI.ProgressBar(r, progress, $"Uploading {_batchIndex + 1} / {_batchTotal} ({currentPlat})…");
                    GUI.color      = oldColor;

                    if (GUILayout.Button("Cancel", GUILayout.Width(66), GUILayout.Height(30)))
                    {
                        _cts?.Cancel();
                        CancelBatch();
                    }
                }
            }

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
            }
        }

        // ============================================================
        //  Core logic
        // ============================================================

        /// <summary>Sets the chosen outfit to Untagged and all others to EditorOnly.
        /// Also switches the PipelineManager blueprintId if one is configured.</summary>
        public void ActivateOutfit(OutfitEntry target)
        {
            if (_outfitsParent == null) return;

            Undo.SetCurrentGroupName($"Activate Outfit: {target.Name}");
            int group = Undo.GetCurrentGroup();

            foreach (var entry in _outfits)
            {
                if (entry.Go == null) continue;

                bool   wantActive = (entry == target);
                string wantTag    = wantActive ? "Untagged" : "EditorOnly";

                bool tagNeedsChange    = entry.Go.tag       != wantTag;
                bool activeNeedsChange = entry.Go.activeSelf != wantActive;

                // Only touch (and record undo for) objects that actually need changing
                if (!tagNeedsChange && !activeNeedsChange) continue;

                Undo.RecordObject(entry.Go, "Set outfit active/tag");

                if (tagNeedsChange)    entry.Go.tag = wantTag;
                if (activeNeedsChange) entry.Go.SetActive(wantActive);

                EditorUtility.SetDirty(entry.Go);
            }

            // Switch PipelineManager blueprintId
            if (!string.IsNullOrWhiteSpace(target.BlueprintId) && _avatarRoot != null)
            {
                var pm = _avatarRoot.GetComponentInChildren<PipelineManager>();
                if (pm != null && pm.blueprintId != target.BlueprintId)
                {
                    Undo.RecordObject(pm, "Set Blueprint ID");
                    pm.blueprintId = target.BlueprintId;
                    EditorUtility.SetDirty(pm);
                }
            }

            // Apply blendshape overrides for this outfit
            if (_skinRenderer != null && target.BlendShapes.Count > 0)
            {
                Undo.RecordObject(_skinRenderer, "Set blendshapes for outfit");
                var mesh = _skinRenderer.sharedMesh;
                foreach (var kv in target.BlendShapes)
                {
                    int idx = mesh.GetBlendShapeIndex(kv.Key);
                    if (idx >= 0)
                        _skinRenderer.SetBlendShapeWeight(idx, kv.Value);
                }
                EditorUtility.SetDirty(_skinRenderer);
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            SetStatus($"✓ Activated: {target.Name}", MessageType.Info);
            Repaint();
        }

        // ---- Blendshape persistence ----

        private static void SaveBlendShapes(OutfitEntry entry)
        {
            // Store the configured blendshape names as a semicolon list, values individually
            var keys = string.Join(";", entry.BlendShapes.Keys);
            EditorPrefs.SetString(entry.PrefsKey + "_BS_keys", keys);
            foreach (var kv in entry.BlendShapes)
                EditorPrefs.SetFloat(entry.PrefsKey + "_BS_" + kv.Key, kv.Value);
        }

        private static void LoadBlendShapes(OutfitEntry entry)
        {
            entry.BlendShapes.Clear();
            string keys = EditorPrefs.GetString(entry.PrefsKey + "_BS_keys", "");
            if (string.IsNullOrEmpty(keys)) return;
            foreach (var k in keys.Split(';'))
            {
                if (string.IsNullOrEmpty(k)) continue;
                entry.BlendShapes[k] = EditorPrefs.GetFloat(entry.PrefsKey + "_BS_" + k, 0f);
            }
        }

        // ---- Confirm sound ----
        private void PlayConfirmSound()
        {
            if (!_soundEnabled) return;

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SOUND_ASSET_PATH);
            if (clip == null)
            {
                Debug.LogWarning("[OutfitBatchUploader] Could not load confirm sound at: " + SOUND_ASSET_PATH);
                return;
            }

            // Unity 2022 internal audio preview — reached via reflection since AudioUtil is not public
            try
            {
                var audioUtil  = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
                var playMethod = audioUtil?.GetMethod(
                    "PlayPreviewClip",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { typeof(AudioClip), typeof(int), typeof(bool) },
                    null);

                if (playMethod != null)
                    playMethod.Invoke(null, new object[] { clip, 0, false });
                else
                    Debug.LogWarning("[OutfitBatchUploader] PlayPreviewClip not found — Unity may have renamed it.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[OutfitBatchUploader] Could not play confirm sound: " + ex.Message);
            }
        }

        // ---- Copyright pre-consent ----

        /// <summary>
        /// Shows ONE confirmation dialog to Shiro, then calls VRCCopyrightAgreement.Agree()
        /// (via reflection, since it's internal) for each blueprint ID.
        /// After this the SDK's own consent check finds everything already agreed and stays silent.
        /// </summary>
        private static async Task<bool> PreConsentAllAsync(IEnumerable<string> blueprintIds)
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Ownership confirmation",
                "Do you confirm that all content you are about to upload belongs to you and that " +
                "you have the necessary rights to upload it?\n\n" +
                "This covers every outfit in the current batch.",
                "Yes, it's all mine",
                "Cancel");

            if (!confirmed) return false;

            // VRCCopyrightAgreement.Agree() is internal — reached via reflection
            var agreeMethod = typeof(VRCCopyrightAgreement).GetMethod(
                "Agree",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (agreeMethod == null)
            {
                Debug.LogWarning("[OutfitBatchUploader] Could not find VRCCopyrightAgreement.Agree via reflection. " +
                                 "The SDK consent dialog will appear normally instead.");
                return true;   // still proceed — SDK dialog will handle it
            }

            foreach (var id in blueprintIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                try
                {
                    var task = (Task<bool>)agreeMethod.Invoke(null, new object[] { id });
                    bool ok = await task;
                    if (!ok)
                        Debug.LogWarning($"[OutfitBatchUploader] Pre-consent API call returned false for {id}. " +
                                         "SDK may still show its own dialog for this outfit.");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[OutfitBatchUploader] Pre-consent failed for {id}: {ex.Message}");
                }
            }

            return true;
        }

        // ---- Flush scene so the SDK builder sees the tag change ----
        private static void FlushScene()
        {
            // Mark all dirty objects, save assets and scene, then let the editor process events
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            // Pump the editor loop so Unity registers the tag changes before the build starts
            EditorApplication.Step();
        }

        // ---- Platform switching helpers ----
        private VRCPlatform GetCurrentPlatform()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            if (target == BuildTarget.Android) return VRCPlatform.Android;
            if (target == BuildTarget.iOS) return VRCPlatform.iOS;
            return VRCPlatform.Windows;
        }

        private bool SwitchPlatform(VRCPlatform plat)
        {
            BuildTargetGroup group = BuildTargetGroup.Standalone;
            BuildTarget target = BuildTarget.StandaloneWindows64;
            
            switch (plat)
            {
                case VRCPlatform.Android:
                    group = BuildTargetGroup.Android;
                    target = BuildTarget.Android;
                    break;
                case VRCPlatform.iOS:
                    group = BuildTargetGroup.iOS;
                    target = BuildTarget.iOS;
                    break;
                case VRCPlatform.Windows:
                    group = BuildTargetGroup.Standalone;
                    target = BuildTarget.StandaloneWindows64;
                    break;
            }
            
            if (!BuildPipeline.IsBuildTargetSupported(group, target))
            {
                Debug.LogError($"[OutfitBatchUploader] Build target {target} is not supported or not installed.");
                return false;
            }

            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
            }
            return true;
        }

        // ---- Cross-Domain Batch Queue System ----
        private async Task StartBatchAsync(List<OutfitEntry> targetOutfits)
        {
            if (targetOutfits.Count == 0) return;

            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out var builder))
            {
                SetStatus("VRC SDK builder not available — open the VRChat SDK window first.", MessageType.Error);
                return;
            }

            // NEW: Check for login before starting
            if (!APIUser.IsLoggedIn)
            {
                SetStatus("Not logged in. Please open the VRChat SDK Control Panel and log in first.", MessageType.Error);
                return;
            }

            var ids = targetOutfits.Select(o => o.BlueprintId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct();
            bool consented = await PreConsentAllAsync(ids);
            if (!consented)
            {
                SetStatus("Upload cancelled — ownership not confirmed.", MessageType.Warning);
                return;
            }

            // Build a flat queue of operations grouped by platform
            var queue = new List<string>();
            var platformOrder = new List<VRCPlatform> { VRCPlatform.Windows, VRCPlatform.Android, VRCPlatform.iOS };
            
            VRCPlatform currentPlatform = GetCurrentPlatform();
            if (platformOrder.Contains(currentPlatform))
            {
                platformOrder.Remove(currentPlatform);
                platformOrder.Insert(0, currentPlatform); // Start with current platform to minimize switching
            }

            foreach (var plat in platformOrder)
            {
                foreach (var outfit in targetOutfits)
                {
                    bool buildsForPlat = 
                        (plat == VRCPlatform.Windows && outfit.BuildWindows) ||
                        (plat == VRCPlatform.Android && outfit.BuildAndroid) ||
                        (plat == VRCPlatform.iOS && outfit.BuildIOS);

                    // Fallback: if no platforms selected for this outfit, build on the current active platform
                    bool hasAny = outfit.BuildWindows || outfit.BuildAndroid || outfit.BuildIOS;
                    if (!hasAny && plat == currentPlatform) buildsForPlat = true;

                    if (buildsForPlat)
                    {
                        queue.Add($"{outfit.Name}|{outfit.BlueprintId}|{plat}");
                    }
                }
            }

            if (queue.Count == 0)
            {
                SetStatus("No platforms configured for the target outfits.", MessageType.Warning);
                return;
            }

            SaveBlendshapeSnapshot();

            // Save queue into Domain-Reload-proof SessionState
            SessionState.SetString(SESSION_BATCH_QUEUE, string.Join("\n", queue));
            SessionState.SetInt(SESSION_BATCH_TOTAL, queue.Count);
            SessionState.SetInt(SESSION_BATCH_INDEX, 0);
            SessionState.SetBool(SESSION_BATCH_ACTIVE, true);
            SessionState.SetString(SESSION_SKIPPED, "");
            SessionState.SetString(SESSION_INITIAL_PLATFORM, currentPlatform.ToString());
            SessionState.SetString(SESSION_BATCH_VERSION, _avatarVersion); // Capture the version from UI

            _isBatchUploading = true;
            _cts = new CancellationTokenSource();
            
            _ = ProcessBatchQueueAsync();
        }

        private async Task ProcessBatchQueueAsync()
        {
            if (!SessionState.GetBool(SESSION_BATCH_ACTIVE, false)) return;
            _isBatchUploading = true;
            Repaint();

            try
            {
                while (true)
                {
                    if (_cts != null && _cts.IsCancellationRequested)
                    {
                        CancelBatch();
                        return;
                    }

                    string queueStr = SessionState.GetString(SESSION_BATCH_QUEUE, "");
                    if (string.IsNullOrWhiteSpace(queueStr))
                    {
                        FinishBatch();
                        return;
                    }

                    var queue = queueStr.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    int total = SessionState.GetInt(SESSION_BATCH_TOTAL, 0);
                    int currentIndex = SessionState.GetInt(SESSION_BATCH_INDEX, 0);

                    _batchIndex = currentIndex;
                    _batchTotal = total;
                    _batchSubProgress = 0.0f;

                    string[] parts = queue[0].Split('|');
                    string outfitName = parts[0];
                    string blueprintId = parts[1];
                    VRCPlatform platform = (VRCPlatform)Enum.Parse(typeof(VRCPlatform), parts[2]);

                    if (platform != GetCurrentPlatform())
                    {
                        SetStatus($"Switching to {platform} for {outfitName}...", MessageType.Info);
                        _batchSubProgress = 0.1f;
                        Repaint();
                        
                        if (!SwitchPlatform(platform))
                            throw new Exception($"Platform {platform} is not installed or supported.");
                        
                        // IMPORTANT: The Platform Switch forces a Unity Domain Reload here. 
                        // All code execution is about to die. We wire up a backup hook to resume just in case 
                        // the switch finishes instantaneously without a reload, then intentionally exit.
                        EditorApplication.update += HandleResumeBatch;
                        return; 
                    }

                    SetStatus($"[{currentIndex + 1}/{total}] Activating {outfitName} ({platform})...", MessageType.Info);
                    _batchSubProgress = 0.2f;
                    Repaint();

                    var outfit = _outfits.FirstOrDefault(o => o.Name == outfitName);
                    if (outfit == null)
                    {
                        // If outfit was deleted from scene mid-batch, skip and continue
                        queue.RemoveAt(0);
                        SessionState.SetString(SESSION_BATCH_QUEUE, string.Join("\n", queue));
                        SessionState.SetInt(SESSION_BATCH_INDEX, currentIndex + 1);
                        continue;
                    }

                    ActivateOutfit(outfit);
                    FlushScene();
                    _batchSubProgress = 0.3f;
                    Repaint();
                    await Task.Delay(1500, _cts.Token);

                    // Double-check platform before upload safeguard
                    if (GetCurrentPlatform() != platform)
                    {
                        throw new Exception($"Critical Safety Check Failed: Queue expected {platform}, but Unity is currently on {GetCurrentPlatform()}.");
                    }

                    // --- Build & Upload Phase ---
                    SetStatus($"[{currentIndex + 1}/{total}] Building & Uploading {outfitName} ({platform})...", MessageType.Info);
                    _batchSubProgress = 0.4f;
                    Repaint();
                    
                    if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out var builder))
                        throw new Exception("SDK Builder not available.");
                    
                    var avatar = await VRCApi.GetAvatar(blueprintId, cancellationToken: _cts.Token);

                    // Set avatar description to version number if it's different
                    string versionToSet = SessionState.GetString(SESSION_BATCH_VERSION, ""); // Use the version captured at batch start

                    // Only attempt to change the description if a version was actually typed in (not blank)
                    if (!string.IsNullOrWhiteSpace(versionToSet))
                    {
                        if (avatar.Description != versionToSet)
                        {
                            avatar.Description = versionToSet;
                            Debug.Log($"[OutfitBatchUploader] Updating '{outfitName}' description to version: {versionToSet}");
                        }
                        
                        // Save it for this specific outfit's blueprint ID so it's remembered across projects!
                        AvatarVersionManager.SetVersion(blueprintId, versionToSet);
                    }

                    await builder.BuildAndUpload(_avatarRoot, avatar, cancellationToken: _cts.Token);

                    // Successful upload! Pop from queue
                    _batchSubProgress = 1.0f;
                    Repaint();
                    queue.RemoveAt(0);
                    SessionState.SetString(SESSION_BATCH_QUEUE, string.Join("\n", queue));
                    SessionState.SetInt(SESSION_BATCH_INDEX, currentIndex + 1);

                    if (queue.Count > 0)
                        await Task.Delay(2000, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                CancelBatch();
            }
            catch (Exception ex)
            {
                HandleBatchError(ex);
            }
        }

        private void HandleBatchError(Exception ex)
        {
            string queueStr = SessionState.GetString(SESSION_BATCH_QUEUE, "");
            if (string.IsNullOrWhiteSpace(queueStr)) { FinishBatch(); return; }

            var queue = queueStr.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            string[] parts = queue[0].Split('|');
            string outfitName = parts[0];
            VRCPlatform platform = (VRCPlatform)Enum.Parse(typeof(VRCPlatform), parts[2]);

            bool isValidation = ex.GetType().Name.Contains("Validation") || 
                                ex.Message.Contains("bone") || ex.Message.Contains("Bone") || 
                                ex.Message.Contains("rig") || ex.Message.Contains("humanoid") || 
                                ex.Message.Contains("Chest") || ex.Message.Contains("validation");

            string shortMsg = ex.Message.Length > 120 ? ex.Message.Substring(0, 120) + "…" : ex.Message;
            string logMsg   = $"[OutfitBatchUploader] '{outfitName}' ({platform}) failed: {ex}";

            if (isValidation)
            {
                Debug.LogWarning(logMsg);
                SetStatus($"⚠ Skipped {outfitName} — validation error (see Console)", MessageType.Warning);

                string skipped = SessionState.GetString(SESSION_SKIPPED, "");
                skipped += $"{outfitName} ({platform})\n";
                SessionState.SetString(SESSION_SKIPPED, skipped);

                PopQueueAndContinue(queue);
            }
            else
            {
                Debug.LogError(logMsg);
                SetStatus($"Error on {outfitName}: {shortMsg}", MessageType.Error);

                bool cont = EditorUtility.DisplayDialog(
                    "Upload Failed",
                    $"Upload failed for '{outfitName}' on {platform}:\n{shortMsg}\n\nContinue with remaining queue?",
                    "Continue", "Stop");

                if (cont)
                    PopQueueAndContinue(queue);
                else
                    CancelBatch();
            }
        }

        private void PopQueueAndContinue(List<string> queue)
        {
            queue.RemoveAt(0);
            SessionState.SetString(SESSION_BATCH_QUEUE, string.Join("\n", queue));
            int currentIndex = SessionState.GetInt(SESSION_BATCH_INDEX, 0);
            SessionState.SetInt(SESSION_BATCH_INDEX, currentIndex + 1);

            _ = ProcessBatchQueueAsync();
        }

        private void FinishBatch()
        {
            int total = SessionState.GetInt(SESSION_BATCH_TOTAL, 0);
            string skippedStr = SessionState.GetString(SESSION_SKIPPED, "");
            var skippedList = skippedStr.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            int succeeded = total - skippedList.Length;
            if (succeeded < 0) succeeded = 0;

            string summary = $"Queue complete — {succeeded}/{total} uploads finished.";
            if (skippedList.Length > 0)
            {
                summary += $"\n\nSkipped ({skippedList.Length}) due to validation errors:\n• " +
                           string.Join("\n• ", skippedList) +
                           "\n\nFix the issues on those outfits and upload them separately.";
            }

            var finalType = skippedList.Length > 0 || succeeded < total ? MessageType.Warning : MessageType.Info;
            SessionState.SetString(SESSION_FINAL_STATUS_MSG, summary);
            SessionState.SetInt(SESSION_FINAL_STATUS_TYPE, (int)finalType);

            if (succeeded > 0 && succeeded == total)
            {
                SessionState.SetBool(SESSION_PLAY_SOUND_ON_WAKE, true);
            }

            RestoreBlendshapeSnapshot();

            SessionState.SetBool(SESSION_BATCH_ACTIVE, false);
            _isBatchUploading = false;
            _batchIndex = _batchTotal;
            
            if (skippedList.Length > 0 || succeeded < total)
                Debug.LogWarning($"[OutfitBatchUploader] {summary}");

            if (!RestoreInitialPlatform())
            {
                // No domain reload is coming, so we can fire the handler logic immediately.
                HandleFinishedBatch();
            }
            else
            {
                // A switch is coming. Clear the current status so it doesn't show a stale message before reload.
                SetStatus("", MessageType.None);
                Repaint();
            }
        }

        private void CancelBatch()
        {
            SessionState.SetBool(SESSION_BATCH_ACTIVE, false);
            _isBatchUploading = false;
            RestoreBlendshapeSnapshot();
            SetStatus("Batch upload cancelled.", MessageType.Warning);
            Repaint();

            RestoreInitialPlatform();
        }

        private bool RestoreInitialPlatform()
        {
            string initialPlatStr = SessionState.GetString(SESSION_INITIAL_PLATFORM, "");
            if (string.IsNullOrEmpty(initialPlatStr)) return false;

            bool switched = false;
            if (Enum.TryParse(initialPlatStr, out VRCPlatform initialPlat) && initialPlat != GetCurrentPlatform())
            {
                SetStatus($"Restoring initial platform to {initialPlat}...", MessageType.Info);
                Repaint();
                SwitchPlatform(initialPlat);
                switched = true;
            }

            SessionState.EraseString(SESSION_INITIAL_PLATFORM); // Clean up regardless
            return switched;
        }

        private void SaveBlendshapeSnapshot()
        {
            if (_skinRenderer == null || _skinRenderer.sharedMesh == null) return;
            var mesh = _skinRenderer.sharedMesh;
            var snap = new List<string>();
            for (int i = 0; i < mesh.blendShapeCount; i++)
                snap.Add($"{mesh.GetBlendShapeName(i)}:{_skinRenderer.GetBlendShapeWeight(i)}");
            SessionState.SetString("ShiroOutfit_BSSnap", string.Join("\n", snap));
        }

        private void RestoreBlendshapeSnapshot()
        {
            if (_skinRenderer == null || _skinRenderer.sharedMesh == null) return;
            string snapStr = SessionState.GetString("ShiroOutfit_BSSnap", "");
            if (string.IsNullOrEmpty(snapStr)) return;

            Undo.RecordObject(_skinRenderer, "Restore blendshapes after batch");
            var mesh = _skinRenderer.sharedMesh;
            foreach (string line in snapStr.Split('\n'))
            {
                if (string.IsNullOrEmpty(line)) continue;
                var parts = line.Split(':');
                if (parts.Length == 2 && float.TryParse(parts[1], out float w))
                {
                    int idx = mesh.GetBlendShapeIndex(parts[0]);
                    if (idx >= 0) _skinRenderer.SetBlendShapeWeight(idx, w);
                }
            }
            EditorUtility.SetDirty(_skinRenderer);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SessionState.EraseString("ShiroOutfit_BSSnap");
        }

        // ============================================================
        //  Helpers
        // ============================================================
        private void SetStatus(string msg, MessageType type)
        {
            _statusMessage = msg;
            _statusType    = type;
        }

        private static void DrawSeparator()
        {
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        }

        private void InitStyles()
        {
            if (_stylesInited) return;
            _stylesInited = true;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 15,
                alignment = TextAnchor.MiddleLeft
            };

            _activeRowStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(6, 6, 4, 4),
                margin  = new RectOffset(0, 0, 0, 0),
                normal  = { background = MakeTex(2, 2, new Color(0.15f, 0.45f, 0.15f, 0.35f)) }
            };

            _inactiveRowStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(6, 6, 4, 4),
                margin  = new RectOffset(0, 0, 0, 0)
            };
        }

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            var t = new Texture2D(w, h);
            t.SetPixels(pix);
            t.Apply();
            return t;
        }

        // ============================================================
        //  Data
        // ============================================================
        [Serializable]
        public class OutfitEntry
        {
            public GameObject                  Go;
            public string                      Name;
            public string                      BlueprintId      = "";
            public bool                        IncludeInBatch   = true;
            public bool                        BuildWindows     = true;
            public bool                        BuildAndroid     = false;
            public bool                        BuildIOS         = false;
            public string                      PrefsKey         = "";   // scoped by avatar name
            // Blendshape overrides: name → value (0-100). Only entries present here are applied.
            public Dictionary<string, float>   BlendShapes      = new Dictionary<string, float>();
            public bool                        BlendShapeExpanded = false;
            public string                      BlendShapeSearch   = "";
        }

        public enum VRCPlatform
        {
            Windows,
            Android,
            iOS
        }
    }

    public static class AvatarVersionManager
    {
        private static readonly string ConfigPath;
        private static Dictionary<string, string> _versions;

        [Serializable]
        private class VersionData
        {
            public List<VersionEntry> versions = new List<VersionEntry>();
        }

        [Serializable]
        private class VersionEntry
        {
            public string blueprintId;
            public string version;
        }

        static AvatarVersionManager()
        {
            // Save locally to this specific Unity project in the ProjectSettings folder
            ConfigPath = Path.Combine("ProjectSettings", "ShiroOutfit_versions.json");
            LoadVersions();
        }

        private static void LoadVersions()
        {
            _versions = new Dictionary<string, string>();
            if (!File.Exists(ConfigPath)) return;

            try
            {
                string json = File.ReadAllText(ConfigPath);
                var data = JsonUtility.FromJson<VersionData>(json);
                if (data?.versions != null)
                {
                    foreach (var entry in data.versions)
                        if (!string.IsNullOrWhiteSpace(entry.blueprintId))
                            _versions[entry.blueprintId] = entry.version;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AvatarVersionManager] Failed to load versions: {ex.Message}");
            }
        }

        private static void SaveVersions()
        {
            try
            {
                var data = new VersionData();
                foreach (var kvp in _versions)
                    data.versions.Add(new VersionEntry { blueprintId = kvp.Key, version = kvp.Value });
                
                string json = JsonUtility.ToJson(data, true);
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AvatarVersionManager] Failed to save versions: {ex.Message}");
            }
        }

        public static string GetVersion(string blueprintId)
        {
            if (string.IsNullOrWhiteSpace(blueprintId)) return "";
            _versions.TryGetValue(blueprintId, out string version);
            return version ?? "";
        }

        public static void SetVersion(string blueprintId, string version)
        {
            if (string.IsNullOrWhiteSpace(blueprintId)) return;
            _versions[blueprintId] = version;
            SaveVersions();
        }
    }
}
