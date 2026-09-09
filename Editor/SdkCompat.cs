// ============================================================
//  VRC Outfit Batch Uploader — SDK / Unity compatibility layer
//
//  Centralizes every reflection call into VRChat SDK or Unity
//  internals. Method lookups are cached, failures produce one
//  clear log message, and callers get a bool/result instead of
//  re-implementing the same BindingFlags dance everywhere.
//
//  Why: the SDK renames/reorders internals between versions.
//  With one choke point, an SDK update means fixing (or
//  gracefully disabling) a feature in exactly one place.
// ============================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDKBase.Editor.Api;   // VRCApi, VRCAvatar

namespace ShiroTools
{
    internal static class SdkCompat
    {
        // ---- Cached MethodInfo lookups (null = known-missing, don't re-warn) ----
        private static readonly Dictionary<string, MethodInfo> _methods =
            new Dictionary<string, MethodInfo>();
        private static readonly HashSet<string> _warned = new HashSet<string>();

        private static MethodInfo FindMethod(Type type, string name, BindingFlags flags, string feature)
        {
            string key = type.FullName + "::" + name;
            if (_methods.TryGetValue(key, out var cached)) return cached;

            var m = type.GetMethod(name, flags);
            if (m == null && _warned.Add(key))
                Debug.LogWarning($"[OutfitBatchUploader] {feature}: '{type.Name}.{name}' not found in this SDK/Unity version — feature disabled, manual flow still works.");

            _methods[key] = m;   // cache the miss too — lookups are per-repaint otherwise
            return m;
        }

        // ============================================================
        //  VRCCopyrightAgreement.Agree(blueprintId) — internal
        // ============================================================
        public static async Task<bool> AgreeCopyrightAsync(string blueprintId)
        {
            var m = FindMethod(typeof(VRCCopyrightAgreement), "Agree",
                BindingFlags.NonPublic | BindingFlags.Static, "Copyright pre-consent");
            if (m == null) return true;   // SDK dialog will appear normally instead
            try
            {
                return await (Task<bool>)m.Invoke(null, new object[] { blueprintId });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OutfitBatchUploader] Pre-consent failed for {blueprintId}: {ex.Message}");
                return true;   // proceed — SDK dialog is the fallback
            }
        }

        // ============================================================
        //  VRCApi.GetAvatars — signature differs between SDK versions
        //  Returns one page; caller handles paging.
        // ============================================================
        public static MethodInfo GetAvatarsMethod =>
            FindMethod(typeof(VRCApi), "GetAvatars",
                BindingFlags.Public | BindingFlags.Static, "Fetch my avatars");

        /// <summary>Builds the argument array for a paged GetAvatars call by
        /// matching parameter names/types instead of a hard-coded order.</summary>
        public static object[] BuildGetAvatarsArgs(MethodInfo method, int pageSize, int offset)
        {
            var pars = method.GetParameters();
            object[] args = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
            {
                var p = pars[i];
                string pn = (p.Name ?? "").ToLowerInvariant();
                if (p.ParameterType == typeof(int) && pn.Contains("offset")) args[i] = offset;
                else if (p.ParameterType == typeof(int))                     args[i] = pageSize;
                else if (p.HasDefaultValue)                                  args[i] = p.DefaultValue;
                else args[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
            }
            return args;
        }

        /// <summary>Awaits a Task returned via reflection and extracts Result,
        /// with an explicit error when the SDK returned a plain non-generic Task.</summary>
        public static async Task<object> InvokeTaskWithResultAsync(MethodInfo method, object[] args, string feature)
        {
            var task = (Task)method.Invoke(null, args);
            await task;
            var resultProp = task.GetType().GetProperty("Result");
            if (resultProp == null)
                throw new Exception($"{feature}: SDK returned a Task without a result — SDK version not supported by this feature.");
            return resultProp.GetValue(task);
        }

        // ============================================================
        //  VRCApi.UpdateAvatarImage — parameter order varies per version
        // ============================================================
        public static async Task UpdateAvatarImageAsync(string blueprintId, string imagePath, VRCAvatar avatar)
        {
            var m = FindMethod(typeof(VRCApi), "UpdateAvatarImage",
                BindingFlags.Public | BindingFlags.Static, "Thumbnail update");
            if (m == null)
                throw new Exception("This SDK version has no VRCApi.UpdateAvatarImage.");

            var pars = m.GetParameters();
            object[] args = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
            {
                var p = pars[i];
                string pn = (p.Name ?? "").ToLowerInvariant();
                if (p.ParameterType == typeof(string) && pn.Contains("id")) args[i] = blueprintId;
                else if (p.ParameterType == typeof(string))                 args[i] = imagePath;
                else if (p.ParameterType == typeof(VRCAvatar))              args[i] = avatar;
                else if (p.HasDefaultValue)                                 args[i] = p.DefaultValue;
                else args[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
            }
            await (Task)m.Invoke(null, args);
        }

        // ============================================================
        //  UnityEditor.AudioUtil.PlayPreviewClip — internal Unity API
        // ============================================================
        public static bool PlayPreviewClip(AudioClip clip)
        {
            var audioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            var m = audioUtil == null ? null : FindMethod(audioUtil, "PlayPreviewClip",
                BindingFlags.Static | BindingFlags.Public, "Confirm sound");
            if (m == null) return false;
            try
            {
                m.Invoke(null, new object[] { clip, 0, false });
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[OutfitBatchUploader] Could not play confirm sound: " + ex.Message);
                return false;
            }
        }
    }
}
