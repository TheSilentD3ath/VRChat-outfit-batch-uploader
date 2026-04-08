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

        // ---- State ----
        private GameObject           _avatarRoot;
        private List<GameObject>     _avatarsInScene   = new List<GameObject>();
        private SkinnedMeshRenderer  _skinRenderer;
        private GameObject           _outfitsParent;
        private List<OutfitEntry>    _outfits          = new List<OutfitEntry>();
        private string               _outfitsParentName = DEFAULT_PARENT_NAME;
        private Vector2              _scroll;
        private bool               _soundEnabled;
        private bool               _isBatchUploading;
        private int                _batchIndex;
        private int                _batchTotal;
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
            }
        }

        /// <summary>Finds the first SkinnedMeshRenderer that is a direct child of the avatar root
        /// and has blendshapes — typically the body/skin mesh.</summary>
        private void AutoDetectSkin()
        {
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
            foreach (Transform child in _outfitsParent.transform)
            {
                string prefKey = PREFS_PREFIX + avatarKey + "_" + child.gameObject.name;
                var entry = new OutfitEntry
                {
                    Go             = child.gameObject,
                    Name           = child.gameObject.name,
                    BlueprintId    = EditorPrefs.GetString(prefKey, ""),
                    IncludeInBatch = EditorPrefs.GetBool(prefKey + "_batch", true),
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
                            _ = UploadSingleAsync(entry);
                        }
                    }
                }

                // Row 3: batch include toggle
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    entry.IncludeInBatch = EditorGUILayout.ToggleLeft("Include in batch upload", entry.IncludeInBatch);
                    if (EditorGUI.EndChangeCheck())
                        EditorPrefs.SetBool(entry.PrefsKey + "_batch", entry.IncludeInBatch);
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
                        if (GUILayout.Button($"Batch Upload All ({ready})", GUILayout.Height(30)))
                        {
                            _cts = new CancellationTokenSource();
                            _ = BatchUploadAsync(_cts.Token);
                        }
                    }
                }
                else
                {
                    float progress = _batchTotal > 0 ? (float)_batchIndex / _batchTotal : 0f;
                    Rect r = EditorGUILayout.GetControlRect(GUILayout.Height(30), GUILayout.ExpandWidth(true));
                    EditorGUI.ProgressBar(r, progress, $"Uploading {_batchIndex + 1} / {_batchTotal}…");

                    if (GUILayout.Button("Cancel", GUILayout.Width(66), GUILayout.Height(30)))
                        _cts?.Cancel();
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

        // ---- Single upload ----
        private async Task UploadSingleAsync(OutfitEntry outfit)
        {
            if (_avatarRoot == null)                           { SetStatus("No avatar root.", MessageType.Error); return; }
            if (string.IsNullOrWhiteSpace(outfit.BlueprintId)) { SetStatus("No Blueprint ID set.", MessageType.Error); return; }

            _isBatchUploading = true;
            _batchIndex = 0; _batchTotal = 1;
            SetStatus($"Activating {outfit.Name}…", MessageType.Info);
            Repaint();

            try
            {
                if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out var builder))
                {
                    SetStatus("VRC SDK builder not available — open the VRChat SDK window first.", MessageType.Error);
                    return;
                }

                // Ask once for ownership confirmation, then pre-register consent with VRChat API
                bool consented = await PreConsentAllAsync(new[] { outfit.BlueprintId });
                if (!consented) { SetStatus("Upload cancelled — ownership not confirmed.", MessageType.Warning); return; }

                // Only activate if not already correctly set (user may have done it manually)
                bool alreadyActive = outfit.Go != null &&
                                     outfit.Go.activeSelf &&
                                     outfit.Go.CompareTag("Untagged");
                if (!alreadyActive)
                {
                    ActivateOutfit(outfit);
                    FlushScene();
                    await Task.Delay(1200);
                }

                SetStatus($"Uploading {outfit.Name}…", MessageType.Info);
                Repaint();

                var avatar = await VRCApi.GetAvatar(outfit.BlueprintId);
                await builder.BuildAndUpload(_avatarRoot, avatar);

                SetStatus($"✓ Uploaded: {outfit.Name}", MessageType.Info);
            }
            catch (Exception ex)
            {
                SetStatus($"Upload failed: {ex.Message}", MessageType.Error);
                Debug.LogError($"[OutfitBatchUploader] Single upload error for '{outfit.Name}': {ex}");
            }
            finally
            {
                _isBatchUploading = false;
                Repaint();
            }
        }

        // ---- Batch upload ----
        private async Task BatchUploadAsync(CancellationToken ct)
        {
            var batch = _outfits
                .Where(o => o.IncludeInBatch && !string.IsNullOrWhiteSpace(o.BlueprintId))
                .ToList();

            if (batch.Count == 0) return;

            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out var builder))
            {
                SetStatus("VRC SDK builder not available — open the VRChat SDK window first.", MessageType.Error);
                return;
            }

            // Ask ONCE for all outfits — pre-registers consent so SDK dialog never appears mid-batch
            bool consented = await PreConsentAllAsync(batch.Select(o => o.BlueprintId));
            if (!consented)
            {
                SetStatus("Batch cancelled — ownership not confirmed.", MessageType.Warning);
                return;
            }

            // Snapshot current blendshape values so we can restore them after the batch
            Dictionary<string, float> blendShapeSnapshot = null;
            if (_skinRenderer != null && _skinRenderer.sharedMesh != null)
            {
                blendShapeSnapshot = new Dictionary<string, float>();
                var mesh = _skinRenderer.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                    blendShapeSnapshot[mesh.GetBlendShapeName(i)] = _skinRenderer.GetBlendShapeWeight(i);
            }

            _isBatchUploading = true;
            _batchTotal       = batch.Count;
            _batchIndex       = 0;

            int succeeded = 0;
            var skipped   = new List<string>();

            for (int i = 0; i < batch.Count; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    SetStatus("Batch upload cancelled.", MessageType.Warning);
                    break;
                }

                var outfit = batch[i];
                _batchIndex = i;

                try
                {
                    // Step 1 — activate outfit if not already correctly set by the user
                    bool alreadyActive = outfit.Go != null &&
                                        outfit.Go.activeSelf &&
                                        outfit.Go.CompareTag("Untagged");

                    if (alreadyActive)
                    {
                        SetStatus($"[{i + 1}/{batch.Count}] {outfit.Name} already active — skipping switch.", MessageType.Info);
                        Repaint();
                    }
                    else
                    {
                        SetStatus($"[{i + 1}/{batch.Count}] Activating {outfit.Name}…", MessageType.Info);
                        Repaint();
                        ActivateOutfit(outfit);
                        FlushScene();
                        await Task.Delay(1500, ct);
                    }

                    // Step 2 — upload
                    SetStatus($"[{i + 1}/{batch.Count}] Uploading {outfit.Name}…", MessageType.Info);
                    Repaint();

                    var avatar = await VRCApi.GetAvatar(outfit.BlueprintId, cancellationToken: ct);
                    await builder.BuildAndUpload(_avatarRoot, avatar, cancellationToken: ct);

                    succeeded++;
                    SetStatus($"[{i + 1}/{batch.Count}] ✓ Done: {outfit.Name}", MessageType.Info);
                    Repaint();

                    // Brief pause between uploads so VRChat API isn't hammered
                    if (i < batch.Count - 1)
                        await Task.Delay(2000, ct);
                }
                catch (OperationCanceledException)
                {
                    SetStatus("Batch upload cancelled.", MessageType.Warning);
                    break;
                }
                catch (Exception ex)
                {
                    // Check if this is a known validation error (e.g. missing bones) — if so, auto-skip
                    bool isValidation = ex.GetType().Name.Contains("Validation") ||
                                        ex.Message.Contains("bone") ||
                                        ex.Message.Contains("Bone") ||
                                        ex.Message.Contains("rig") ||
                                        ex.Message.Contains("humanoid") ||
                                        ex.Message.Contains("Chest") ||
                                        ex.Message.Contains("validation") ||
                                        ex.Message.Contains("Validation");

                    string shortMsg = ex.Message.Length > 120 ? ex.Message.Substring(0, 120) + "…" : ex.Message;
                    string logMsg   = $"[OutfitBatchUploader] '{outfit.Name}' failed: {ex}";

                    if (isValidation)
                    {
                        // Auto-skip validation errors and continue — log to console
                        skipped.Add($"{outfit.Name}  ({shortMsg})");
                        Debug.LogWarning(logMsg);
                        SetStatus($"⚠ Skipped {outfit.Name} — validation error (see Console)", MessageType.Warning);
                        Repaint();
                        await Task.Delay(800, CancellationToken.None);
                    }
                    else
                    {
                        // Unknown error — ask whether to continue
                        SetStatus($"Error on {outfit.Name}: {shortMsg}", MessageType.Error);
                        Debug.LogError(logMsg);

                        bool cont = EditorUtility.DisplayDialog(
                            "Upload Failed",
                            $"Upload failed for '{outfit.Name}':\n{shortMsg}\n\nContinue with remaining outfits?",
                            "Continue", "Stop");
                        if (!cont) break;
                    }
                }
            }

            // Final summary
            string summary = $"Batch complete — {succeeded}/{batch.Count} uploaded.";
            if (skipped.Count > 0)
                summary += $"\n\nSkipped ({skipped.Count}) due to validation errors:\n• " +
                           string.Join("\n• ", skipped) +
                           "\n\nFix the missing bones on those outfits and upload them separately.";

            // Restore blendshapes to the values they had before the batch started
            if (blendShapeSnapshot != null && _skinRenderer != null && _skinRenderer.sharedMesh != null)
            {
                Undo.RecordObject(_skinRenderer, "Restore blendshapes after batch upload");
                var mesh = _skinRenderer.sharedMesh;
                foreach (var kv in blendShapeSnapshot)
                {
                    int idx = mesh.GetBlendShapeIndex(kv.Key);
                    if (idx >= 0)
                        _skinRenderer.SetBlendShapeWeight(idx, kv.Value);
                }
                EditorUtility.SetDirty(_skinRenderer);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            SetStatus(summary, skipped.Count > 0 ? MessageType.Warning : MessageType.Info);
            _isBatchUploading = false;
            _batchIndex       = _batchTotal;
            Repaint();

            if (skipped.Count > 0)
                Debug.LogWarning($"[OutfitBatchUploader] {summary}");

            // Play confirm sound only when every planned outfit was uploaded successfully
            if (succeeded == batch.Count)
                PlayConfirmSound();
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
            public string                      PrefsKey         = "";   // scoped by avatar name
            // Blendshape overrides: name → value (0-100). Only entries present here are applied.
            public Dictionary<string, float>   BlendShapes      = new Dictionary<string, float>();
            public bool                        BlendShapeExpanded = false;
            public string                      BlendShapeSearch   = "";
        }
    }
}
