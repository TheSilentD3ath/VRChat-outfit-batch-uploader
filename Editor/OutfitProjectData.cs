// ============================================================
//  VRC Outfit Batch Uploader — project-scoped settings store
//
//  All per-avatar / per-outfit data (blueprint IDs, batch and
//  platform toggles, blendshape overrides, item selections and
//  FaceEmo captures) lives in ProjectSettings/ShiroOutfit_data.json.
//
//  Why ProjectSettings and not EditorPrefs:
//    • survives deleting/replacing the plugin folder on updates
//      (ProjectSettings is never touched by that)
//    • scoped to THIS project — two projects with the same avatar
//      name no longer share blueprint IDs
//    • can be committed / backed up together with the project
//
//  Legacy EditorPrefs entries are migrated lazily: the first time
//  a value is read and the JSON doesn't know it yet, the old
//  EditorPrefs value is imported and saved. Nothing is lost.
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShiroTools
{
    internal static class OutfitProjectData
    {
        private const string FILE_NAME = "ShiroOutfit_data.json";
        private static string FilePath => Path.Combine("ProjectSettings", FILE_NAME);

        // ---- Legacy EditorPrefs key patterns (for one-time migration) ----
        private const string LEGACY_PREFIX             = "ShiroOutfitUploader_";
        private const string LEGACY_ITEM_PREFIX        = "ShiroItem_";
        private const string LEGACY_ITEM_DEFAULT_PREFIX = "ShiroItemDefault_";
        private const string LEGACY_FACEEMO_PREFIX     = "ShiroFaceEmo_";

        // ============================================================
        //  Data model (JsonUtility-friendly: no dictionaries)
        // ============================================================
        [Serializable]
        internal class BlendShapeOverride
        {
            public string name;
            public float  value;
        }

        [Serializable]
        internal class ItemOverride
        {
            public string name;
            public bool   included;
        }

        [Serializable]
        internal class OutfitData
        {
            public string name;
            public string blueprintId    = "";
            public bool   includeInBatch = true;
            public bool   buildWindows   = true;
            public bool   buildAndroid;
            public bool   buildIOS;
            public string faceEmoName    = "";
            // Last successful upload per platform ("yyyy-MM-dd HH:mm", empty = never)
            public string lastUploadWindows = "";
            public string lastUploadAndroid = "";
            public string lastUploadIOS     = "";
            public List<BlendShapeOverride> blendShapes   = new List<BlendShapeOverride>();
            public List<ItemOverride>       itemOverrides = new List<ItemOverride>();
        }

        [Serializable]
        internal class AvatarData
        {
            public string name;
            public List<OutfitData> outfits      = new List<OutfitData>();
            // Item names included on EVERY outfit by default (per-outfit overrides win)
            public List<string>     itemDefaults = new List<string>();
            // Item names whose default was explicitly decided (so migration runs only once each)
            public List<string>     itemDefaultsDecided = new List<string>();
        }

        [Serializable]
        private class Root
        {
            public List<AvatarData> avatars = new List<AvatarData>();
        }

        private static Root _root;

        // ============================================================
        //  Load / save
        // ============================================================
        private static Root Data
        {
            get
            {
                if (_root == null) Load();
                return _root;
            }
        }

        private static void Load()
        {
            _root = new Root();
            try
            {
                if (File.Exists(FilePath))
                {
                    var parsed = JsonUtility.FromJson<Root>(File.ReadAllText(FilePath));
                    if (parsed != null && parsed.avatars != null) _root = parsed;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OutfitBatchUploader] Could not read {FILE_NAME}: {ex.Message}");
            }
        }

        internal static void Save()
        {
            if (_root == null) return;
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(_root, true));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OutfitBatchUploader] Could not write {FILE_NAME}: {ex.Message}");
            }
        }

        // ============================================================
        //  Accessors
        // ============================================================
        internal static AvatarData GetAvatar(string avatarName)
        {
            var av = Data.avatars.FirstOrDefault(a => a.name == avatarName);
            if (av == null)
            {
                av = new AvatarData { name = avatarName };
                Data.avatars.Add(av);
            }
            return av;
        }

        /// <summary>Returns the outfit record, creating it (and importing any legacy
        /// EditorPrefs values for it) if the JSON doesn't know it yet.</summary>
        internal static OutfitData GetOutfit(string avatarName, string outfitName)
        {
            var av = GetAvatar(avatarName);
            var o = av.outfits.FirstOrDefault(x => x.name == outfitName);
            if (o != null) return o;

            o = new OutfitData { name = outfitName };
            MigrateLegacyOutfit(avatarName, o);
            av.outfits.Add(o);
            Save();
            return o;
        }

        // ---- Items ----
        internal static bool GetItemIncluded(string avatarName, string outfitName, string itemName)
        {
            var o = GetOutfit(avatarName, outfitName);
            var ov = o.itemOverrides.FirstOrDefault(x => x.name == itemName);
            if (ov != null) return ov.included;

            // Legacy per-outfit override?
            string legacyKey = LEGACY_ITEM_PREFIX + avatarName + "_" + outfitName + "_" + itemName;
            if (EditorPrefs.HasKey(legacyKey))
            {
                bool val = EditorPrefs.GetBool(legacyKey, false);
                o.itemOverrides.Add(new ItemOverride { name = itemName, included = val });
                Save();
                return val;
            }

            return GetItemDefault(avatarName, itemName);
        }

        internal static void SetItemIncluded(string avatarName, string outfitName, string itemName, bool included)
        {
            var o = GetOutfit(avatarName, outfitName);
            var ov = o.itemOverrides.FirstOrDefault(x => x.name == itemName);
            if (ov == null) o.itemOverrides.Add(new ItemOverride { name = itemName, included = included });
            else ov.included = included;
            Save();
        }

        internal static bool GetItemDefault(string avatarName, string itemName)
        {
            var av = GetAvatar(avatarName);
            if (av.itemDefaults.Contains(itemName)) return true;
            if (av.itemDefaultsDecided.Contains(itemName)) return false;

            // Legacy global default (old model was not per avatar)
            string legacyKey = LEGACY_ITEM_DEFAULT_PREFIX + itemName;
            if (EditorPrefs.HasKey(legacyKey))
            {
                bool val = EditorPrefs.GetBool(legacyKey, false);
                av.itemDefaultsDecided.Add(itemName);
                if (val) av.itemDefaults.Add(itemName);
                Save();
                return val;
            }
            return false;
        }

        internal static void SetItemDefault(string avatarName, string itemName, bool included)
        {
            var av = GetAvatar(avatarName);
            if (!av.itemDefaultsDecided.Contains(itemName)) av.itemDefaultsDecided.Add(itemName);
            bool has = av.itemDefaults.Contains(itemName);
            if (included && !has) av.itemDefaults.Add(itemName);
            if (!included && has) av.itemDefaults.Remove(itemName);
            Save();
        }

        // ---- Last upload timestamps ----
        internal static void MarkUploaded(OutfitData o, string platform)
        {
            if (o == null) return;
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm",
                System.Globalization.CultureInfo.InvariantCulture);
            switch (platform)
            {
                case "Android": o.lastUploadAndroid = now; break;
                case "iOS":     o.lastUploadIOS     = now; break;
                default:        o.lastUploadWindows = now; break;
            }
            Save();
        }

        // ---- Export / import (whole file, for backups & transferring to another project) ----
        internal static string ExportRaw()
        {
            Save();
            try
            {
                return File.Exists(FilePath)
                    ? File.ReadAllText(FilePath)
                    : JsonUtility.ToJson(new Root(), true);
            }
            catch { return null; }
        }

        internal static bool ImportRaw(string json)
        {
            try
            {
                var parsed = JsonUtility.FromJson<Root>(json);
                if (parsed == null || parsed.avatars == null || parsed.avatars.Count == 0) return false;
                _root = parsed;
                Save();
                return true;
            }
            catch { return false; }
        }

        // ---- FaceEmo ----
        internal static string GetFaceEmoName(string avatarName, string outfitName) =>
            GetOutfit(avatarName, outfitName).faceEmoName ?? "";

        internal static void SetFaceEmoName(string avatarName, string outfitName, string value)
        {
            GetOutfit(avatarName, outfitName).faceEmoName = value ?? "";
            Save();
        }

        // ============================================================
        //  Legacy EditorPrefs migration (per outfit, runs once)
        // ============================================================
        private static void MigrateLegacyOutfit(string avatarName, OutfitData o)
        {
            try
            {
                string prefKey = LEGACY_PREFIX + avatarName + "_" + o.name;

                if (EditorPrefs.HasKey(prefKey))
                    o.blueprintId = EditorPrefs.GetString(prefKey, "");
                if (EditorPrefs.HasKey(prefKey + "_batch"))
                    o.includeInBatch = EditorPrefs.GetBool(prefKey + "_batch", true);

                // Platform toggles were additionally scoped by a project hash
                string projKey = Hash128.Compute(Application.dataPath).ToString();
                if (EditorPrefs.HasKey(prefKey + "_" + projKey + "_Win"))
                    o.buildWindows = EditorPrefs.GetBool(prefKey + "_" + projKey + "_Win", true);
                if (EditorPrefs.HasKey(prefKey + "_" + projKey + "_And"))
                    o.buildAndroid = EditorPrefs.GetBool(prefKey + "_" + projKey + "_And", false);
                if (EditorPrefs.HasKey(prefKey + "_" + projKey + "_iOS"))
                    o.buildIOS = EditorPrefs.GetBool(prefKey + "_" + projKey + "_iOS", false);

                // Blendshape overrides ("keys" list + one float per name)
                string keys = EditorPrefs.GetString(prefKey + "_BS_keys", "");
                if (!string.IsNullOrEmpty(keys))
                {
                    foreach (var k in keys.Split(';'))
                    {
                        if (string.IsNullOrEmpty(k)) continue;
                        o.blendShapes.Add(new BlendShapeOverride
                        {
                            name  = k,
                            value = EditorPrefs.GetFloat(prefKey + "_BS_" + k, 0f)
                        });
                    }
                }

                // FaceEmo capture name
                string feKey = LEGACY_FACEEMO_PREFIX + avatarName + "_" + o.name;
                if (EditorPrefs.HasKey(feKey))
                    o.faceEmoName = EditorPrefs.GetString(feKey, "");

                if (!string.IsNullOrEmpty(o.blueprintId) || o.blendShapes.Count > 0 || !string.IsNullOrEmpty(o.faceEmoName))
                    Debug.Log($"[OutfitBatchUploader] Migrated legacy settings for '{avatarName}/{o.name}' into ProjectSettings/{FILE_NAME}.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OutfitBatchUploader] Legacy settings migration for '{o.name}' failed: {ex.Message}");
            }
        }
    }
}
