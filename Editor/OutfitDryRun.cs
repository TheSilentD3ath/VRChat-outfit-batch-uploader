// ============================================================
//  VRC Outfit Batch Uploader — Dry run & upload log module
//  (partial class — lives alongside OutfitBatchUploader.cs)
//
//  Dry run: validates everything a batch would check — WITHOUT
//  uploading anything — and shows a per-outfit report:
//    • SDK builder available / logged in
//    • Blueprint ID present + valid format, duplicate IDs
//    • duplicate outfit names (would clash in saved settings)
//    • platform toggles, contact hard cap, realtime lights
//    • FaceEmo assignment pointing at a missing object,
//      stray uncaptured FaceEmoPrefab
//    • blendshape overrides configured but no skin mesh selected
//
//  Upload log: every batch/Express result is appended to
//  ProjectSettings/ShiroOutfit_upload.log — unlike the Unity
//  console this survives the domain reloads that platform
//  switches trigger mid-batch.
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Editor;

namespace ShiroTools
{
    public partial class OutfitBatchUploader
    {
        // ============================================================
        //  Upload log
        // ============================================================
        private static readonly string UPLOAD_LOG_PATH = Path.Combine("ProjectSettings", "ShiroOutfit_upload.log");

        /// <summary>Appends a timestamped line to the upload log. Never throws.</summary>
        internal static void LogUpload(string message)
        {
            try
            {
                var info = new FileInfo(UPLOAD_LOG_PATH);
                if (info.Exists && info.Length > 1_000_000)   // rotate at ~1 MB
                {
                    File.Copy(UPLOAD_LOG_PATH, UPLOAD_LOG_PATH + ".old", true);
                    File.Delete(UPLOAD_LOG_PATH);
                }
                File.AppendAllText(UPLOAD_LOG_PATH,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { /* logging must never break an upload */ }
        }

        // ============================================================
        //  Dry run
        // ============================================================
        private void RunDryRun()
        {
            var sb = new StringBuilder();
            int problems = 0, warnings = 0;

            void Err(string s)  { sb.AppendLine("  ✖ " + s); problems++; }
            void Warn(string s) { sb.AppendLine("  ⚠ " + s); warnings++; }
            void Ok(string s)   { sb.AppendLine("  ✓ " + s); }

            sb.AppendLine($"Dry run — {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine();

            // ---- Global checks ----
            sb.AppendLine("General:");
            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out _))
                Err("VRC SDK builder not available — open the VRChat SDK Control Panel.");
            else
                Ok("SDK builder available.");

            if (!APIUser.IsLoggedIn) Err("Not logged in to the VRChat SDK.");
            else Ok("Logged in.");

            var included = _outfits.Where(o => o.IncludeInBatch).ToList();
            if (included.Count == 0)
                Err("No outfits have \"Include in batch\" checked.");

            // Duplicate outfit names → their saved settings would collide
            var dupNames = _outfits.GroupBy(o => o.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            foreach (var n in dupNames)
                Err($"Two or more outfits are named \"{n}\" — their saved settings collide. Rename one.");

            // Duplicate blueprint IDs across outfits → probably a paste error
            var dupIds = _outfits.Where(o => !string.IsNullOrWhiteSpace(o.BlueprintId))
                                 .GroupBy(o => o.BlueprintId).Where(g => g.Count() > 1).ToList();
            foreach (var g in dupIds)
                Err($"Outfits {string.Join(", ", g.Select(o => $"\"{o.Name}\""))} share the same Blueprint ID ({g.Key}) — they would overwrite each other.");

            bool anyOverrides = _outfits.Any(o => o.BlendShapes.Count > 0);
            if (anyOverrides && (_skinRenderer == null || _skinRenderer.sharedMesh == null))
                Warn("Blendshape overrides are configured but no Avatar skin mesh is selected — they won't be applied.");

            var strayFaceEmo = FindAvatarChild(FACEEMO_PREFAB_NAME);
            if (strayFaceEmo != null)
                Warn("An uncaptured \"FaceEmoPrefab\" exists — it would be merged into EVERY upload. Capture or delete it.");

            // ---- Per-outfit checks ----
            RecomputeBudgetsNow();   // synchronous — the report needs fresh numbers right now
            EnsureItemsBuilt();
            bool pc = GetCurrentPlatform() == VRCPlatform.Windows;

            foreach (var o in included)
            {
                sb.AppendLine();
                sb.AppendLine($"Outfit \"{o.Name}\":");

                if (o.Go == null) { Err("GameObject is missing from the scene."); continue; }

                // Blueprint ID
                if (string.IsNullOrWhiteSpace(o.BlueprintId))
                    Warn("No Blueprint ID — Upload All would run first-time setup (Express/Advanced) for it.");
                else if (!IsValidBlueprintId(o.BlueprintId))
                    Err($"Blueprint ID \"{Truncate(o.BlueprintId, 40)}\" is not a valid avtr_<GUID> — batch refuses to start.");
                else
                    Ok("Blueprint ID looks valid.");

                // Platforms
                bool hasAny = o.BuildWindows || o.BuildAndroid || o.BuildIOS;
                if (!hasAny)
                    Warn($"No platform selected — falls back to the current platform ({GetCurrentPlatform()}).");
                else
                {
                    var plats = new List<string>();
                    if (o.BuildWindows) plats.Add("Windows");
                    if (o.BuildAndroid) plats.Add("Android");
                    if (o.BuildIOS)     plats.Add("iOS");
                    Ok("Platforms: " + string.Join(", ", plats));
                }

                // Contacts / lights budget
                ContactsFor(o, out int net, out int total);
                if (total >= CONTACT_HARD_LIMIT)
                    Err($"Contacts {total}/{CONTACT_HARD_LIMIT} — at/over the hard cap, the SDK will refuse the upload.");
                else if (net > (pc ? 32 : 16))
                    Warn($"Networked contacts {net} → rank \"Very Poor\" on {(pc ? "PC" : "Quest")}.");
                else
                    Ok($"Contacts {net} networked, {total}/{CONTACT_HARD_LIMIT} total.");

                int lights = LightsFor(o);
                if (lights > 0)
                    Warn($"{lights} realtime light(s) upload with this outfit — avatars should have 0.");

                // FaceEmo
                string fe = GetFaceEmoName(o.Name);
                if (!string.IsNullOrEmpty(fe) && FindAvatarChild(fe) == null)
                    Warn($"Assigned FaceEmo object \"{fe}\" is missing from the scene.");

                // Quest/iOS shader compatibility (only VRChat/Mobile shaders are allowed on mobile avatars)
                if (o.BuildAndroid || o.BuildIOS)
                {
                    var bad = new HashSet<string>();
                    foreach (var rend in CollectUploadRenderers(o))
                        foreach (var mat in rend.sharedMaterials)
                            if (mat != null && mat.shader != null &&
                                !mat.shader.name.StartsWith("VRChat/Mobile/", StringComparison.Ordinal))
                                bad.Add($"{mat.name} ({mat.shader.name})");

                    if (bad.Count > 0)
                        Err($"Android/iOS build has {bad.Count} material(s) without a VRChat/Mobile shader — " +
                            "the SDK will refuse the mobile upload: " +
                            string.Join(", ", bad.Take(4)) + (bad.Count > 4 ? ", …" : ""));
                    else
                        Ok("All materials use VRChat/Mobile shaders (Quest/iOS-ready).");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"Result: {problems} problem(s), {warnings} warning(s), {included.Count} outfit(s) checked. Nothing was uploaded.");

            string report = sb.ToString();
            Debug.Log("[OutfitBatchUploader] " + report);
            DryRunReportWindow.Open(report, problems, warnings);
        }

        /// <summary>Scrollable report window for the dry run.</summary>
        internal class DryRunReportWindow : EditorWindow
        {
            private string _text = "";
            private int _problems, _warnings;
            private Vector2 _scroll;

            internal static void Open(string text, int problems, int warnings)
            {
                var w = GetWindow<DryRunReportWindow>(true, "Dry run report");
                w._text = text;
                w._problems = problems;
                w._warnings = warnings;
                w.minSize = new Vector2(480, 360);
                w.Show();
            }

            private void OnGUI()
            {
                EditorGUILayout.Space(4);
                if (_problems > 0)
                    EditorGUILayout.HelpBox($"{_problems} problem(s) would stop or break the batch. Fix them first.", MessageType.Error);
                else if (_warnings > 0)
                    EditorGUILayout.HelpBox($"No blockers — {_warnings} warning(s) worth a look.", MessageType.Warning);
                else
                    EditorGUILayout.HelpBox("All checks passed — the batch should run through.", MessageType.Info);

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                EditorGUILayout.TextArea(_text, EditorStyles.wordWrappedLabel, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("Close", GUILayout.Height(24))) Close();
                EditorGUILayout.Space(4);
            }
        }
    }
}
