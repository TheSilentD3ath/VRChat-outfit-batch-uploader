// ============================================================
//  VRC Outfit Batch Uploader — VRChat API tools module
//  (partial class — lives alongside OutfitBatchUploader.cs)
//
//  • Fetch my avatars: pulls your avatar list from the VRChat
//    API so Blueprint IDs can be picked from a menu (▾ next to
//    the ID field) instead of copy-pasting, and auto-matched to
//    outfits by name (☁ IDs button in the top bar).
//    VRCApi.GetAvatars is reached via reflection so this module
//    degrades gracefully on SDK versions where it differs.
//  • Thumbnail update: per-outfit "Thumb" button — captures a
//    new thumbnail (with preview!) and uploads ONLY the image,
//    without rebuilding the avatar.
//  • Settings export/import: one JSON bundle with all per-avatar
//    data + versions, for backups or moving to another project.
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDKBase.Editor.Api;   // VRCApi, VRCAvatar

namespace ShiroTools
{
    public partial class OutfitBatchUploader
    {
        // ============================================================
        //  Fetch my avatars
        // ============================================================
        private List<VRCAvatar> _fetchedAvatars;
        private bool _isFetchingAvatars;

        private async Task<bool> EnsureAvatarsFetchedAsync(bool force = false)
        {
            if (_fetchedAvatars != null && _fetchedAvatars.Count > 0 && !force) return true;
            if (_isFetchingAvatars) return false;
            if (!APIUser.IsLoggedIn)
            {
                SetStatus("Log in to the VRChat SDK Control Panel first.", MessageType.Error);
                return false;
            }

            _isFetchingAvatars = true;
            SetStatus("Fetching your avatars from VRChat…", MessageType.Info);
            Repaint();
            try
            {
                _fetchedAvatars = await FetchAvatarListAsync();
                SetStatus($"Fetched {_fetchedAvatars.Count} avatar(s) from your VRChat account.", MessageType.Info);
                return _fetchedAvatars.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.LogError("[OutfitBatchUploader] Could not fetch avatar list: " + ex);
                SetStatus("Could not fetch avatars: " + Truncate(ex.Message, 120), MessageType.Error);
                return false;
            }
            finally
            {
                _isFetchingAvatars = false;
                Repaint();
            }
        }

        /// <summary>Pages through VRCApi.GetAvatars via reflection (signature differs between
        /// SDK versions). Throws with a clear message when the method doesn't exist.</summary>
        private static async Task<List<VRCAvatar>> FetchAvatarListAsync()
        {
            var results = new List<VRCAvatar>();

            var method = typeof(VRCApi).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetAvatars");
            if (method == null)
                throw new Exception("This SDK version has no VRCApi.GetAvatars — paste Blueprint IDs manually.");

            var pars = method.GetParameters();
            const int PAGE = 50;

            for (int offset = 0; offset < 1000; )
            {
                object[] args = new object[pars.Length];
                for (int i = 0; i < pars.Length; i++)
                {
                    var p = pars[i];
                    string pn = (p.Name ?? "").ToLowerInvariant();
                    if (p.ParameterType == typeof(int) && pn.Contains("offset"))      args[i] = offset;
                    else if (p.ParameterType == typeof(int))                          args[i] = PAGE;   // count / number / n
                    else if (p.HasDefaultValue)                                       args[i] = p.DefaultValue;
                    else args[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
                }

                var task = (Task)method.Invoke(null, args);
                await task;
                object result = task.GetType().GetProperty("Result")?.GetValue(task);

                var page = ExtractAvatars(result);
                if (page.Count == 0) break;
                results.AddRange(page);
                if (page.Count < PAGE) break;
                offset += page.Count;
            }
            return results;
        }

        /// <summary>Pulls VRCAvatar entries out of whatever GetAvatars returned
        /// (a plain list, or a wrapper object holding an enumerable).</summary>
        private static List<VRCAvatar> ExtractAvatars(object result)
        {
            var list = new List<VRCAvatar>();
            if (result == null) return list;

            if (result is IEnumerable<VRCAvatar> direct)
            {
                list.AddRange(direct);
                return list;
            }

            foreach (var prop in result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.PropertyType == typeof(string)) continue;
                if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType)) continue;
                if (prop.GetValue(result) is System.Collections.IEnumerable en)
                    foreach (var item in en)
                        if (item is VRCAvatar a) list.Add(a);
                if (list.Count > 0) return list;
            }
            return list;
        }

        // ---- Per-outfit picker (▾ button next to the Blueprint ID field) ----
        private async Task ShowAvatarPickerAsync(OutfitEntry entry)
        {
            if (!await EnsureAvatarsFetchedAsync()) return;

            var menu = new GenericMenu();
            foreach (var a in _fetchedAvatars.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var av = a;   // capture
                string label = (av.Name ?? "(unnamed)").Replace("/", "⁄") + $"   [{av.ReleaseStatus}]";
                menu.AddItem(new GUIContent(label), av.ID == entry.BlueprintId, () =>
                {
                    entry.BlueprintId = av.ID;
                    if (entry.Data != null) { entry.Data.blueprintId = av.ID; OutfitProjectData.Save(); }
                    SetStatus($"✓ \"{entry.Name}\" → {av.Name} ({av.ID})", MessageType.Info);
                    Repaint();
                });
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("↻ Refresh list"), false, () => _ = EnsureAvatarsFetchedAsync(true));
            menu.ShowAsContext();
        }

        // ---- Fetch & auto-match (☁ IDs button in the top bar) ----
        private async Task FetchAndMatchAsync()
        {
            if (!await EnsureAvatarsFetchedAsync(true)) return;
            LoadNewSetupDefaults();

            var matches = new List<(OutfitEntry entry, VRCAvatar avatar)>();
            foreach (var o in _outfits.Where(o => o.Go != null && string.IsNullOrWhiteSpace(o.BlueprintId)))
            {
                string templated = ApplyTokens(_nsNameTemplate, o);
                var hit = _fetchedAvatars.FirstOrDefault(a =>
                    string.Equals(a.Name, o.Name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.Name, templated, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(hit.ID)) matches.Add((o, hit));
            }

            if (matches.Count == 0)
            {
                SetStatus("No name matches found — use the ▾ button on each outfit to pick manually.", MessageType.Info);
                return;
            }

            string listing = string.Join("\n", matches.Select(m => $"• {m.entry.Name}  →  {m.avatar.Name}"));
            if (!EditorUtility.DisplayDialog("Assign matched Blueprint IDs",
                    $"{matches.Count} outfit(s) match an avatar on your account by name:\n\n{listing}\n\nAssign these IDs?",
                    "Assign", "Cancel"))
                return;

            foreach (var (entry, avatar) in matches)
            {
                entry.BlueprintId = avatar.ID;
                if (entry.Data != null) entry.Data.blueprintId = avatar.ID;
            }
            OutfitProjectData.Save();
            SetStatus($"✓ Assigned {matches.Count} Blueprint ID(s) by name match.", MessageType.Info);
            Repaint();
        }

        // ============================================================
        //  Thumbnail update for existing outfits ("Thumb" button)
        // ============================================================
        private void StartThumbnailUpdate(OutfitEntry entry)
        {
            if (!IsValidBlueprintId(entry.BlueprintId))
            {
                SetStatus("This outfit has no valid Blueprint ID yet — use Express setup first.", MessageType.Warning);
                return;
            }
            if (!APIUser.IsLoggedIn)
            {
                SetStatus("Log in to the VRChat SDK Control Panel first.", MessageType.Error);
                return;
            }

            LoadNewSetupDefaults();
            ActivateOutfit(entry);   // make sure the right outfit is visible for the capture

            string path = ResolveThumbnailPath(entry, null);
            if (string.IsNullOrEmpty(path))
            {
                SetStatus("Could not capture a thumbnail — see Console.", MessageType.Error);
                return;
            }

            // Preview first — upload only happens when the user confirms in the window.
            ThumbPreviewWindow.Open(path, () => _ = UploadThumbnailAsync(entry, path));
        }

        private async Task UploadThumbnailAsync(OutfitEntry entry, string thumbPath)
        {
            try
            {
                SetStatus($"Updating thumbnail for '{entry.Name}'…", MessageType.Info);
                Repaint();

                var avatar = await VRCApi.GetAvatar(entry.BlueprintId);

                // VRCApi.UpdateAvatarImage via reflection (parameter order/name varies per SDK version)
                var method = typeof(VRCApi).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "UpdateAvatarImage");
                if (method == null)
                    throw new Exception("This SDK version has no VRCApi.UpdateAvatarImage.");

                var pars = method.GetParameters();
                object[] args = new object[pars.Length];
                for (int i = 0; i < pars.Length; i++)
                {
                    var p = pars[i];
                    string pn = (p.Name ?? "").ToLowerInvariant();
                    if (p.ParameterType == typeof(string) && pn.Contains("id"))          args[i] = entry.BlueprintId;
                    else if (p.ParameterType == typeof(string))                          args[i] = thumbPath;   // pathToImage
                    else if (p.ParameterType == typeof(VRCAvatar))                       args[i] = avatar;
                    else if (p.HasDefaultValue)                                          args[i] = p.DefaultValue;
                    else args[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
                }

                var task = (Task)method.Invoke(null, args);
                await task;

                LogUpload($"OK    {entry.Name} (thumbnail update) → {entry.BlueprintId}");
                SetStatus($"✓ Thumbnail updated for '{entry.Name}'.", MessageType.Info);
                if (_soundEnabled) PlayConfirmSound();
            }
            catch (Exception ex)
            {
                Debug.LogError("[OutfitBatchUploader] Thumbnail update failed: " + ex);
                LogUpload($"FAIL  {entry.Name} (thumbnail update): {ex.Message}");
                SetStatus("Thumbnail update failed: " + Truncate(ex.Message, 120), MessageType.Error);
            }
            finally
            {
                CleanupTempThumb(thumbPath);
                Repaint();
            }
        }

        // ============================================================
        //  Settings export / import (backup, move to another project)
        // ============================================================
        [Serializable]
        private class SettingsBundle
        {
            public string data;      // ShiroOutfit_data.json content
            public string versions;  // ShiroOutfit_versions.json content
        }

        private void ExportAllSettings()
        {
            string path = EditorUtility.SaveFilePanel("Export Outfit Uploader settings",
                "", "ShiroOutfit_backup.json", "json");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var bundle = new SettingsBundle
                {
                    data     = OutfitProjectData.ExportRaw() ?? "",
                    versions = AvatarVersionManager.ExportRaw() ?? ""
                };
                File.WriteAllText(path, JsonUtility.ToJson(bundle, true));
                SetStatus("✓ Settings exported to " + Path.GetFileName(path), MessageType.Info);
            }
            catch (Exception ex)
            {
                SetStatus("Export failed: " + Truncate(ex.Message, 120), MessageType.Error);
            }
        }

        private void ImportAllSettings()
        {
            string path = EditorUtility.OpenFilePanel("Import Outfit Uploader settings", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            if (!EditorUtility.DisplayDialog("Import settings",
                    "This REPLACES all Outfit Uploader settings of this project (Blueprint IDs, blendshapes, " +
                    "items, FaceEmo captures, versions) with the file's contents.\n\nContinue?",
                    "Import", "Cancel"))
                return;

            try
            {
                string json = File.ReadAllText(path);
                var bundle = JsonUtility.FromJson<SettingsBundle>(json);

                bool ok;
                if (bundle != null && !string.IsNullOrEmpty(bundle.data))
                {
                    ok = OutfitProjectData.ImportRaw(bundle.data);
                    if (ok && !string.IsNullOrEmpty(bundle.versions))
                        AvatarVersionManager.ImportRaw(bundle.versions);
                }
                else
                {
                    ok = OutfitProjectData.ImportRaw(json);   // plain data-file fallback
                }

                if (ok)
                {
                    ScanScene();
                    SetStatus("✓ Settings imported.", MessageType.Info);
                }
                else
                    SetStatus("Import failed — not a valid settings file.", MessageType.Error);
            }
            catch (Exception ex)
            {
                SetStatus("Import failed: " + Truncate(ex.Message, 120), MessageType.Error);
            }
            Repaint();
        }
    }
}
