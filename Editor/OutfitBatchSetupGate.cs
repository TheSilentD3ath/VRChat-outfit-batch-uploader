// ============================================================
//  VRC Outfit Batch Uploader — batch setup gate
//  (partial class — lives alongside OutfitBatchUploader.cs)
//
//  When you press "Upload All" with a mix of already-configured and
//  not-yet-set-up outfits, this walks the not-set-up ones first:
//    1. Asks once: Express-setup ALL of them? / Ask per outfit / Cancel.
//    2. Per outfit (if chosen): Express / Skip / Configure… (a modal
//       window with name, description, tags and thumbnail).
//  Each set-up outfit is created & uploaded on the current platform.
//  Afterwards the already-configured outfits run through the normal
//  platform-grouped batch.
// ============================================================

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Editor;

namespace ShiroTools
{
    public partial class OutfitBatchUploader
    {
        /// <summary>Entry point for "Upload All": set up any unconfigured outfits, then batch the rest.</summary>
        private async Task StartBatchWithSetupAsync(List<OutfitEntry> includedAll)
        {
            if (includedAll == null || includedAll.Count == 0) return;
            if (_isBatchUploading || _isExpressBusy) return;

            var configured   = includedAll.Where(o => o != null && !string.IsNullOrWhiteSpace(o.BlueprintId)).ToList();
            var unconfigured = includedAll.Where(o => o != null && o.Go != null && string.IsNullOrWhiteSpace(o.BlueprintId)).ToList();

            if (unconfigured.Count > 0)
            {
                if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out _))
                { SetStatus("Open the VRChat SDK window (and log in) to set up new outfits.", MessageType.Error); return; }
                if (!APIUser.IsLoggedIn)
                { SetStatus("Log in to the VRChat SDK to set up new outfits.", MessageType.Error); return; }

                string names = string.Join("\n• ", unconfigured.Select(o => o.Name));
                int choice = EditorUtility.DisplayDialogComplex(
                    "Set up new outfits",
                    $"{unconfigured.Count} of the selected outfit(s) aren't set up yet:\n• {names}\n\n" +
                    "Express-setup ALL of them now? Each is created & uploaded on the current platform using your defaults.",
                    "Express all", "Cancel", "Ask me per outfit");

                if (choice == 1) { SetStatus("Upload cancelled.", MessageType.Warning); return; }
                bool askEach = (choice == 2);

                foreach (var o in unconfigured)
                {
                    if (o?.Go == null) continue;

                    if (askEach)
                    {
                        int d = EditorUtility.DisplayDialogComplex(
                            "Set up outfit",
                            $"\"{o.Name}\" isn't set up yet. How do you want to handle it?",
                            "Express setup", "Skip", "Configure…");

                        if (d == 1) continue; // Skip

                        if (d == 2) // Configure manually (modal window)
                        {
                            EnsureDraft(o);
                            var draft = _nsDrafts[o.Name];
                            var res = OutfitConfigWindow.Show(o.Name, _avatarRoot != null ? _avatarRoot.name : "", draft);
                            if (res != OutfitConfigWindow.Result.Create) continue; // skipped / closed
                            await ExpressSetupAsync(o, draft, skipConfirm: true);
                            continue;
                        }
                        // d == 0 → Express
                    }

                    await ExpressSetupAsync(o, null, skipConfirm: true);
                }
            }

            _expressQuietMode = false;

            // Upload the already-configured outfits via the normal platform-grouped batch.
            // (Outfits just set up above were already uploaded on the current platform.)
            var toBatch = configured.Where(o => o.Go != null && !string.IsNullOrWhiteSpace(o.BlueprintId)).ToList();
            if (toBatch.Count > 0)
                await StartBatchAsync(toBatch);
            else if (unconfigured.Count > 0)
            {
                SetStatus("Outfit setup complete.", MessageType.Info);
                if (_soundEnabled) PlayConfirmSound();   // ONE sound for the whole setup run
            }
        }

        // ============================================================
        //  Modal configuration window (manual setup of one outfit)
        // ============================================================
        public class OutfitConfigWindow : EditorWindow
        {
            public enum Result { None, Create, Skip }

            private static Result s_result = Result.None;   // survives the window being destroyed on Close()
            private AdvancedDraft _draft;
            private string _outfitName;
            private Vector2 _scroll;

            internal static Result Show(string outfitName, string avatarName, AdvancedDraft draft)
            {
                s_result = Result.None;
                var w = CreateInstance<OutfitConfigWindow>();
                w.titleContent = new GUIContent("Set up: " + outfitName);
                w._draft = draft;
                w._outfitName = outfitName;
                w.minSize = new Vector2(400, 470);
                w.ShowModalUtility();   // blocks until the window is closed
                return s_result;
            }

            private void OnGUI()
            {
                if (_draft == null) { Close(); return; }

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField($"Configure new avatar for \"{_outfitName}\"", EditorStyles.boldLabel);
                EditorGUILayout.Space(4);

                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                _draft.Name = EditorGUILayout.TextField("Avatar name", _draft.Name);

                EditorGUILayout.LabelField("Description", EditorStyles.miniBoldLabel);
                _draft.Description = EditorGUILayout.TextArea(_draft.Description ?? "", GUILayout.MinHeight(44));

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Release status", GUILayout.Width(110));
                    int idx = _draft.Release == "public" ? 1 : 0;
                    idx = EditorGUILayout.Popup(idx, new[] { "private", "public" });
                    _draft.Release = idx == 1 ? "public" : "private";
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Content warnings", EditorStyles.miniBoldLabel);
                foreach (var tag in CONTENT_TAGS)
                    _draft.Tags[tag] = EditorGUILayout.ToggleLeft(ContentTagLabel(tag),
                        _draft.Tags.TryGetValue(tag, out var b) && b);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Thumbnail", EditorStyles.miniBoldLabel);
                int tIdx = _draft.ThumbMode == "image" ? 2 : (_draft.ThumbMode == "sceneview" ? 1 : 0);
                tIdx = EditorGUILayout.Popup(tIdx, new[]
                {
                    "Standard view (auto-framed front shot)",
                    "Scene view camera (exactly what you see)",
                    "Default image"
                });
                _draft.ThumbMode = tIdx == 2 ? "image" : (tIdx == 1 ? "sceneview" : "scene");
                if (_draft.ThumbMode == "image")
                {
                    Texture2D tex = (!string.IsNullOrEmpty(_draft.ImagePath) && _draft.ImagePath.StartsWith("Assets"))
                        ? AssetDatabase.LoadAssetAtPath<Texture2D>(_draft.ImagePath) : null;
                    var picked = (Texture2D)EditorGUILayout.ObjectField("Image", tex, typeof(Texture2D), false);
                    if (picked != null)
                    {
                        var p = AssetDatabase.GetAssetPath(picked);
                        if (!string.IsNullOrEmpty(p)) _draft.ImagePath = p;
                    }
                }
                else if (_draft.ThumbMode == "sceneview")
                    EditorGUILayout.LabelField(
                        "Uses your current Scene view angle — arrange the view before pressing Create & Upload.",
                        EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space(6);
                using (new EditorGUILayout.HorizontalScope())
                {
                    var old = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.4f, 0.75f, 1f);
                    if (GUILayout.Button("Create & Upload", GUILayout.Height(30)))
                    {
                        s_result = Result.Create;
                        Close();
                    }
                    GUI.backgroundColor = old;

                    if (GUILayout.Button("Skip this outfit", GUILayout.Height(30), GUILayout.Width(140)))
                    {
                        s_result = Result.Skip;
                        Close();
                    }
                }
                EditorGUILayout.Space(4);
            }
        }
    }
}
