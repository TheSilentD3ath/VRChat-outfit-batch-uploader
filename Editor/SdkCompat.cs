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
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDK3A.Editor;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRC.SDKBase.Editor;
using VRC.SDKBase.Editor.Api;   // VRCApi, VRCAvatar
// The VRC import set OutfitBatchUploader.cs compiles with. Only VRCApi / VRCAvatar (public
// SDK API) are referenced as types here; SDK internals are looked up by name — see FindType.

namespace ShiroTools
{
    internal static class SdkCompat
    {
        // ---- Cached lookups (null = known-missing, don't re-warn) ----
        private static readonly Dictionary<string, MethodInfo> _methods =
            new Dictionary<string, MethodInfo>();
        private static readonly Dictionary<string, Type> _types =
            new Dictionary<string, Type>();
        private static readonly HashSet<string> _warned = new HashSet<string>();

        /// <summary>Resolves an SDK type by its simple name, once, across the loaded assemblies.
        /// Use this instead of typeof() for anything that is not documented public SDK API:
        /// typeof() is a compile-time reference, so a class the SDK moves or removes stops the
        /// WHOLE plugin from compiling — a name lookup only switches off the one feature.</summary>
        private static Type FindType(string simpleName, string feature)
        {
            if (_types.TryGetValue(simpleName, out var cached)) return cached;

            Type found = null;
            // SDK assemblies first — on a hit nothing else gets scanned.
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .OrderBy(a => IsSdkAssembly(a) ? 0 : 1);
            foreach (var asm in assemblies)
            {
                Type[] candidates;
                try { candidates = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { candidates = ex.Types; }   // partly loadable
                catch (Exception) { continue; }
                if (candidates == null) continue;

                found = candidates.FirstOrDefault(t => t != null && t.Name == simpleName);
                if (found != null) break;
            }

            if (found == null && _warned.Add("type::" + simpleName))
                Debug.LogWarning($"[OutfitBatchUploader] {feature}: type '{simpleName}' not found in this SDK version — feature disabled, manual flow still works.");

            _types[simpleName] = found;   // cache the miss too
            return found;
        }

        private static bool IsSdkAssembly(Assembly assembly)
        {
            try { return (assembly.GetName().Name ?? "").IndexOf("VRC", StringComparison.OrdinalIgnoreCase) >= 0; }
            catch (Exception) { return false; }   // nameless / dynamic assembly — not the SDK
        }

        /// <summary>Resolves a method once and caches the result (hits and misses alike).
        /// Pass <paramref name="signature"/> when the SDK/Unity type has overloads of this
        /// name — a name-only lookup would otherwise throw AmbiguousMatchException.</summary>
        private static MethodInfo FindMethod(Type type, string name, BindingFlags flags, string feature,
                                             Type[] signature = null)
        {
            string key = type.FullName + "::" + name +
                         (signature == null ? "" : "(" + string.Join(",", signature.Select(t => t.Name)) + ")");
            if (_methods.TryGetValue(key, out var cached)) return cached;

            MethodInfo m;
            try
            {
                m = signature != null
                    ? type.GetMethod(name, flags, null, signature, null)
                    : type.GetMethod(name, flags);
            }
            catch (AmbiguousMatchException)
            {
                // Overloads exist but the caller binds arguments by name/type anyway
                // (UpdateAvatarImageAsync) — take the first by name.
                m = type.GetMethods(flags).FirstOrDefault(x => x.Name == name);
            }

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
            // By name, not typeof(VRCCopyrightAgreement): that reference is what failed the first
            // Unity compile of this file (CS0246). Public | NonPublic so the lookup also survives
            // the SDK changing the method's visibility.
            var type = FindType("VRCCopyrightAgreement", "Copyright pre-consent");
            var m = type == null ? null : FindMethod(type, "Agree",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, "Copyright pre-consent");
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

        // (The former GetAvatars helpers lived here. They looked up VRCApi.GetAvatars, a method
        //  that exists in no SDK from 3.3.0 to 3.10.6, so the avatar list never loaded. Listing
        //  now goes through the SDK's public VRCApi.Get — see OutfitApiTools.FetchAvatarListAsync.)

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
                BindingFlags.Static | BindingFlags.Public, "Confirm sound",
                new[] { typeof(AudioClip), typeof(int), typeof(bool) });
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
