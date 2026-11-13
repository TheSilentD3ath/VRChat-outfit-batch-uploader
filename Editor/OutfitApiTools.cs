// ============================================================
//  VRC Outfit Batch Uploader — VRChat API tools module
//  (partial class — lives alongside OutfitBatchUploader.cs)
//
//  • Fetch my avatars: pulls your avatar list from the VRChat
//    API so Blueprint IDs can be picked from a menu (▾ next to
//    the ID field) instead of copy-pasting, and auto-matched to
//    outfits by name (☁ IDs button in the top bar).
//    The list comes from the SDK's public VRCApi.Get against the
//    "avatars" endpoint; only avatars the logged-in account owns
//    are ever offered.
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

        /// <summary>Pages through the avatars of the logged-in account.
        /// VRCApi has no "list my avatars" call — the VRCApi.GetAvatars this module used to look
        /// up by reflection exists in no SDK from 3.3.0 to 3.10.6, so ☁ IDs and the ▾ picker
        /// never returned anything. The SDK's public generic request (VRCApi.Get, marked
        /// [PublicAPI], same signature in 3.8.0 and 3.10.6) against the "avatars" endpoint
        /// returns the same VRCAvatar records VRCApi.GetAvatar does.</summary>
        private static async Task<List<VRCAvatar>> FetchAvatarListAsync()
        {
            string myId = APIUser.CurrentUser?.id;
            if (string.IsNullOrEmpty(myId))
                throw new Exception("Not logged in to the VRChat SDK.");

            var results = new List<VRCAvatar>();
            var seen = new HashSet<string>();
            bool sawForeign = false;
            const int PAGE = 50;

            for (int offset = 0; offset < 2000; offset += PAGE)
            {
                var query = new Dictionary<string, string>
                {
                    { "user", "me" },
                    { "releaseStatus", "all" },
                    { "n", PAGE.ToString() },
                    { "offset", offset.ToString() }
                };
                List<VRCAvatar> page = await VRCApi.Get<List<VRCAvatar>>("avatars", query, forceRefresh: true);
                if (page == null || page.Count == 0) break;

                int added = 0;
                foreach (var a in page)
                {
                    if (string.IsNullOrEmpty(a.ID) || !seen.Add(a.ID)) continue;
                    added++;
                    // Only ever offer avatars this account owns, whatever the server sent back:
                    // an ID that belongs to someone else must never end up on an outfit.
                    if (a.AuthorId == myId) results.Add(a);
                    else sawForeign = true;
                }
                // Nothing new on this page → the end (or a server ignoring the offset).
                if (added == 0) break;
            }

            if (results.Count == 0 && sawForeign)
                throw new Exception("VRChat returned avatars, but none of them belong to the logged-in account.");
            return results;
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

                // VRCApi.UpdateAvatarImage — parameter order/name varies per SDK version,
                // so SdkCompat binds them by name/type.
                await SdkCompat.UpdateAvatarImageAsync(entry.BlueprintId, thumbPath, avatar);

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
