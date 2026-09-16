// ============================================================
//  VRC Outfit Batch Uploader — avatar version store
//
//  Base version numbers per Blueprint ID, stamped into the VRChat
//  description on upload. Lives in ProjectSettings/ShiroOutfit_versions.json
//  for the same reasons as OutfitProjectData: it survives replacing the
//  plugin folder, is scoped to this project, and can be backed up with it.
//  Writes go through OutfitProjectData.WriteAtomically, so a crash mid-save
//  leaves the previous file as .bak instead of a truncated one.
//
//  Split out of OutfitBatchUploader.cs: it is a standalone store, not part
//  of the window's partial class, and the architecture map puts persistence
//  next to the other stores rather than at the end of the UI file.
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ShiroTools
{
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
                LoadVersionsFromJson(File.ReadAllText(ConfigPath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AvatarVersionManager] Failed to load versions: {ex.Message}");
                try
                {
                    string backupPath = ConfigPath + ".bak";
                    if (File.Exists(backupPath))
                    {
                        LoadVersionsFromJson(File.ReadAllText(backupPath));
                        Debug.LogWarning("[AvatarVersionManager] Recovered versions from ShiroOutfit_versions.json.bak.");
                    }
                }
                catch (Exception backupEx)
                {
                    Debug.LogError($"[AvatarVersionManager] Failed to recover versions backup: {backupEx.Message}");
                }
            }
        }

        private static void LoadVersionsFromJson(string json)
        {
            var data = JsonUtility.FromJson<VersionData>(json);
            if (data?.versions == null) throw new InvalidDataException("Missing versions collection.");
            _versions.Clear();
            foreach (var entry in data.versions)
                if (!string.IsNullOrWhiteSpace(entry.blueprintId))
                    _versions[entry.blueprintId] = entry.version;
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
                OutfitProjectData.WriteAtomically(ConfigPath, json);
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
