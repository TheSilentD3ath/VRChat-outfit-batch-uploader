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
    public partial class OutfitBatchUploader : EditorWindow
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
        private const string SESSION_FAILED       = "Shiro_BatchFailed";
        private const string SESSION_INITIAL_PLATFORM = "Shiro_InitialPlatform";
        private const string SESSION_FINAL_STATUS_MSG = "Shiro_FinalStatusMsg";
        private const string SESSION_FINAL_STATUS_TYPE = "Shiro_FinalStatusType";
        private const string SESSION_PLAY_SOUND_ON_WAKE = "Shiro_PlaySoundOnWake";
        private const string SESSION_BATCH_VERSION = "Shiro_BatchVersion";

        private const string PREFS_VERSION_MODE = "ShiroOutfitUploader_VersionMode"; // 0 = replace description, 1 = append "v<version>" line

        // ---- Batch queue (JSON in SessionState — robust against any characters in outfit names) ----
        [Serializable]
        private class QueueItem
        {
            public string outfit;
            public string id;
            public string platform;
        }

        [Serializable]
        private class QueueList
        {
            public List<QueueItem> items = new List<QueueItem>();
        }

        private static List<QueueItem> LoadQueue(string sessionKey)
        {
            string s = SessionState.GetString(sessionKey, "");
            if (string.IsNullOrEmpty(s)) return new List<QueueItem>();
            try
            {
                var q = JsonUtility.FromJson<QueueList>(s);
                return q?.items ?? new List<QueueItem>();
            }
            catch { return new List<QueueItem>(); }
        }

        private static void SaveQueue(string sessionKey, List<QueueItem> items)
        {
            if (items == null || items.Count == 0) SessionState.EraseString(sessionKey);
            else SessionState.SetString(sessionKey, JsonUtility.ToJson(new QueueList { items = items }));
        }

        private static VRCPlatform ParsePlatform(string s) =>
            Enum.TryParse(s, out VRCPlatform p) ? p : VRCPlatform.Windows;

        // ---- Blueprint ID validation ----
        private static readonly System.Text.RegularExpressions.Regex BLUEPRINT_ID_REGEX =
            new System.Text.RegularExpressions.Regex(
                @"^avtr_[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");

        private static bool IsValidBlueprintId(string id) =>
            !string.IsNullOrWhiteSpace(id) && BLUEPRINT_ID_REGEX.IsMatch(id.Trim());

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

            // Resume a new-avatar (Express) upload if a domain reload interrupted it
            TryResumeExpress();
        }

        private void OnDisable()
        {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorApplication.update -= HandleResumeExpress;
            EditorApplication.update -= HandleResumeBatch;
            EditorApplication.update -= HandleFinishedBatch;
            StopConsentWatcher();
            _cts?.Dispose();
            _cts = null;
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
            _cts?.Dispose();
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

            FlashTaskbar();
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

            // Data is scoped by avatar name inside the project-local JSON store
            // (ProjectSettings/ShiroOutfit_data.json — survives plugin updates).
            string avatarKey = _avatarRoot.name;

            foreach (Transform child in _outfitsParent.transform)
            {
                var data = OutfitProjectData.GetOutfit(avatarKey, child.gameObject.name);
                var entry = new OutfitEntry
                {
                    Go             = child.gameObject,
                    Name           = child.gameObject.name,
                    Data           = data,
                    BlueprintId    = data.blueprintId,
                    IncludeInBatch = data.includeInBatch,
                    BuildWindows   = data.buildWindows,
                    BuildAndroid   = data.buildAndroid,
                    BuildIOS       = data.buildIOS
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
            var pm = _avatarRoot.GetComponentInChildren<PipelineManager>();
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
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    $"Outfits ({_outfits.Count})  —  parent: \"{_outfitsParent.name}\"",
                    EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(_isBatchUploading || _isExpressBusy))
                {
                    EditorGUILayout.LabelField("Include in batch:", EditorStyles.miniLabel, GUILayout.Width(96));
                    if (GUILayout.Button("All", EditorStyles.miniButton, GUILayout.Width(40)))  SetAllOutfitsIncluded(true);
                    if (GUILayout.Button("None", EditorStyles.miniButton, GUILayout.Width(44))) SetAllOutfitsIncluded(false);
                }
            }
            EditorGUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _outfits.Count; i++)
                DrawOutfitRow(_outfits[i]);
            EditorGUILayout.EndScrollView();

            DrawSeparator();
            DrawNewOutfitSetupSection();

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

                using (new EditorGUI.DisabledScope(_isBatchUploading || _isExpressBusy || _isFetchingAvatars))
                {
                    if (GUILayout.Button(new GUIContent("☁ IDs",
                            "Fetch your avatars from VRChat and auto-match Blueprint IDs to outfits by name"),
                        EditorStyles.miniButton, GUILayout.Width(48)))
                        _ = FetchAndMatchAsync();
                }
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

                    EditorGUI.BeginChangeCheck();
                    int vMode = EditorPrefs.GetInt(PREFS_VERSION_MODE, 0);
                    vMode = EditorGUILayout.Popup(vMode, new[]
                    {
                        "replaces description",
                        "appends \"v…\" line"
                    }, GUILayout.Width(140));
                    if (EditorGUI.EndChangeCheck())
                        EditorPrefs.SetInt(PREFS_VERSION_MODE, vMode);
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

        // ---- Select / deselect all outfits for batch ----
        private void SetAllOutfitsIncluded(bool include)
        {
            foreach (var o in _outfits)
            {
                if (o == null) continue;
                o.IncludeInBatch = include;
                if (o.Data != null) o.Data.includeInBatch = include;
            }
            OutfitProjectData.Save();
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

                        if (GUILayout.Button(new GUIContent("VRAM", "Optimize this outfit's textures (compression + resolution)"), GUILayout.Width(50)))
                            OptimizeOutfitTextures(entry);

                        using (new EditorGUI.DisabledScope(!IsValidBlueprintId(entry.BlueprintId) || _isExpressBusy))
                        {
                            if (GUILayout.Button(new GUIContent("Thumb", "Capture & upload a new VRChat thumbnail for this outfit (with preview — nothing else is rebuilt)"), GUILayout.Width(52)))
                                StartThumbnailUpdate(entry);
                        }
                    }
                }

                // Row 2: Blueprint ID + Upload button
                bool hasBlueprintId = !string.IsNullOrWhiteSpace(entry.BlueprintId);
                bool idValid = !hasBlueprintId || IsValidBlueprintId(entry.BlueprintId);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Blueprint ID:", GUILayout.Width(82));
                    var oldBg = GUI.backgroundColor;
                    if (!idValid) GUI.backgroundColor = new Color(1f, 0.45f, 0.45f);
                    EditorGUI.BeginChangeCheck();
                    entry.BlueprintId = EditorGUILayout.TextField(entry.BlueprintId ?? "", GUILayout.ExpandWidth(true));
                    if (EditorGUI.EndChangeCheck())
                    {
                        entry.BlueprintId = (entry.BlueprintId ?? "").Trim();
                        if (entry.Data != null) { entry.Data.blueprintId = entry.BlueprintId; OutfitProjectData.Save(); }
                    }
                    GUI.backgroundColor = oldBg;

                    using (new EditorGUI.DisabledScope(_isBatchUploading || _isExpressBusy || _isFetchingAvatars))
                    {
                        if (GUILayout.Button(new GUIContent("▾", "Pick from your VRChat avatars"), GUILayout.Width(22)))
                            _ = ShowAvatarPickerAsync(entry);
                    }

                    if (hasBlueprintId)
                    {
                        using (new EditorGUI.DisabledScope(_isBatchUploading))
                        {
                            if (GUILayout.Button("Upload", GUILayout.Width(56)))
                            {
                                ActivateOutfit(entry);
                                _ = StartBatchAsync(new List<OutfitEntry> { entry });
                            }
                        }
                    }
                }

                if (!idValid)
                    EditorGUILayout.HelpBox(
                        "This doesn't look like a valid Blueprint ID (expected: avtr_ followed by a GUID). " +
                        "Uploading with a wrong ID can overwrite a different avatar!", MessageType.Warning);

                // New outfit (no Blueprint ID yet) → inline first-time setup buttons
                if (!hasBlueprintId)
                    DrawInlineNewOutfitButtons(entry);

                // Row 3: batch include toggle
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    entry.IncludeInBatch = EditorGUILayout.ToggleLeft("Include in batch upload", entry.IncludeInBatch, GUILayout.Width(160));
                    if (EditorGUI.EndChangeCheck() && entry.Data != null)
                    {
                        entry.Data.includeInBatch = entry.IncludeInBatch;
                        OutfitProjectData.Save();
                    }

                    GUILayout.FlexibleSpace();

                    EditorGUI.BeginChangeCheck();
                    entry.BuildWindows = EditorGUILayout.ToggleLeft("Win", entry.BuildWindows, GUILayout.Width(45));
                    entry.BuildAndroid = EditorGUILayout.ToggleLeft("And", entry.BuildAndroid, GUILayout.Width(45));
                    entry.BuildIOS     = EditorGUILayout.ToggleLeft("iOS", entry.BuildIOS, GUILayout.Width(40));
                    if (EditorGUI.EndChangeCheck() && entry.Data != null)
                    {
                        entry.Data.buildWindows = entry.BuildWindows;
                        entry.Data.buildAndroid = entry.BuildAndroid;
                        entry.Data.buildIOS     = entry.BuildIOS;
                        OutfitProjectData.Save();
                    }
                }

                // Last-upload info (per platform)
                if (hasBlueprintId && entry.Data != null)
                {
                    string last = $"Last upload — Win: {AgoLabel(entry.Data.lastUploadWindows)}";
                    if (entry.BuildAndroid || !string.IsNullOrEmpty(entry.Data.lastUploadAndroid))
                        last += $" · And: {AgoLabel(entry.Data.lastUploadAndroid)}";
                    if (entry.BuildIOS || !string.IsNullOrEmpty(entry.Data.lastUploadIOS))
                        last += $" · iOS: {AgoLabel(entry.Data.lastUploadIOS)}";
                    EditorGUILayout.LabelField(last, EditorStyles.miniLabel);
                }

                // Row 4: blendshape foldout
                DrawBlendShapeFoldout(entry);

                // Per-outfit contact budget + item (accessory) selection + FaceEmo
                DrawContactCounter(entry);
                DrawOutfitItems(entry);
                DrawOutfitFaceEmo(entry);
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

            // Scrollable list so long blendshape lists don't overflow the window
            entry.BlendShapeScroll = EditorGUILayout.BeginScrollView(
                entry.BlendShapeScroll, GUILayout.Height(Mathf.Min(260f, Mathf.Max(1, bsCount) * 20f + 4f)));

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
            EditorGUILayout.EndScrollView();

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
            int included  = _outfits.Count(o => o.IncludeInBatch);
            int ready     = _outfits.Count(o => o.IncludeInBatch && !string.IsNullOrWhiteSpace(o.BlueprintId));
            int needSetup = included - ready;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Batch Upload", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(_isBatchUploading || _isExpressBusy))
                {
                    if (GUILayout.Button(new GUIContent("Dry run",
                            "Checks everything the batch would check (IDs, platforms, budgets, …) without uploading anything."),
                        EditorStyles.miniButton, GUILayout.Width(60)))
                        RunDryRun();
                }
                EditorGUI.BeginChangeCheck();
                _soundEnabled = EditorGUILayout.ToggleLeft("🔔 Sound when done", _soundEnabled, GUILayout.Width(140));
                if (EditorGUI.EndChangeCheck())
                    EditorPrefs.SetBool(PREFS_SOUND_ENABLED, _soundEnabled);
            }
            EditorGUILayout.LabelField(
                needSetup > 0
                    ? $"{included} selected — {ready} ready, {needSetup} need setup (you'll be asked to Express / configure them first)"
                    : $"{ready} outfit(s) ready  (have a Blueprint ID + \"Include in batch\" checked)",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (!_isBatchUploading)
                {
                    using (new EditorGUI.DisabledScope(included == 0 || _isExpressBusy))
                    {
                        Color oldColor = GUI.backgroundColor;
                        bool isSuccess = _statusMessage.StartsWith("Queue complete") && _statusType == MessageType.Info;
                        if (isSuccess) GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);

                        string btnLabel = needSetup > 0
                            ? $"Upload All ({included})  —  {needSetup} need setup"
                            : $"Batch Upload All ({ready})";
                        if (GUILayout.Button(btnLabel, GUILayout.Height(30)))
                        {
                            var includedAll = _outfits.Where(o => o.IncludeInBatch).ToList();
                            _ = StartBatchWithSetupAsync(includedAll);
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
                    var pendingQueue = LoadQueue(SESSION_BATCH_QUEUE);
                    if (pendingQueue.Count > 0)
                        currentPlat = ParsePlatform(pendingQueue[0].platform);

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

            // Retry failed/skipped uploads from the last batch
            if (!_isBatchUploading)
            {
                var lastFailed = LoadQueue(SESSION_FAILED);
                if (lastFailed.Count > 0)
                {
                    EditorGUILayout.Space(2);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(_isExpressBusy))
                        {
                            Color oldColor = GUI.backgroundColor;
                            GUI.backgroundColor = new Color(0.95f, 0.75f, 0.3f);
                            if (GUILayout.Button($"↻ Retry failed ({lastFailed.Count})", GUILayout.Height(24)))
                                _ = RetryFailedAsync(lastFailed);
                            GUI.backgroundColor = oldColor;
                        }
                        if (GUILayout.Button("Dismiss", GUILayout.Width(70), GUILayout.Height(24)))
                            SessionState.EraseString(SESSION_FAILED);
                    }
                }
            }

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
            }
        }

        /// <summary>Re-queues the failed items of the last batch (only those whose outfit still exists).</summary>
        private async Task RetryFailedAsync(List<QueueItem> failed)
        {
            var valid = failed.Where(f => _outfits.Any(o => o.Name == f.outfit)).ToList();
            if (valid.Count == 0)
            {
                SetStatus("None of the failed outfits exist in the scene anymore.", MessageType.Warning);
                SessionState.EraseString(SESSION_FAILED);
                return;
            }

            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out _))
            { SetStatus("VRC SDK builder not available — open the VRChat SDK window first.", MessageType.Error); return; }
            if (!APIUser.IsLoggedIn)
            { SetStatus("Not logged in. Please open the VRChat SDK Control Panel and log in first.", MessageType.Error); return; }

            bool consented = await PreConsentAllAsync(valid.Select(f => f.id).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct());
            if (!consented)
            {
                SetStatus("Retry cancelled — ownership not confirmed.", MessageType.Warning);
                return;
            }

            StartQueuedBatch(valid, GetCurrentPlatform());
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

            // Apply item (accessory) include/exclude tags for THIS outfit's selection
            ApplyItemStates(target);

            // Apply this outfit's FaceEmo (active → Untagged, other outfits' FaceEmo → EditorOnly)
            ApplyFaceEmoStates(target);

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            SetStatus($"✓ Activated: {target.Name}", MessageType.Info);
            Repaint();
        }

        // ---- Blendshape persistence ----

        private static void SaveBlendShapes(OutfitEntry entry)
        {
            if (entry.Data == null) return;
            entry.Data.blendShapes.Clear();
            foreach (var kv in entry.BlendShapes)
                entry.Data.blendShapes.Add(new OutfitProjectData.BlendShapeOverride { name = kv.Key, value = kv.Value });
            OutfitProjectData.Save();
        }

        private static void LoadBlendShapes(OutfitEntry entry)
        {
            entry.BlendShapes.Clear();
            if (entry.Data == null) return;
            foreach (var bs in entry.Data.blendShapes)
                if (!string.IsNullOrEmpty(bs.name))
                    entry.BlendShapes[bs.name] = bs.value;
        }

        // ---- Confirm sound ----
        /// <summary>Finds the confirm AudioClip even if the tool folder was renamed:
        /// tries the historical hard-coded path first, then searches the whole project by name.</summary>
        private static AudioClip LoadConfirmClip()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SOUND_ASSET_PATH);
            if (clip != null) return clip;

            foreach (var guid in AssetDatabase.FindAssets("UI Confirm Sound t:AudioClip"))
            {
                var found = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (found != null) return found;
            }
            return null;
        }

        private void PlayConfirmSound()
        {
            if (!_soundEnabled) return;

            var clip = LoadConfirmClip();
            if (clip == null)
            {
                Debug.LogWarning("[OutfitBatchUploader] Could not find the confirm sound 'UI Confirm Sound' anywhere in the project.");
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

            // Validate blueprint IDs before doing anything
            var badIds = targetOutfits
                .Where(o => !string.IsNullOrWhiteSpace(o.BlueprintId) && !IsValidBlueprintId(o.BlueprintId))
                .Select(o => o.Name).ToList();
            if (badIds.Count > 0)
            {
                SetStatus("Invalid Blueprint ID on: " + string.Join(", ", badIds) +
                          " — expected format avtr_<GUID>. Fix before uploading.", MessageType.Error);
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
            var queue = new List<QueueItem>();
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
                        queue.Add(new QueueItem
                        {
                            outfit   = outfit.Name,
                            id       = outfit.BlueprintId,
                            platform = plat.ToString()
                        });
                    }
                }
            }

            if (queue.Count == 0)
            {
                SetStatus("No platforms configured for the target outfits.", MessageType.Warning);
                return;
            }

            StartQueuedBatch(queue, currentPlatform);
        }

        /// <summary>Arms the SessionState queue and kicks off processing.
        /// Used by StartBatchAsync and by "Retry failed".</summary>
        private void StartQueuedBatch(List<QueueItem> queue, VRCPlatform currentPlatform)
        {
            SaveBlendshapeSnapshot();

            // Save queue into Domain-Reload-proof SessionState
            SaveQueue(SESSION_BATCH_QUEUE, queue);
            SessionState.SetInt(SESSION_BATCH_TOTAL, queue.Count);
            SessionState.SetInt(SESSION_BATCH_INDEX, 0);
            SessionState.SetBool(SESSION_BATCH_ACTIVE, true);
            SessionState.EraseString(SESSION_FAILED);
            SessionState.SetString(SESSION_INITIAL_PLATFORM, currentPlatform.ToString());
            SessionState.SetString(SESSION_BATCH_VERSION, _avatarVersion); // Capture the version from UI

            _isBatchUploading = true;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            LogUpload($"Batch started — {queue.Count} upload(s): " +
                      string.Join(", ", queue.Select(q => $"{q.outfit} ({q.platform})")));

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

                    var queue = LoadQueue(SESSION_BATCH_QUEUE);
                    if (queue.Count == 0)
                    {
                        FinishBatch();
                        return;
                    }

                    int total = SessionState.GetInt(SESSION_BATCH_TOTAL, 0);
                    int currentIndex = SessionState.GetInt(SESSION_BATCH_INDEX, 0);

                    _batchIndex = currentIndex;
                    _batchTotal = total;
                    _batchSubProgress = 0.0f;

                    string outfitName  = queue[0].outfit;
                    string blueprintId = queue[0].id;
                    VRCPlatform platform = ParsePlatform(queue[0].platform);

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
                        // (-= first so the handler can never be registered twice.)
                        EditorApplication.update -= HandleResumeBatch;
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
                        SaveQueue(SESSION_BATCH_QUEUE, queue);
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

                    // Stamp the version into the description if one was typed in (not blank)
                    string versionToSet = SessionState.GetString(SESSION_BATCH_VERSION, ""); // Use the version captured at batch start
                    if (!string.IsNullOrWhiteSpace(versionToSet))
                    {
                        string stamped = StampVersion(avatar.Description, versionToSet);
                        if (avatar.Description != stamped)
                        {
                            avatar.Description = stamped;
                            Debug.Log($"[OutfitBatchUploader] Updating '{outfitName}' description for version: {versionToSet}");
                        }

                        // Save it for this specific outfit's blueprint ID so it's remembered across projects!
                        AvatarVersionManager.SetVersion(blueprintId, versionToSet);
                    }

                    await builder.BuildAndUpload(_avatarRoot, avatar, cancellationToken: _cts.Token);

                    // Successful upload! Pop from queue
                    LogUpload($"OK    {outfitName} ({platform}) → {blueprintId}" +
                              (string.IsNullOrWhiteSpace(versionToSet) ? "" : $"  v: {versionToSet}"));
                    OutfitProjectData.MarkUploaded(outfit.Data, platform.ToString());
                    _batchSubProgress = 1.0f;
                    Repaint();
                    queue.RemoveAt(0);
                    SaveQueue(SESSION_BATCH_QUEUE, queue);
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
            var queue = LoadQueue(SESSION_BATCH_QUEUE);
            if (queue.Count == 0) { FinishBatch(); return; }

            QueueItem failed = queue[0];
            string outfitName = failed.outfit;
            VRCPlatform platform = ParsePlatform(failed.platform);

            bool isValidation = ex.GetType().Name.Contains("Validation") ||
                                ex.Message.Contains("bone") || ex.Message.Contains("Bone") ||
                                ex.Message.Contains("rig") || ex.Message.Contains("humanoid") ||
                                ex.Message.Contains("Chest") || ex.Message.Contains("validation");

            string shortMsg = ex.Message.Length > 120 ? ex.Message.Substring(0, 120) + "…" : ex.Message;
            string logMsg   = $"[OutfitBatchUploader] '{outfitName}' ({platform}) failed: {ex}";

            // Remember the failed item so it can be retried after the batch
            var failedList = LoadQueue(SESSION_FAILED);
            failedList.Add(failed);
            SaveQueue(SESSION_FAILED, failedList);

            LogUpload($"FAIL  {outfitName} ({platform}): {ex.Message}");

            if (isValidation)
            {
                Debug.LogWarning(logMsg);
                SetStatus($"⚠ Skipped {outfitName} — validation error (see Console)", MessageType.Warning);
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

        private void PopQueueAndContinue(List<QueueItem> queue)
        {
            queue.RemoveAt(0);
            SaveQueue(SESSION_BATCH_QUEUE, queue);
            int currentIndex = SessionState.GetInt(SESSION_BATCH_INDEX, 0);
            SessionState.SetInt(SESSION_BATCH_INDEX, currentIndex + 1);

            _ = ProcessBatchQueueAsync();
        }

        private void FinishBatch()
        {
            int total = SessionState.GetInt(SESSION_BATCH_TOTAL, 0);
            var failedList = LoadQueue(SESSION_FAILED);

            int succeeded = total - failedList.Count;
            if (succeeded < 0) succeeded = 0;

            string summary = $"Queue complete — {succeeded}/{total} uploads finished.";
            if (failedList.Count > 0)
            {
                summary += $"\n\nFailed/skipped ({failedList.Count}):\n• " +
                           string.Join("\n• ", failedList.Select(f => $"{f.outfit} ({f.platform})")) +
                           "\n\nUse \"Retry failed\" below, or fix the issues and upload them separately.";
            }

            var finalType = failedList.Count > 0 || succeeded < total ? MessageType.Warning : MessageType.Info;
            LogUpload($"Batch finished — {succeeded}/{total} succeeded" +
                      (failedList.Count > 0 ? $", failed: {string.Join(", ", failedList.Select(f => f.outfit))}" : "") + ".");
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

            if (failedList.Count > 0 || succeeded < total)
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
            LogUpload("Batch cancelled.");
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

        [Serializable]
        private class BlendShapeSnapshot
        {
            public List<string> names   = new List<string>();
            public List<float>  weights = new List<float>();
        }

        private void SaveBlendshapeSnapshot()
        {
            if (_skinRenderer == null || _skinRenderer.sharedMesh == null) return;
            var mesh = _skinRenderer.sharedMesh;
            var snap = new BlendShapeSnapshot();
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                snap.names.Add(mesh.GetBlendShapeName(i));
                snap.weights.Add(_skinRenderer.GetBlendShapeWeight(i));
            }
            SessionState.SetString("ShiroOutfit_BSSnap", JsonUtility.ToJson(snap));
        }

        private void RestoreBlendshapeSnapshot()
        {
            if (_skinRenderer == null || _skinRenderer.sharedMesh == null) return;
            string snapStr = SessionState.GetString("ShiroOutfit_BSSnap", "");
            if (string.IsNullOrEmpty(snapStr)) return;

            BlendShapeSnapshot snap = null;
            try { snap = JsonUtility.FromJson<BlendShapeSnapshot>(snapStr); } catch { }
            if (snap == null || snap.names == null || snap.weights == null ||
                snap.names.Count != snap.weights.Count)
            {
                SessionState.EraseString("ShiroOutfit_BSSnap");
                return;
            }

            Undo.RecordObject(_skinRenderer, "Restore blendshapes after batch");
            var mesh = _skinRenderer.sharedMesh;
            for (int i = 0; i < snap.names.Count; i++)
            {
                int idx = mesh.GetBlendShapeIndex(snap.names[i]);
                if (idx >= 0) _skinRenderer.SetBlendShapeWeight(idx, snap.weights[i]);
            }
            EditorUtility.SetDirty(_skinRenderer);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SessionState.EraseString("ShiroOutfit_BSSnap");
        }

        // ============================================================
        //  Helpers
        // ============================================================

        /// <summary>"3d ago" style label for a stored "yyyy-MM-dd HH:mm" timestamp.</summary>
        private static string AgoLabel(string stamp)
        {
            if (string.IsNullOrEmpty(stamp)) return "never";
            if (!DateTime.TryParseExact(stamp, "yyyy-MM-dd HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
                return "?";
            var span = DateTime.Now - dt;
            if (span.TotalMinutes < 1)  return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours  < 24)  return $"{(int)span.TotalHours}h ago";
            return $"{(int)span.TotalDays}d ago";
        }

#if UNITY_EDITOR_WIN
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
#endif

        /// <summary>Flashes the Unity taskbar icon until the window gets focus —
        /// so long batches get noticed even when you're AFK / in another app.</summary>
        private static void FlashTaskbar()
        {
#if UNITY_EDITOR_WIN
            try
            {
                IntPtr h = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (h == IntPtr.Zero) return;
                var fi = new FLASHWINFO
                {
                    cbSize    = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(FLASHWINFO)),
                    hwnd      = h,
                    dwFlags   = 3u | 12u,   // FLASHW_ALL | FLASHW_TIMERNOFG
                    uCount    = 0,
                    dwTimeout = 0
                };
                FlashWindowEx(ref fi);
            }
            catch { /* purely cosmetic */ }
#endif
        }

        /// <summary>Applies the version to the description according to the chosen mode:
        /// 0 = description becomes the version string (classic behavior),
        /// 1 = description is kept, a "v&lt;version&gt;" line is appended/updated at the end.</summary>
        private static string StampVersion(string description, string version)
        {
            int mode = EditorPrefs.GetInt(PREFS_VERSION_MODE, 0);
            if (mode == 0) return version;

            string stamp = version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : "v" + version;

            // Drop any previous version-stamp lines (e.g. "v1.2", "V2.0.1")
            var lines = (description ?? "")
                .Split('\n')
                .Where(l => !System.Text.RegularExpressions.Regex.IsMatch(
                    l.Trim(), @"^[vV]\d[\w\.\-]*$"))
                .ToList();

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                lines.RemoveAt(lines.Count - 1);

            lines.Add(stamp);
            return string.Join("\n", lines).Trim();
        }

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
            internal OutfitProjectData.OutfitData Data;   // project-local persistent record
            // Blendshape overrides: name → value (0-100). Only entries present here are applied.
            public Dictionary<string, float>   BlendShapes      = new Dictionary<string, float>();
            public bool                        BlendShapeExpanded = false;
            public string                      BlendShapeSearch   = "";
            public Vector2                     BlendShapeScroll;
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

        internal static string ExportRaw()
        {
            var data = new VersionData();
            foreach (var kvp in _versions)
                data.versions.Add(new VersionEntry { blueprintId = kvp.Key, version = kvp.Value });
            return JsonUtility.ToJson(data);
        }

        internal static bool ImportRaw(string json)
        {
            try
            {
                var data = JsonUtility.FromJson<VersionData>(json);
                if (data?.versions == null) return false;
                _versions.Clear();
                foreach (var e in data.versions)
                    if (!string.IsNullOrWhiteSpace(e.blueprintId))
                        _versions[e.blueprintId] = e.version;
                SaveVersions();
                return true;
            }
            catch { return false; }
        }
    }
}
