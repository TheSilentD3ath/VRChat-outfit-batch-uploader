// ============================================================
//  VRC Outfit Batch Uploader — New Outfit Setup module
//  (partial class — lives alongside OutfitBatchUploader.cs)
//
//  Adds an "Express / Advanced" one-click setup for outfits that
//  do not have a Blueprint ID yet:
//    1. Clears the PipelineManager blueprint ID so the VRChat SDK
//       treats the avatar in the scene as a brand-new avatar.
//    2. Captures / assigns a thumbnail (scene camera with a filled
//       background, or a fixed default image).
//    3. Applies a default description, release status and content
//       warning tags (with optional SPS/DPS auto-detection that
//       adds the "Sexually Suggestive" tag automatically).
//    4. Optionally accepts the auto-fixes the VRChat SDK proposes
//       in its build alerts (best-effort, see TryApplySdkAutoFixes).
//    5. Builds + uploads as a NEW avatar and writes the freshly
//       created Blueprint ID back into the tool automatically.
//
//  Defaults are configured in the "New Outfit Setup" section and
//  stored in EditorPrefs so they persist across sessions.
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
using UnityEngine.UIElements;
using VRC.Core;
using VRC.SDK3A.Editor;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor;
using VRC.SDKBase.Editor.Api;

namespace ShiroTools
{
    public partial class OutfitBatchUploader
    {
        // ---- The five VRChat content-warning tags (exact SDK strings) ----
        private static readonly string[] CONTENT_TAGS =
            { "content_sex", "content_adult", "content_violence", "content_gore", "content_horror" };

        private static string ContentTagLabel(string tag) => tag switch
        {
            "content_sex"      => "Sexually Suggestive",
            "content_adult"    => "Adult Language and Themes",
            "content_violence" => "Graphic Violence",
            "content_gore"     => "Excessive Gore",
            "content_horror"   => "Extreme Horror",
            _                  => tag
        };

        // ---- Defaults prefs keys ----
        private const string NS_DESC        = "ShiroNewOutfit_DescTemplate";
        private const string NS_NAME        = "ShiroNewOutfit_NameTemplate";
        private const string NS_RELEASE     = "ShiroNewOutfit_Release";          // "private" / "public"
        private const string NS_THUMB_MODE  = "ShiroNewOutfit_ThumbMode";        // "scene" / "image"
        private const string NS_THUMB_IMG   = "ShiroNewOutfit_ThumbImagePath";   // asset or absolute path
        private const string NS_BG_COLOR    = "ShiroNewOutfit_BgColor";          // html RGBA
        private const string NS_AUTO_SPS    = "ShiroNewOutfit_AutoDetectSps";
        private const string NS_AUTO_FIX    = "ShiroNewOutfit_AutoApplyFixes";
        private const string NS_AUTO_CONSENT = "ShiroNewOutfit_AutoConsent";
        private const string NS_TAG_PREFIX  = "ShiroNewOutfit_Tag_";             // + content tag

        // ---- Express resume (domain-reload-safe) ----
        private const string SESSION_EXPRESS_PENDING = "Shiro_Express_Pending";
        private const string SESSION_EXPRESS_OUTFIT  = "Shiro_Express_Outfit";
        private const string SESSION_EXPRESS_THUMB   = "Shiro_Express_Thumb";
        private const string SESSION_EXPRESS_DESC    = "Shiro_Express_Desc";
        private const string SESSION_EXPRESS_NAME    = "Shiro_Express_Name";
        private const string SESSION_EXPRESS_TAGS    = "Shiro_Express_Tags";
        private const string SESSION_EXPRESS_RELEASE = "Shiro_Express_Release";

        // ---- Runtime defaults (loaded lazily) ----
        private bool   _nsLoaded;
        private string _nsDescTemplate;
        private string _nsNameTemplate;
        private string _nsRelease;
        private string _nsThumbMode;
        private string _nsThumbImagePath;
        private Color  _nsBgColor = new Color(0.10f, 0.10f, 0.12f, 1f);
        private bool   _nsAutoSps;
        private bool   _nsAutoFix;
        private bool   _nsAutoConsent;
        private readonly Dictionary<string, bool> _nsTagDefaults = new Dictionary<string, bool>();

        // ---- UI state ----
        private bool _nsSectionExpanded;
        private Vector2 _nsDefaultsScroll;
        private string _nsAdvancedOutfit;           // which outfit's advanced panel is open
        private readonly Dictionary<string, AdvancedDraft> _nsDrafts = new Dictionary<string, AdvancedDraft>();

        internal class AdvancedDraft
        {
            public string Name;
            public string Description;
            public string Release = "private";
            public string ThumbMode = "scene";   // "scene" (auto-framed) / "sceneview" (current view) / "image"
            public string ImagePath = "";
            public readonly Dictionary<string, bool> Tags = new Dictionary<string, bool>();
        }

        // ============================================================
        //  Defaults persistence
        // ============================================================
        private void LoadNewSetupDefaults()
        {
            if (_nsLoaded) return;
            _nsLoaded = true;

            _nsDescTemplate   = EditorPrefs.GetString(NS_DESC, "");
            _nsNameTemplate   = EditorPrefs.GetString(NS_NAME, "{outfit}");
            _nsRelease        = EditorPrefs.GetString(NS_RELEASE, "private");
            _nsThumbMode      = EditorPrefs.GetString(NS_THUMB_MODE, "scene");
            _nsThumbImagePath = EditorPrefs.GetString(NS_THUMB_IMG, "");
            _nsAutoSps        = EditorPrefs.GetBool(NS_AUTO_SPS, true);
            _nsAutoFix        = EditorPrefs.GetBool(NS_AUTO_FIX, true);
            _nsAutoConsent    = EditorPrefs.GetBool(NS_AUTO_CONSENT, true);

            string col = EditorPrefs.GetString(NS_BG_COLOR, "1A1A1FFF");
            if (ColorUtility.TryParseHtmlString("#" + col, out var parsed)) _nsBgColor = parsed;

            _nsTagDefaults.Clear();
            foreach (var tag in CONTENT_TAGS)
                _nsTagDefaults[tag] = EditorPrefs.GetBool(NS_TAG_PREFIX + tag, false);
        }

        private void SaveNewSetupDefaults()
        {
            EditorPrefs.SetString(NS_DESC, _nsDescTemplate ?? "");
            EditorPrefs.SetString(NS_NAME, string.IsNullOrWhiteSpace(_nsNameTemplate) ? "{outfit}" : _nsNameTemplate);
            EditorPrefs.SetString(NS_RELEASE, _nsRelease);
            EditorPrefs.SetString(NS_THUMB_MODE, _nsThumbMode);
            EditorPrefs.SetString(NS_THUMB_IMG, _nsThumbImagePath ?? "");
            EditorPrefs.SetBool(NS_AUTO_SPS, _nsAutoSps);
            EditorPrefs.SetBool(NS_AUTO_FIX, _nsAutoFix);
            EditorPrefs.SetBool(NS_AUTO_CONSENT, _nsAutoConsent);
            EditorPrefs.SetString(NS_BG_COLOR, ColorUtility.ToHtmlStringRGBA(_nsBgColor));
            foreach (var tag in CONTENT_TAGS)
                EditorPrefs.SetBool(NS_TAG_PREFIX + tag, _nsTagDefaults.TryGetValue(tag, out var b) && b);
        }

        // ============================================================
        //  GUI — drawn from OnGUI via DrawNewOutfitSetupSection()
        // ============================================================
        private void DrawNewOutfitSetupSection(bool standalone = false)
        {
            LoadNewSetupDefaults();

            int newCount = _outfits.Count(o => o.Go != null && string.IsNullOrWhiteSpace(o.BlueprintId));

            if (!standalone)
            {
                _nsSectionExpanded = EditorGUILayout.Foldout(
                    _nsSectionExpanded,
                    newCount > 0
                        ? $"New Outfit Defaults  ({newCount} outfit(s) need setup — use ⚡/⚙ on each above)"
                        : "New Outfit Defaults",
                    true, EditorStyles.foldoutHeader);

                if (!_nsSectionExpanded) return;
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("New Outfit Defaults", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (newCount > 0)
                        EditorGUILayout.LabelField($"{newCount} need setup", EditorStyles.miniLabel, GUILayout.Width(92));
                }
            }

            // Keep the complete defaults page usable in short editor windows. Individual
            // nested lists retain their own scroll views; wheel routing is registered so
            // the main outfit list's momentum scrolling does not steal this section's input.
            float scrollHeight = Mathf.Clamp(position.height - 220f, 180f, 520f);
            _nsDefaultsScroll = standalone
                ? EditorGUILayout.BeginScrollView(_nsDefaultsScroll, GUILayout.ExpandHeight(true))
                : EditorGUILayout.BeginScrollView(_nsDefaultsScroll, GUILayout.Height(scrollHeight));
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox(
                    "These defaults are applied by the ⚡ Express button shown on each outfit that has no Blueprint ID yet. " +
                    "Use ⚙ Advanced on an outfit to override them per upload.",
                    MessageType.Info);
                DrawDefaultsConfig();
                DrawTextureOptDefaults();
                DrawItemDefaultsConfig();

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Backup", EditorStyles.miniBoldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Export settings…", EditorStyles.miniButton))
                        ExportAllSettings();
                    if (GUILayout.Button("Import settings…", EditorStyles.miniButton))
                        ImportAllSettings();
                }
                EditorGUILayout.LabelField(
                    "Exports/imports all per-avatar data (Blueprint IDs, blendshapes, items, FaceEmo, versions) " +
                    "as one JSON file — for backups or moving to another project.",
                    EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndScrollView();
            RegisterNestedScrollRect();
        }

        private void DrawDefaultsConfig()
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Avatar name template", EditorStyles.miniBoldLabel);
            _nsNameTemplate = EditorGUILayout.TextField(_nsNameTemplate);
            EditorGUILayout.LabelField("Tokens: {outfit}, {avatar}", EditorStyles.miniLabel);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Description template", EditorStyles.miniBoldLabel);
            _nsDescTemplate = EditorGUILayout.TextArea(_nsDescTemplate ?? "", GUILayout.MinHeight(40));

            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Release status", GUILayout.Width(110));
                int relIdx = _nsRelease == "public" ? 1 : 0;
                relIdx = EditorGUILayout.Popup(relIdx, new[] { "private", "public" });
                _nsRelease = relIdx == 1 ? "public" : "private";
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Default content warnings", EditorStyles.miniBoldLabel);
            foreach (var tag in CONTENT_TAGS)
            {
                bool cur = _nsTagDefaults.TryGetValue(tag, out var b) && b;
                _nsTagDefaults[tag] = EditorGUILayout.ToggleLeft(ContentTagLabel(tag), cur);
            }
            _nsAutoSps = EditorGUILayout.ToggleLeft(
                "Auto-detect SPS/DPS → add \"Sexually Suggestive\" automatically", _nsAutoSps);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Thumbnail", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Source", GUILayout.Width(110));
                int tIdx = _nsThumbMode == "image" ? 2 : (_nsThumbMode == "sceneview" ? 1 : 0);
                tIdx = EditorGUILayout.Popup(tIdx, new[]
                {
                    "Standard view (auto-framed front shot)",
                    "Scene view camera (exactly what you see)",
                    "Default image"
                });
                _nsThumbMode = tIdx == 2 ? "image" : (tIdx == 1 ? "sceneview" : "scene");
            }
            if (_nsThumbMode != "image")
            {
                if (_nsThumbMode == "sceneview")
                    EditorGUILayout.LabelField(
                        "Uses your current Scene view angle & position. Arrange the view first, then check with Preview. " +
                        "The scene background is replaced by the fill color below.",
                        EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Background", GUILayout.Width(110));
                    _nsBgColor = EditorGUILayout.ColorField(_nsBgColor);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(114);
                    using (new EditorGUI.DisabledScope(_avatarRoot == null))
                    {
                        if (GUILayout.Button("👁 Preview thumbnail", EditorStyles.miniButton, GUILayout.Width(140)))
                        {
                            string p = CaptureSceneThumbnail(_nsThumbMode == "sceneview");
                            if (p != null) ThumbPreviewWindow.Open(p);
                            else SetStatus("Thumbnail preview failed — see Console.", MessageType.Error);
                        }
                    }
                }
            }
            else
            {
                Texture2D tex = null;
                if (!string.IsNullOrEmpty(_nsThumbImagePath) && _nsThumbImagePath.StartsWith("Assets"))
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(_nsThumbImagePath);

                var picked = (Texture2D)EditorGUILayout.ObjectField("Image", tex, typeof(Texture2D), false);
                if (picked != null)
                {
                    string p = AssetDatabase.GetAssetPath(picked);
                    if (!string.IsNullOrEmpty(p)) _nsThumbImagePath = p;
                }
                EditorGUILayout.LabelField(string.IsNullOrEmpty(_nsThumbImagePath) ? "(none)" : _nsThumbImagePath,
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(4);
            _nsAutoFix = EditorGUILayout.ToggleLeft(
                "Auto-accept the fixes the VRChat SDK proposes in its alerts (best-effort)", _nsAutoFix);
            _nsAutoConsent = EditorGUILayout.ToggleLeft(
                "Auto-confirm the SDK's copyright/ownership dialog (you still confirm once in this tool)", _nsAutoConsent);

            if (EditorGUI.EndChangeCheck())
                SaveNewSetupDefaults();
        }

        /// <summary>Drawn inside the normal outfit row (from DrawOutfitRow) when the outfit has no
        /// Blueprint ID yet — gives the Express / Advanced first-time-setup buttons right there.</summary>
        private void DrawInlineNewOutfitButtons(OutfitEntry entry)
        {
            LoadNewSetupDefaults();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("First-time setup:", GUILayout.Width(96));

                using (new EditorGUI.DisabledScope(_isBatchUploading || _isExpressBusy))
                {
                    var old = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.4f, 0.75f, 1f);
                    if (GUILayout.Button("⚡ Express setup", GUILayout.Height(22)))
                        _ = ExpressSetupAsync(entry);
                    GUI.backgroundColor = old;

                    bool advOpen = _nsAdvancedOutfit == entry.Name;
                    if (GUILayout.Button(advOpen ? "⚙ Advanced ▲" : "⚙ Advanced ▼", GUILayout.Width(110), GUILayout.Height(22)))
                    {
                        _nsAdvancedOutfit = advOpen ? null : entry.Name;
                        if (_nsAdvancedOutfit == entry.Name) EnsureDraft(entry);
                    }
                }
            }

            if (_nsAdvancedOutfit == entry.Name)
                DrawAdvancedPanel(entry);
        }

        private void EnsureDraft(OutfitEntry entry)
        {
            if (_nsDrafts.ContainsKey(entry.Name)) return;
            var d = new AdvancedDraft
            {
                Name        = ApplyTokens(_nsNameTemplate, entry),
                Description = ApplyTokens(_nsDescTemplate, entry),
                Release     = _nsRelease,
                ThumbMode   = _nsThumbMode,
                ImagePath   = _nsThumbImagePath
            };
            foreach (var tag in CONTENT_TAGS)
                d.Tags[tag] = _nsTagDefaults.TryGetValue(tag, out var b) && b;
            if (_nsAutoSps && DetectSpsOrDps(entry)) d.Tags["content_sex"] = true;
            _nsDrafts[entry.Name] = d;
        }

        private void DrawAdvancedPanel(OutfitEntry entry)
        {
            if (!_nsDrafts.TryGetValue(entry.Name, out var d)) { EnsureDraft(entry); d = _nsDrafts[entry.Name]; }

            EditorGUILayout.Space(2);
            d.Name        = EditorGUILayout.TextField("Avatar name", d.Name);
            EditorGUILayout.LabelField("Description", EditorStyles.miniBoldLabel);
            d.Description = EditorGUILayout.TextArea(d.Description ?? "", GUILayout.MinHeight(36));

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Release status", GUILayout.Width(110));
                int relIdx = d.Release == "public" ? 1 : 0;
                relIdx = EditorGUILayout.Popup(relIdx, new[] { "private", "public" });
                d.Release = relIdx == 1 ? "public" : "private";
            }

            EditorGUILayout.LabelField("Content warnings", EditorStyles.miniBoldLabel);
            foreach (var tag in CONTENT_TAGS)
                d.Tags[tag] = EditorGUILayout.ToggleLeft(ContentTagLabel(tag),
                    d.Tags.TryGetValue(tag, out var b) && b);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Thumbnail", GUILayout.Width(110));
                int tIdx = d.ThumbMode == "image" ? 2 : (d.ThumbMode == "sceneview" ? 1 : 0);
                tIdx = EditorGUILayout.Popup(tIdx, new[]
                {
                    "Standard view (auto-framed front shot)",
                    "Scene view camera (exactly what you see)",
                    "Default image"
                });
                d.ThumbMode = tIdx == 2 ? "image" : (tIdx == 1 ? "sceneview" : "scene");
            }
            if (d.ThumbMode == "image")
            {
                Texture2D tex = (!string.IsNullOrEmpty(d.ImagePath) && d.ImagePath.StartsWith("Assets"))
                    ? AssetDatabase.LoadAssetAtPath<Texture2D>(d.ImagePath) : null;
                var picked = (Texture2D)EditorGUILayout.ObjectField("Image", tex, typeof(Texture2D), false);
                if (picked != null)
                {
                    string p = AssetDatabase.GetAssetPath(picked);
                    if (!string.IsNullOrEmpty(p)) d.ImagePath = p;
                }
            }
            else if (GUILayout.Button("👁 Preview thumbnail", EditorStyles.miniButton, GUILayout.Width(140)))
            {
                string p = CaptureSceneThumbnail(d.ThumbMode == "sceneview");
                if (p != null) ThumbPreviewWindow.Open(p);
            }

            EditorGUILayout.Space(2);
            using (new EditorGUI.DisabledScope(_isBatchUploading || _isExpressBusy))
            {
                if (GUILayout.Button("Create & Upload as new avatar", GUILayout.Height(26)))
                    _ = ExpressSetupAsync(entry, d);
            }
        }

        // ============================================================
        //  Express / new-avatar creation flow
        // ============================================================
        private bool _isExpressBusy;
        private bool _expressQuietMode;   // true while the Upload-All gate runs several Express setups (one sound at the end instead of one per outfit)

        private async Task ExpressSetupAsync(OutfitEntry entry, AdvancedDraft draft = null, bool skipConfirm = false)
        {
            _expressQuietMode = skipConfirm;
            if (_avatarRoot == null) { SetStatus("No avatar root selected.", MessageType.Error); return; }
            if (_outfitsParent == null) { SetStatus("No outfits parent found.", MessageType.Error); return; }

            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out var builder))
            {
                SetStatus("VRC SDK builder not available — open the VRChat SDK window first.", MessageType.Error);
                return;
            }
            if (!APIUser.IsLoggedIn)
            {
                SetStatus("Not logged in. Open the VRChat SDK Control Panel and log in first.", MessageType.Error);
                return;
            }

            // Activate the target outfit FIRST (this outfit → Untagged, all others → EditorOnly)
            // so SPS/DPS auto-detection and tags reflect exactly what will be uploaded.
            ActivateOutfit(entry);

            // Resolve the values to use (Advanced draft overrides defaults)
            string name = draft?.Name ?? ApplyTokens(_nsNameTemplate, entry);
            if (string.IsNullOrWhiteSpace(name)) name = entry.Name;
            string desc = draft?.Description ?? ApplyTokens(_nsDescTemplate, entry);
            string release = draft?.Release ?? _nsRelease;
            var tags = ComputeContentTags(entry, draft);

            // One ownership confirmation (skipped when the batch setup-gate already confirmed)
            bool confirmed = skipConfirm || EditorUtility.DisplayDialog(
                "Create new avatar",
                $"This will register \"{entry.Name}\" as a BRAND-NEW VRChat avatar:\n\n" +
                $"• Name: {name}\n" +
                $"• Release: {release}\n" +
                $"• Tags: {(tags.Count == 0 ? "none" : string.Join(", ", tags.Select(ContentTagLabel)))}\n\n" +
                "Do you confirm that all content belongs to you and you have the rights to upload it?",
                "Yes, create it", "Cancel");
            if (!confirmed) { SetStatus("New-avatar setup cancelled.", MessageType.Warning); return; }

            _isExpressBusy = true;
            SetStatus($"Setting up new avatar for '{entry.Name}'…", MessageType.Info);
            Repaint();

            try
            {
                // (Outfit already activated above so detection/tags matched the upload.)

                // 1b) Optionally optimize this outfit's textures (VRAM) before uploading
                MaybeOptimizeDuringExpress(entry);

                // 2) Resolve a thumbnail before clearing the shared PipelineManager state.
                string thumbPath = ResolveThumbnailPath(entry, draft);
                if (string.IsNullOrEmpty(thumbPath))
                {
                    SetStatus("Could not produce a thumbnail — aborting.", MessageType.Error);
                    _isExpressBusy = false;
                    return;
                }

                // 3) Persist the complete resume record BEFORE any operation that may trigger
                // a domain reload (scene save, asset refresh or SDK auto-fix).
                SessionState.SetBool(SESSION_EXPRESS_PENDING, true);
                SessionState.SetString(SESSION_EXPRESS_OUTFIT, entry.Name);
                SessionState.SetString(SESSION_EXPRESS_THUMB, thumbPath);
                SessionState.SetString(SESSION_EXPRESS_DESC, desc ?? "");
                SessionState.SetString(SESSION_EXPRESS_NAME, name);
                SessionState.SetString(SESSION_EXPRESS_TAGS, string.Join(";", tags));
                SessionState.SetString(SESSION_EXPRESS_RELEASE, release);

                // 4) Clear the Blueprint ID so the SDK creates a NEW avatar.
                ClearBlueprintId();

                // 5) Save the scene so the SDK sees the changes.
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                AssetDatabase.SaveAssets();

                // 6) Optionally accept SDK auto-fixes (best-effort).
                if (_nsAutoFix)
                {
                    int fixes = TryApplySdkAutoFixes(builder);
                    if (fixes > 0)
                    {
                        SetStatus($"Applied {fixes} SDK auto-fix(es). Refreshing…", MessageType.Info);
                        Repaint();
                        AssetDatabase.Refresh();
                        await Task.Delay(800);
                    }
                }

                await ContinueExpressUploadAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError("[OutfitBatchUploader] Express setup failed: " + ex);
                SetStatus("Express setup failed: " + Truncate(ex.Message, 140), MessageType.Error);
                CleanupTempThumb(SessionState.GetString(SESSION_EXPRESS_THUMB, ""));
                ClearExpressState();
                _isExpressBusy = false;
                Repaint();
            }
        }

        /// <summary>Performs the actual new-avatar build+upload from the resume record,
        /// then writes the freshly created Blueprint ID back into the tool.</summary>
        private async Task ContinueExpressUploadAsync()
        {
            if (!SessionState.GetBool(SESSION_EXPRESS_PENDING, false)) { _isExpressBusy = false; return; }

            // Make sure defaults (incl. _nsAutoConsent) are loaded — after a domain reload
            // this runs BEFORE the GUI has drawn once, so they'd otherwise still be unset.
            LoadNewSetupDefaults();

            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out var builder))
            {
                SetStatus("VRC SDK builder not available.", MessageType.Error); _isExpressBusy = false; return;
            }

            string outfitName = SessionState.GetString(SESSION_EXPRESS_OUTFIT, "");
            string thumbPath  = SessionState.GetString(SESSION_EXPRESS_THUMB, "");
            string desc       = SessionState.GetString(SESSION_EXPRESS_DESC, "");
            string name       = SessionState.GetString(SESSION_EXPRESS_NAME, outfitName);
            string release    = SessionState.GetString(SESSION_EXPRESS_RELEASE, "private");
            var tags = SessionState.GetString(SESSION_EXPRESS_TAGS, "")
                        .Split(';').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

            var pm = _avatarRoot != null ? _avatarRoot.GetComponentInChildren<PipelineManager>() : null;
            if (pm == null)
            {
                SetStatus("No PipelineManager on the avatar.", MessageType.Error);
                ClearExpressState(); _isExpressBusy = false; return;
            }

            _isExpressBusy = true;
            SetStatus($"Uploading new avatar '{name}'…", MessageType.Info);
            Repaint();

            var newAvatar = new VRCAvatar
            {
                Name          = name,
                Description   = desc ?? "",
                Tags          = tags,
                ReleaseStatus = release
            };

            // Auto-confirm the SDK's copyright/ownership modal that pops up for a brand-new
            // avatar (its content ID is only reserved mid-upload, so it can't be pre-agreed).
            if (_nsAutoConsent) StartConsentWatcher();

            try
            {
                await builder.BuildAndUpload(_avatarRoot, newAvatar, thumbPath);

                // The SDK writes the new ID onto the PipelineManager after a successful create.
                // On some SDK versions that happens a moment AFTER BuildAndUpload returns,
                // so poll for up to ~5 seconds (and re-resolve the component each try).
                string newId = null;
                for (int i = 0; i < 25 && string.IsNullOrWhiteSpace(newId); i++)
                {
                    var pmNow = _avatarRoot != null ? _avatarRoot.GetComponentInChildren<PipelineManager>() : pm;
                    newId = pmNow != null ? pmNow.blueprintId : null;
                    if (string.IsNullOrWhiteSpace(newId)) await Task.Delay(200);
                }

                if (!string.IsNullOrWhiteSpace(newId))
                {
                    // Persist straight into the project store — works even if the outfit list
                    // was rebuilt in the meantime and the UI entry object is stale/gone.
                    var entry = _outfits.FirstOrDefault(o => o.Name == outfitName);
                    var data = entry?.Data ?? (_avatarRoot != null
                        ? OutfitProjectData.GetOutfit(_avatarRoot.name, outfitName)
                        : null);
                    if (entry != null) entry.BlueprintId = newId;
                    if (data != null)
                    {
                        data.blueprintId = newId;
                        OutfitProjectData.MarkUploaded(data, GetCurrentPlatform().ToString());
                    }
                    LogUpload($"OK    {outfitName} (Express new avatar) → {newId}");
                    SetStatus($"✓ Created new avatar for '{outfitName}'  →  {newId}", MessageType.Info);
                    if (_soundEnabled && !_expressQuietMode) PlayConfirmSound();
                }
                else
                {
                    LogUpload($"WARN  {outfitName} (Express new avatar): upload finished but no Blueprint ID appeared on the PipelineManager.");
                    SetStatus($"Upload finished but no new Blueprint ID was detected. " +
                              "Use the ▾ picker (☁ fetches your avatars) or paste the ID manually.", MessageType.Warning);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[OutfitBatchUploader] New-avatar upload failed: " + ex);
                LogUpload($"FAIL  {outfitName} (Express new avatar): {ex.Message}");
                SetStatus("New-avatar upload failed: " + Truncate(ex.Message, 140), MessageType.Error);
            }
            finally
            {
                StopConsentWatcher();
                ClearExpressState();
                _isExpressBusy = false;
                CleanupTempThumb(thumbPath);
                Repaint();
            }
        }

        // ============================================================
        //  Auto-confirm the SDK copyright/ownership modal
        // ============================================================
        private bool _consentWatcherActive;
        private double _consentWatcherStarted;

        private void StartConsentWatcher()
        {
            if (_consentWatcherActive) return;
            _consentWatcherActive = true;
            _consentWatcherStarted = EditorApplication.timeSinceStartup;
            EditorApplication.update += ConsentWatcherTick;
        }

        private void StopConsentWatcher()
        {
            if (!_consentWatcherActive) return;
            _consentWatcherActive = false;
            EditorApplication.update -= ConsentWatcherTick;
        }

        private void ConsentWatcherTick()
        {
            // Safety timeout so the watcher never runs forever (e.g. 5 min)
            if (EditorApplication.timeSinceStartup - _consentWatcherStarted > 300.0)
            {
                StopConsentWatcher();
                return;
            }
            try { TryAutoConfirmCopyrightModal(); }
            catch (Exception ex)
            {
                Debug.LogWarning("[OutfitBatchUploader] Auto-consent tick failed: " + ex.Message);
            }
        }

        /// <summary>Finds a visible "copyright / ownership" modal in the SDK panel and clicks its OK button.
        /// Best-effort: relies on SDK UI internals, wrapped so failure is harmless (you click OK yourself).</summary>
        private void TryAutoConfirmCopyrightModal()
        {
            var panel = VRCSdkControlPanel.window;
            if (panel == null) return;
            var root = panel.rootVisualElement;
            if (root == null) return;

            foreach (var btn in root.Query<Button>("modal-action-button").ToList())
            {
                if (btn == null || btn.panel == null) continue;
                if (btn.resolvedStyle.display == DisplayStyle.None) continue;

                // Walk up to the owning Modal element
                VisualElement modal = btn;
                while (modal != null && modal.GetType().Name != "Modal") modal = modal.parent;
                if (modal == null) continue;
                if (modal.ClassListContains("d-none")) continue;     // hidden modal

                if (!ModalLooksLikeCopyright(modal)) continue;

                if (InvokeButtonClick(btn))
                    Debug.Log("[OutfitBatchUploader] Auto-confirmed the SDK copyright/ownership dialog.");
            }
        }

        private static bool ModalLooksLikeCopyright(VisualElement modal)
        {
            foreach (var label in modal.Query<Label>().ToList())
            {
                string txt = (label.text ?? "").ToLowerInvariant();
                if (txt.Contains("necessary rights") || txt.Contains("copyright") ||
                    txt.Contains("ownership") || txt.Contains("intellectual property"))
                    return true;
            }
            return false;
        }

        /// <summary>Invokes a UIElements Button's click handlers via its Clickable manipulator.</summary>
        private static bool InvokeButtonClick(Button btn)
        {
            try
            {
                var clickableProp = typeof(Button).GetProperty("clickable",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var clickable = clickableProp?.GetValue(btn);
                if (clickable == null) return false;

                // Clickable.clicked is a field-like event → backing delegate field named "clicked"
                var clickedField = clickable.GetType().GetField("clicked",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (clickedField?.GetValue(clickable) is Action clicked)
                {
                    clicked.Invoke();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[OutfitBatchUploader] Could not invoke modal OK button: " + ex.Message);
            }
            return false;
        }

        private void ClearExpressState()
        {
            SessionState.EraseBool(SESSION_EXPRESS_PENDING);
            SessionState.EraseString(SESSION_EXPRESS_OUTFIT);
            SessionState.EraseString(SESSION_EXPRESS_THUMB);
            SessionState.EraseString(SESSION_EXPRESS_DESC);
            SessionState.EraseString(SESSION_EXPRESS_NAME);
            SessionState.EraseString(SESSION_EXPRESS_TAGS);
            SessionState.EraseString(SESSION_EXPRESS_RELEASE);
        }

        /// <summary>Called from OnEnable (in the main file) to resume a new-avatar upload
        /// if a domain reload interrupted the flow.</summary>
        private void TryResumeExpress()
        {
            if (!SessionState.GetBool(SESSION_EXPRESS_PENDING, false)) return;
            if (SessionState.GetBool(SESSION_BATCH_ACTIVE, false)) return; // batch takes priority
            _isExpressBusy = true;
            EditorApplication.update += HandleResumeExpress;
        }

        private void HandleResumeExpress()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out _)) return;
            if (!APIUser.IsLoggedIn) { SetStatus("Waiting for VRChat SDK login…", MessageType.Info); Repaint(); return; }

            EditorApplication.update -= HandleResumeExpress;
            _ = ContinueExpressUploadAsync();
        }

        // ============================================================
        //  Helpers
        // ============================================================
        private void ClearBlueprintId()
        {
            var pm = _avatarRoot.GetComponentInChildren<PipelineManager>();
            if (pm == null) return;
            Undo.RecordObject(pm, "Clear Blueprint ID for new avatar");
            pm.blueprintId = "";
            // Best-effort: reset the "completed setup" flag if it exists on this SDK version
            var f = pm.GetType().GetField("completedSDK3Setup");
            if (f != null && f.FieldType == typeof(bool)) f.SetValue(pm, false);
            EditorUtility.SetDirty(pm);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private List<string> ComputeContentTags(OutfitEntry entry, AdvancedDraft draft)
        {
            var tags = new List<string>();
            foreach (var tag in CONTENT_TAGS)
            {
                bool on = draft != null
                    ? (draft.Tags.TryGetValue(tag, out var b) && b)
                    : (_nsTagDefaults.TryGetValue(tag, out var d) && d);
                if (on) tags.Add(tag);
            }

            if (draft == null && _nsAutoSps && DetectSpsOrDps(entry) && !tags.Contains("content_sex"))
                tags.Add("content_sex");

            return tags.Distinct().ToList();
        }

        private string ApplyTokens(string template, OutfitEntry entry)
        {
            if (string.IsNullOrEmpty(template)) return "";
            string avatarName = _avatarRoot != null ? _avatarRoot.name : "";
            return template.Replace("{outfit}", entry.Name).Replace("{avatar}", avatarName);
        }

        private static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) ? s : (s.Length > n ? s.Substring(0, n) + "…" : s);

        /// <summary>Detects VRChat SPS (VRCFury Haptic Plug/Socket) or DPS markers that will actually
        /// be uploaded for this outfit — i.e. not on an "EditorOnly" (do-not-upload) subtree and not
        /// inside a different outfit. Call AFTER the target outfit has been activated.</summary>
        private bool DetectSpsOrDps(OutfitEntry target)
        {
            if (_avatarRoot == null) return false;

            // SPS — VRCFury haptic components (internal types, matched by name)
            foreach (var comp in _avatarRoot.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                string tn = comp.GetType().Name;
                if (tn == "VRCFuryHapticSocket" || tn == "VRCFuryHapticPlug" || tn == "VRCFuryHapticTouchReceiver")
                    if (IsUploadRelevant(comp.transform, target))
                    {
                        Debug.Log($"[OutfitBatchUploader] SPS auto-detection: {tn} on '{comp.gameObject.name}' → adding \"Sexually Suggestive\" tag.");
                        return true;
                    }
            }

            // DPS — name heuristic on GameObjects. "dps" only matches as its own word
            // (so e.g. "HandPSprite" or "SoundPS" can't trigger a false positive).
            foreach (var t in _avatarRoot.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                bool hit = n.Contains("penetrator") || n.Contains("orifice") ||
                           System.Text.RegularExpressions.Regex.IsMatch(n, @"(?<![a-z0-9])dps(?![a-z0-9])");
                if (hit && IsUploadRelevant(t, target))
                {
                    Debug.Log($"[OutfitBatchUploader] DPS auto-detection: object name '{t.name}' → adding \"Sexually Suggestive\" tag.");
                    return true;
                }
            }
            return false;
        }

        /// <summary>True if this transform is part of what gets uploaded for the target outfit:
        /// not under an EditorOnly tag, and not inside a different outfit.</summary>
        private bool IsUploadRelevant(Transform t, OutfitEntry target)
        {
            if (_outfitsParent != null)
            {
                var owner = DirectChildUnder(_outfitsParent.transform, t);
                if (owner != null)
                    return owner.gameObject == target?.Go && !IsUnderEditorOnlyBelowOwner(t, owner);
            }

            EnsureItemsBuilt();
            if (_itemsParent != null)
            {
                var owner = DirectChildUnder(_itemsParent.transform, t);
                if (owner != null)
                    return target != null && ItemIncludedFor(target.Name, owner.name) &&
                           !IsUnderEditorOnlyBelowOwner(t, owner);
            }

            return !IsUnderEditorOnly(t);
        }

        /// <summary>True if the transform or any ancestor (up to the avatar root) is tagged "EditorOnly".</summary>
        private bool IsUnderEditorOnly(Transform t)
        {
            Transform cur = t;
            Transform rootT = _avatarRoot != null ? _avatarRoot.transform : null;
            while (cur != null)
            {
                if (cur.CompareTag("EditorOnly")) return true;
                if (cur == rootT) break;
                cur = cur.parent;
            }
            return false;
        }

        /// <summary>True if the transform lives inside a sibling outfit (a direct child of the
        /// outfits parent) that is NOT the target outfit. Shared/body objects return false.</summary>
        private bool BelongsToOtherOutfit(Transform t, OutfitEntry target)
        {
            if (_outfitsParent == null || target?.Go == null) return false;
            Transform parentT = _outfitsParent.transform;

            Transform cur = t;
            while (cur != null && cur.parent != parentT)
                cur = cur.parent;

            if (cur == null) return false;          // not under the outfits parent → shared, keep
            return cur.gameObject != target.Go;     // under a different outfit's root
        }

        // ---- Thumbnail ----
        private string ResolveThumbnailPath(OutfitEntry entry, AdvancedDraft draft)
        {
            string mode = draft != null ? draft.ThumbMode : _nsThumbMode;
            string imgPath = draft != null ? draft.ImagePath : _nsThumbImagePath;

            if (mode == "image" && !string.IsNullOrEmpty(imgPath))
            {
                try
                {
                    string abs = imgPath.StartsWith("Assets")
                        ? Path.GetFullPath(imgPath)
                        : imgPath;
                    if (File.Exists(abs)) return abs;
                    Debug.LogWarning("[OutfitBatchUploader] Default thumbnail image not found, falling back to scene capture: " + abs);
                }
                catch (Exception ex) { Debug.LogWarning("[OutfitBatchUploader] Thumbnail path error: " + ex.Message); }
            }

            return CaptureSceneThumbnail(mode == "sceneview");
        }

        /// <summary>Renders the avatar with a temporary camera against a solid (filled) background
        /// and writes a 1200x900 PNG to a temp file. Returns the absolute path or null.
        /// With <paramref name="useSceneView"/> the current Scene view's camera angle/position/FOV
        /// is used (exactly what you see); otherwise a standard auto-framed front shot.</summary>
        private string CaptureSceneThumbnail(bool useSceneView = false)
        {
            const int W = 1200, H = 900;
            GameObject camGo = null;
            RenderTexture rt = null;
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                camGo = new GameObject("~ShiroThumbCam") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = _nsBgColor;           // fills the background
                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 100f;

                if (useSceneView)
                {
                    var sv = SceneView.lastActiveSceneView;
                    if (sv != null && sv.camera != null)
                    {
                        cam.transform.position = sv.camera.transform.position;
                        cam.transform.rotation = sv.camera.transform.rotation;
                        cam.fieldOfView        = sv.camera.fieldOfView;
                        cam.orthographic       = sv.camera.orthographic;
                        cam.orthographicSize   = sv.camera.orthographicSize;
                    }
                    else
                    {
                        Debug.LogWarning("[OutfitBatchUploader] No Scene view open — falling back to the standard auto-framed thumbnail.");
                        useSceneView = false;
                    }
                }

                if (!useSceneView)
                {
                    // Frame the avatar from the renderers' combined bounds
                    Bounds b = ComputeRenderBounds(_avatarRoot);
                    Vector3 center = b.center;
                    float radius = Mathf.Max(0.25f, b.extents.magnitude);
                    float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    // Slightly above center, looking at the upper body
                    Vector3 dir = new Vector3(0f, 0.15f, 1f).normalized;
                    cam.transform.position = center + dir * dist * 1.05f + Vector3.up * radius * 0.15f;
                    cam.transform.LookAt(center + Vector3.up * radius * 0.15f);
                }

                rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();

                byte[] png = tex.EncodeToPNG();
                DestroyImmediate(tex);

                string path = Path.Combine(Path.GetTempPath(),
                    "shiro_thumb_" + DateTime.Now.Ticks + ".png");
                File.WriteAllBytes(path, png);
                return path;
            }
            catch (Exception ex)
            {
                Debug.LogError("[OutfitBatchUploader] Thumbnail capture failed: " + ex);
                return null;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (rt != null) { rt.Release(); DestroyImmediate(rt); }
                if (camGo != null) DestroyImmediate(camGo);
            }
        }

        private static Bounds ComputeRenderBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(false)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (renderers.Length == 0)
                return new Bounds(root.transform.position + Vector3.up, Vector3.one);
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        private static void CleanupTempThumb(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                string fullPath = Path.GetFullPath(path);
                string tempRoot = Path.GetFullPath(Path.GetTempPath())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string parent = Path.GetDirectoryName(fullPath)?.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string fileName = Path.GetFileName(fullPath);
                if (string.Equals(parent, tempRoot, StringComparison.OrdinalIgnoreCase) &&
                    fileName.StartsWith("shiro_thumb_", StringComparison.Ordinal) &&
                    fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    File.Delete(fullPath);
            }
            catch { /* ignore */ }
        }

        /// <summary>Small window that shows exactly what the captured VRChat thumbnail will look like.</summary>
        internal class ThumbPreviewWindow : EditorWindow
        {
            private Texture2D _tex;
            private string _path;
            private Action _onUpload;   // optional: shown as an "Upload" button (used by the per-outfit Thumb update)

            internal static void Open(string pngPath, Action onUpload = null)
            {
                try
                {
                    var bytes = File.ReadAllBytes(pngPath);
                    var tex = new Texture2D(2, 2);
                    if (!tex.LoadImage(bytes)) { DestroyImmediate(tex); return; }

                    var w = GetWindow<ThumbPreviewWindow>(true, "Thumbnail preview");
                    if (w._tex != null) DestroyImmediate(w._tex);
                    CleanupTempThumb(w._path);
                    w._tex = tex;
                    w._path = pngPath;
                    w._onUpload = onUpload;
                    w.minSize = new Vector2(430, 400);
                    w.Show();
                }
                catch (Exception ex)
                {
                    Debug.LogError("[OutfitBatchUploader] Could not open thumbnail preview: " + ex.Message);
                }
            }

            private void OnGUI()
            {
                if (_tex == null) { Close(); return; }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("This is exactly what the VRChat thumbnail will look like:", EditorStyles.miniBoldLabel);
                EditorGUILayout.Space(2);

                float aspect = (float)_tex.width / _tex.height;
                Rect r = GUILayoutUtility.GetAspectRect(aspect);
                GUI.DrawTexture(r, _tex, ScaleMode.ScaleToFit);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(
                    "Not happy? Adjust the Scene view (or the background color) and press Preview again.",
                    EditorStyles.wordWrappedMiniLabel);

                if (_onUpload != null)
                {
                    var old = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.4f, 0.75f, 1f);
                    if (GUILayout.Button("⬆ Upload this thumbnail", GUILayout.Height(28)))
                    {
                        var cb = _onUpload;
                        _path = null;      // keep the file — the upload needs it (it cleans up itself)
                        Close();
                        cb();
                        return;
                    }
                    GUI.backgroundColor = old;
                }
                if (GUILayout.Button(_onUpload != null ? "Cancel" : "Close", GUILayout.Height(24))) Close();
            }

            private void OnDestroy()
            {
                if (_tex != null) DestroyImmediate(_tex);
                CleanupTempThumb(_path);
            }
        }

        // ---- SDK auto-fix (best-effort, reflection) ----
        /// <summary>
        /// Walks the SDK builder's internal alert lists (GUIErrors/GUIWarnings) and invokes
        /// each issue's auto-fix action. This relies on SDK internals and is intentionally
        /// defensive: any failure is swallowed and the normal (manual) SDK flow still works.
        /// Returns the number of fixes invoked.
        /// </summary>
        private int TryApplySdkAutoFixes(object builder)
        {
            int applied = 0;
            try
            {
                // The builder instance derives from VRCSdkControlPanelBuilder which holds the dicts.
                Type t = builder.GetType();
                FieldInfo errF = null, warnF = null;
                while (t != null && (errF == null || warnF == null))
                {
                    errF  ??= t.GetField("GUIErrors",   BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    warnF ??= t.GetField("GUIWarnings", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    t = t.BaseType;
                }

                applied += InvokeFixesFromDict(errF?.GetValue(builder));
                applied += InvokeFixesFromDict(warnF?.GetValue(builder));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[OutfitBatchUploader] Auto-fix pass skipped (SDK internals changed?): " + ex.Message);
            }
            return applied;
        }

        private int InvokeFixesFromDict(object dictObj)
        {
            int n = 0;
            if (dictObj is not System.Collections.IDictionary dict) return 0;

            foreach (System.Collections.DictionaryEntry kv in dict)
            {
                if (kv.Value is not System.Collections.IEnumerable list) continue;
                foreach (var issueObj in list)
                {
                    if (issueObj == null) continue;
                    var fixField = issueObj.GetType().GetField("fixThisIssue");
                    if (fixField?.GetValue(issueObj) is Action fix)
                    {
                        try { fix(); n++; }
                        catch (Exception ex)
                        {
                            Debug.LogWarning("[OutfitBatchUploader] An SDK auto-fix threw: " + ex.Message);
                        }
                    }
                }
            }
            return n;
        }
    }
}
