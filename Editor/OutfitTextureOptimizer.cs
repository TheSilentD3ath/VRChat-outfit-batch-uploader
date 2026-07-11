// ============================================================
//  VRC Outfit Batch Uploader — Texture / VRAM optimizer module
//  (partial class — lives alongside OutfitBatchUploader.cs)
//
//  Reduces the VRAM footprint of an outfit's textures using the
//  same recommendations as Thry's Avatar Performance Tools
//  (MIT, (c) Thryrallo): block-compress uncompressed textures
//  (BC7 for alpha/normal maps, DXT1 otherwise) and cap oversized
//  textures' resolution. Changes are applied through the
//  TextureImporter and are NOT undo-able, so they are gated behind
//  a preview/confirmation (configurable).
//
//  Recommendation logic ported from:
//    de.thryrallo.vrc.avatar-performance-tools / TextureVRAM.cs
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShiroTools
{
    public partial class OutfitBatchUploader
    {
        // ---- Defaults prefs keys ----
        private const string NS_OPT_ENABLED = "ShiroNewOutfit_OptEnabled";
        private const string NS_OPT_ASK     = "ShiroNewOutfit_OptAsk";
        private const string NS_OPT_MAXRES  = "ShiroNewOutfit_OptMaxRes";
        private const string NS_OPT_MINRES  = "ShiroNewOutfit_OptMinRes";

        // ---- Runtime defaults ----
        private bool _optLoaded;
        private bool _nsOptEnabled;     // run automatically during Express
        private bool _nsOptAsk = true;  // ask before applying
        private int  _nsOptMaxRes = 2048;
        private int  _nsOptMinRes = 0;  // never reduce a texture below this (0 = no floor)

        private void EnsureOptDefaults()
        {
            if (_optLoaded) return;
            _optLoaded = true;
            _nsOptEnabled = EditorPrefs.GetBool(NS_OPT_ENABLED, false);
            _nsOptAsk     = EditorPrefs.GetBool(NS_OPT_ASK, true);
            _nsOptMaxRes  = EditorPrefs.GetInt(NS_OPT_MAXRES, 2048);
            _nsOptMinRes  = EditorPrefs.GetInt(NS_OPT_MINRES, 0);
        }

        private void SaveOptDefaults()
        {
            EditorPrefs.SetBool(NS_OPT_ENABLED, _nsOptEnabled);
            EditorPrefs.SetBool(NS_OPT_ASK, _nsOptAsk);
            EditorPrefs.SetInt(NS_OPT_MAXRES, Mathf.Clamp(_nsOptMaxRes, 32, 8192));
            EditorPrefs.SetInt(NS_OPT_MINRES, Mathf.Clamp(_nsOptMinRes, 0, 8192));
        }

        // ============================================================
        //  Defaults UI (drawn under the New Outfit Defaults section)
        // ============================================================
        private void DrawTextureOptDefaults()
        {
            EnsureOptDefaults();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Texture optimization (VRAM)", EditorStyles.miniBoldLabel);

            EditorGUI.BeginChangeCheck();

            _nsOptEnabled = EditorGUILayout.ToggleLeft(
                "Optimize textures during Express setup", _nsOptEnabled);
            using (new EditorGUI.DisabledScope(!_nsOptEnabled))
            {
                _nsOptAsk = EditorGUILayout.ToggleLeft(
                    "Ask before applying (off = apply automatically)", _nsOptAsk);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Max resolution (cap)", GUILayout.Width(150));
                _nsOptMaxRes = EditorGUILayout.IntPopup(_nsOptMaxRes,
                    new[] { "256", "512", "1024", "2048", "4096" },
                    new[] { 256, 512, 1024, 2048, 4096 });
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Never reduce below", GUILayout.Width(150));
                _nsOptMinRes = EditorGUILayout.IntPopup(_nsOptMinRes,
                    new[] { "no floor", "256", "512", "1024", "2048" },
                    new[] { 0, 256, 512, 1024, 2048 });
            }

            if (EditorGUI.EndChangeCheck())
                SaveOptDefaults();

            EditorGUILayout.LabelField(
                "Recommendations match Thry's Avatar Performance Tools (MIT). Changes to texture " +
                "import settings are NOT undo-able.", EditorStyles.wordWrappedMiniLabel);
        }

        // ============================================================
        //  Public entry points
        // ============================================================

        /// <summary>Manual per-outfit optimization (from the outfit row "VRAM" button).
        /// Always previews + confirms.</summary>
        private void OptimizeOutfitTextures(OutfitEntry entry)
        {
            EnsureOptDefaults();
            if (entry?.Go == null) return;

            var plan = BuildOptimizationPlan(entry.Go);
            if (plan.Count == 0)
            {
                SetStatus($"'{entry.Name}': textures already optimal — nothing to do.", MessageType.Info);
                return;
            }

            long saved = plan.Sum(p => p.SavedBytes);
            LogPlan(entry.Name, plan, saved);

            bool ok = EditorUtility.DisplayDialog(
                "Optimize textures (VRAM)",
                BuildPlanSummary(entry.Name, plan, saved) +
                "\n\nThis changes the textures' import settings and is NOT undo-able. Continue?",
                "Optimize", "Cancel");
            if (!ok) { SetStatus("Texture optimization cancelled.", MessageType.Warning); return; }

            ApplyPlan(plan);
            SetStatus($"✓ Optimized {plan.Count} texture(s) for '{entry.Name}' — saved ~{Mib(saved)}.", MessageType.Info);
        }

        /// <summary>Called from the Express flow. Honours the "enabled" / "ask" defaults.</summary>
        private void MaybeOptimizeDuringExpress(OutfitEntry entry)
        {
            EnsureOptDefaults();
            if (!_nsOptEnabled || entry?.Go == null) return;

            var plan = BuildOptimizationPlan(entry.Go);
            if (plan.Count == 0) return;

            long saved = plan.Sum(p => p.SavedBytes);
            LogPlan(entry.Name, plan, saved);

            if (_nsOptAsk)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Optimize textures (VRAM)",
                    BuildPlanSummary(entry.Name, plan, saved) +
                    "\n\nThis changes texture import settings and is NOT undo-able.",
                    "Optimize now", "Skip", "Always (don't ask again)");

                if (choice == 1) return;                 // Skip
                if (choice == 2) { _nsOptAsk = false; SaveOptDefaults(); }  // Always
            }

            ApplyPlan(plan);
            SetStatus($"Optimized {plan.Count} texture(s) — saved ~{Mib(saved)}.", MessageType.Info);
            Repaint();
        }

        // ============================================================
        //  Plan building / applying
        // ============================================================
        private class TexOpt
        {
            public Texture2D Texture;
            public string Path;
            public int CurrentRes;
            public int TargetRes;
            public bool ChangeFormat;
            public TextureImporterFormat TargetFormat;
            public long SavedBytes;
        }

        private List<TexOpt> BuildOptimizationPlan(GameObject outfitGo)
        {
            var result = new List<TexOpt>();
            var seen = new HashSet<Texture2D>();

            foreach (var tex in CollectOutfitTextures(outfitGo))
            {
                if (tex == null || !seen.Add(tex)) continue;

                string path = AssetDatabase.GetAssetPath(tex);
                if (string.IsNullOrEmpty(path)) continue;
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue; // e.g. DDS / render textures

                bool hasAlpha = importer.DoesSourceTextureHaveAlpha();
                bool isNormal = importer.textureType == TextureImporterType.NormalMap;
                float minBpp  = (hasAlpha || isNormal) ? 8f : 4f;
                float curBpp  = BppOf(tex.format);

                var recFormat = (hasAlpha || isNormal) ? TextureImporterFormat.BC7 : TextureImporterFormat.DXT1;
                float recBpp  = (hasAlpha || isNormal) ? 8f : 4f;

                bool changeFormat = curBpp > minBpp;   // not yet optimally block-compressed

                int curRes    = Mathf.Max(tex.width, tex.height);
                int targetRes = Mathf.Min(curRes, _nsOptMaxRes);
                if (_nsOptMinRes > 0) targetRes = Mathf.Max(targetRes, Mathf.Min(curRes, _nsOptMinRes));
                bool changeRes = targetRes < curRes;

                if (!changeFormat && !changeRes) continue;

                long curBytes    = TexBytes(tex, curBpp, 1f);
                float targetBpp  = changeFormat ? recBpp : curBpp;
                float scale      = changeRes ? (float)targetRes / curRes : 1f;
                long targetBytes = TexBytes(tex, targetBpp, scale);

                result.Add(new TexOpt
                {
                    Texture      = tex,
                    Path         = path,
                    CurrentRes   = curRes,
                    TargetRes    = targetRes,
                    ChangeFormat = changeFormat,
                    TargetFormat = recFormat,
                    SavedBytes   = Math.Max(0, curBytes - targetBytes)
                });
            }

            result.Sort((a, b) => b.SavedBytes.CompareTo(a.SavedBytes));
            return result;
        }

        private static void ApplyPlan(List<TexOpt> plan)
        {
            for (int i = 0; i < plan.Count; i++)
            {
                var p = plan[i];
                if (!(AssetImporter.GetAtPath(p.Path) is TextureImporter importer)) continue;

                EditorUtility.DisplayProgressBar("Optimizing textures",
                    $"{p.Texture.name}  ({i + 1}/{plan.Count})", (float)i / plan.Count);

                bool changed = false;
                if (p.TargetRes < p.CurrentRes)
                {
                    importer.maxTextureSize = p.TargetRes;
                    changed = true;
                }
                if (p.ChangeFormat)
                {
                    var pc = importer.GetPlatformTextureSettings("PC");
                    pc.overridden = true;
                    pc.format = p.TargetFormat;
                    if (p.TargetRes < p.CurrentRes) pc.maxTextureSize = p.TargetRes;
                    pc.compressionQuality = 100;
                    importer.SetPlatformTextureSettings(pc);
                    changed = true;
                }
                if (changed) importer.SaveAndReimport();
            }
            EditorUtility.ClearProgressBar();
        }

        // ============================================================
        //  Per-outfit VRAM estimate (shown in the budget counters)
        // ============================================================
        private double _vramCacheTime = -100;
        private readonly Dictionary<string, long> _vramCache = new Dictionary<string, long>();

        /// <summary>Estimated texture VRAM of everything that uploads WITH this outfit
        /// (shared body + the outfit + its included items). Cached for ~10 s.</summary>
        private long EstimateVramFor(OutfitEntry entry)
        {
            if (entry?.Go == null) return 0;
            if (EditorApplication.timeSinceStartup - _vramCacheTime > 10.0)
            {
                _vramCache.Clear();
                _vramCacheTime = EditorApplication.timeSinceStartup;
            }
            if (_vramCache.TryGetValue(entry.Name, out long cached)) return cached;

            long total = 0;
            var seen = new HashSet<Texture2D>();
            foreach (var rend in CollectUploadRenderers(entry))
            {
                foreach (var mat in rend.sharedMaterials)
                {
                    if (mat == null || mat.shader == null) continue;
                    int count = ShaderUtil.GetPropertyCount(mat.shader);
                    for (int i = 0; i < count; i++)
                    {
                        if (ShaderUtil.GetPropertyType(mat.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                            continue;
                        if (mat.GetTexture(ShaderUtil.GetPropertyName(mat.shader, i)) is Texture2D t2d && seen.Add(t2d))
                            total += TexBytes(t2d, BppOf(t2d.format), 1f);
                    }
                }
            }

            _vramCache[entry.Name] = total;
            return total;
        }

        /// <summary>All renderers that upload with this outfit: shared body (not EditorOnly),
        /// the outfit itself, and its included items — other outfits and excluded items skipped.</summary>
        private IEnumerable<Renderer> CollectUploadRenderers(OutfitEntry entry)
        {
            if (_avatarRoot == null || entry?.Go == null) yield break;
            EnsureItemsBuilt();

            Transform outfitsT = _outfitsParent != null ? _outfitsParent.transform : null;
            Transform itemsT   = _itemsParent   != null ? _itemsParent.transform   : null;

            foreach (var r in _avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                Transform tr = r.transform;
                GameObject ownerOutfit = null, ownerItem = null;
                if (outfitsT != null) { var c = DirectChildUnder(outfitsT, tr); if (c != null) ownerOutfit = c.gameObject; }
                if (ownerOutfit == null && itemsT != null) { var c = DirectChildUnder(itemsT, tr); if (c != null) ownerItem = c.gameObject; }

                if (ownerOutfit != null && ownerOutfit != entry.Go) continue;                       // another outfit
                if (ownerItem != null && !ItemIncludedFor(entry.Name, ownerItem.name)) continue;    // excluded item
                if (ownerOutfit == null && ownerItem == null && IsUnderEditorOnly(tr)) continue;    // stripped shared subtree

                yield return r;
            }
        }

        // ============================================================
        //  Texture collection
        // ============================================================
        private static IEnumerable<Texture2D> CollectOutfitTextures(GameObject outfitGo)
        {
            foreach (var rend in outfitGo.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var mat in rend.sharedMaterials)
                {
                    if (mat == null || mat.shader == null) continue;
                    int count = ShaderUtil.GetPropertyCount(mat.shader);
                    for (int i = 0; i < count; i++)
                    {
                        if (ShaderUtil.GetPropertyType(mat.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                            continue;
                        string prop = ShaderUtil.GetPropertyName(mat.shader, i);
                        if (mat.GetTexture(prop) is Texture2D t2d)
                            yield return t2d;
                    }
                }
            }
        }

        // ============================================================
        //  Size math (ported from Thry's TextureVRAM, MIT)
        // ============================================================
        private static long TexBytes(Texture t, float bpp, float resolutionScale)
        {
            int width  = (int)(t.width * resolutionScale);
            int height = (int)(t.height * resolutionScale);
            long bytes = 0;
            int mipCount = Mathf.Max(1, t.mipmapCount);
            for (int index = 0; index < mipCount; ++index)
                bytes += (long)Mathf.RoundToInt((float)((width * height) >> (2 * index)) * bpp / 8f);
            return bytes;
        }

        private static float BppOf(TextureFormat f)
        {
            switch (f)
            {
                case TextureFormat.DXT1:
                case TextureFormat.DXT1Crunched:
                case TextureFormat.BC4:
                case TextureFormat.EAC_R:
                case TextureFormat.ETC_RGB4:
                    return 4f;
                case TextureFormat.DXT5:
                case TextureFormat.DXT5Crunched:
                case TextureFormat.BC7:
                case TextureFormat.BC6H:
                case TextureFormat.BC5:
                case TextureFormat.EAC_RG:
                case TextureFormat.R8:
                case TextureFormat.Alpha8:
                    return 8f;
                case TextureFormat.RGB565:
                case TextureFormat.ARGB4444:
                case TextureFormat.RGBA4444:
                case TextureFormat.R16:
                case TextureFormat.RG16:
                case TextureFormat.RHalf:
                    return 16f;
                case TextureFormat.RGB24:
                    return 24f;
                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                case TextureFormat.BGRA32:
                case TextureFormat.RG32:
                case TextureFormat.RGHalf:
                case TextureFormat.RFloat:
                    return 32f;
                case TextureFormat.RGBAHalf:
                case TextureFormat.RGFloat:
                    return 64f;
                case TextureFormat.RGBAFloat:
                    return 128f;
                default:
                    return 32f; // assume uncompressed-ish → recommends compression
            }
        }

        // ============================================================
        //  Reporting helpers
        // ============================================================
        private static string Mib(long bytes) => (bytes / 1048576f).ToString("0.0") + " MiB";

        private static string BuildPlanSummary(string outfitName, List<TexOpt> plan, long saved)
        {
            int fmt = plan.Count(p => p.ChangeFormat);
            int res = plan.Count(p => p.TargetRes < p.CurrentRes);
            return $"Outfit '{outfitName}': {plan.Count} texture(s) can be optimized.\n" +
                   $"• Compression changes: {fmt}\n" +
                   $"• Resolution caps: {res}\n" +
                   $"• Estimated VRAM saved: ~{Mib(saved)}";
        }

        private static void LogPlan(string outfitName, List<TexOpt> plan, long saved)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[OutfitBatchUploader] Texture optimization plan for '{outfitName}' (~{Mib(saved)} saved):");
            foreach (var p in plan)
            {
                string parts = "";
                if (p.ChangeFormat) parts += $"→ {p.TargetFormat} ";
                if (p.TargetRes < p.CurrentRes) parts += $"{p.CurrentRes}→{p.TargetRes}px ";
                sb.AppendLine($"   • {p.Texture.name}: {parts}(-{Mib(p.SavedBytes)})");
            }
            Debug.Log(sb.ToString());
        }
    }
}
