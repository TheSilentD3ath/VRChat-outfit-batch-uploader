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
        private const string NS_OPT_ITEMS   = "ShiroNewOutfit_OptItems";
        private const string NS_OPT_BODY    = "ShiroNewOutfit_OptBody";

        // ---- Runtime defaults ----
        private bool _optLoaded;
        private bool _nsOptEnabled;     // run automatically during Express
        private bool _nsOptAsk = true;  // ask before applying
        private int  _nsOptMaxRes = 2048;
        private int  _nsOptMinRes = 0;  // never reduce a texture below this (0 = no floor)
        private bool _nsOptItems;        // also optimize the outfit's INCLUDED items (accessories), opt-in
        private bool _nsOptBody;         // also optimize the SHARED body textures, opt-in (affects every outfit)

        private void EnsureOptDefaults()
        {
            if (_optLoaded) return;
            _optLoaded = true;
            _nsOptEnabled = EditorPrefs.GetBool(NS_OPT_ENABLED, false);
            _nsOptAsk     = EditorPrefs.GetBool(NS_OPT_ASK, true);
            _nsOptMaxRes  = EditorPrefs.GetInt(NS_OPT_MAXRES, 2048);
            _nsOptMinRes  = EditorPrefs.GetInt(NS_OPT_MINRES, 0);
            _nsOptItems   = EditorPrefs.GetBool(NS_OPT_ITEMS, false);
            _nsOptBody    = EditorPrefs.GetBool(NS_OPT_BODY, false);
        }

        private void SaveOptDefaults()
        {
            EditorPrefs.SetBool(NS_OPT_ENABLED, _nsOptEnabled);
            EditorPrefs.SetBool(NS_OPT_ASK, _nsOptAsk);
            EditorPrefs.SetInt(NS_OPT_MAXRES, Mathf.Clamp(_nsOptMaxRes, 32, 8192));
            EditorPrefs.SetInt(NS_OPT_MINRES, Mathf.Clamp(_nsOptMinRes, 0, 8192));
            EditorPrefs.SetBool(NS_OPT_ITEMS, _nsOptItems);
            EditorPrefs.SetBool(NS_OPT_BODY, _nsOptBody);
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

            _nsOptItems = EditorGUILayout.ToggleLeft(
                "Also optimize the outfit's selected items (accessories)", _nsOptItems);
            _nsOptBody = EditorGUILayout.ToggleLeft(
                new GUIContent("Also optimize shared body textures",
                    "The body / base mesh is usually the largest share of the VRAM estimate, but its " +
                    "textures are used by EVERY outfit — optimizing them from one outfit changes all " +
                    "of them. Off by default, and a plan that includes the body is always confirmed."),
                _nsOptBody);

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

            var plan = BuildOptimizationPlan(entry, out int itemsInc, out bool bodyInc);
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
                ScopeNote(itemsInc, bodyInc) +
                "\n\nThis changes the textures' import settings and is NOT undo-able. Continue?",
                "Optimize", "Cancel");
            if (!ok) { SetStatus("Texture optimization cancelled.", MessageType.Warning); return; }

            ApplyPlan(plan);
            ClearVramCache();
            SetStatus($"✓ Optimized {plan.Count} texture(s) for '{entry.Name}' — saved ~{Mib(saved)}.", MessageType.Info);
        }

        /// <summary>Called from the Express flow. Honours the "enabled" / "ask" defaults.</summary>
        private void MaybeOptimizeDuringExpress(OutfitEntry entry)
        {
            EnsureOptDefaults();
            if (!_nsOptEnabled || entry?.Go == null) return;

            var plan = BuildOptimizationPlan(entry, out int itemsInc, out bool bodyInc);
            if (plan.Count == 0) return;

            long saved = plan.Sum(p => p.SavedBytes);
            LogPlan(entry.Name, plan, saved);

            // A plan reaching the shared body is confirmed even when "don't ask again" is set:
            // it changes textures belonging to every outfit, which the project rules require to
            // be explicitly confirmed and spelled out every time.
            if (_nsOptAsk || bodyInc)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Optimize textures (VRAM)",
                    BuildPlanSummary(entry.Name, plan, saved) +
                    ScopeNote(itemsInc, bodyInc) +
                    "\n\nThis changes texture import settings and is NOT undo-able.",
                    "Optimize now", "Skip", "Always (don't ask again)");

                if (choice == 1) return;                 // Skip
                if (choice == 2) { _nsOptAsk = false; SaveOptDefaults(); }  // Always
            }

            ApplyPlan(plan);
            ClearVramCache();
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

        /// <summary>Plan for an outfit: its own textures, plus — each only if enabled in the
        /// defaults — the textures of its selected items and of the shared body. The body is the
        /// usual reason the VRAM counter barely moves after optimizing, because it is normally the
        /// largest share; it stays opt-in because its textures belong to EVERY outfit.</summary>
        private List<TexOpt> BuildOptimizationPlan(OutfitEntry entry, out int itemsIncluded, out bool bodyIncluded)
        {
            itemsIncluded = 0;
            bodyIncluded  = false;
            var roots = new List<GameObject> { entry.Go };

            if (_nsOptItems)
            {
                EnsureItemsBuilt();
                if (_items != null)
                    foreach (var it in _items)
                        if (it.Go != null && ItemIncludedFor(entry.Name, it.Name))
                        {
                            roots.Add(it.Go);
                            itemsIncluded++;
                        }
            }

            IEnumerable<Texture2D> textures = roots.SelectMany(CollectOutfitTextures);

            if (_nsOptBody)
            {
                var body = new List<Renderer>(); var outfit = new List<Renderer>(); var items = new List<Renderer>();
                CollectUploadRenderersByBucket(entry, body, outfit, items);
                if (body.Count > 0)
                {
                    bodyIncluded = true;
                    textures = textures.Concat(CollectTextures(body));
                }
            }

            return BuildOptimizationPlan(textures);
        }

        private List<TexOpt> BuildOptimizationPlan(IEnumerable<Texture2D> textures)
        {
            var result = new List<TexOpt>();
            var seen = new HashSet<Texture2D>();

            foreach (var tex in textures)
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
            try
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
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ============================================================
        //  Per-outfit VRAM estimate (shown in the budget counters)
        //
        //  Perf notes: values are computed OUTSIDE OnGUI by a throttled
        //  EditorApplication.update pump (one outfit per ~150 ms), so
        //  scrolling stays perfectly smooth — pending rows just show "…".
        //  Results are cached until the hierarchy changes. Textures are
        //  enumerated via GetTexturePropertyNames (ShaderUtil property
        //  iteration is very slow on big shaders like Poiyomi/lilToon).
        // ============================================================
        /// <summary>Per-bucket VRAM of one outfit. A texture referenced by more than one bucket is
        /// counted ONCE, in the first bucket that reaches it — body before outfit before items. So
        /// the three numbers always add up to Total, and a shared texture is attributed to the
        /// broadest scope that would have to be touched to save it.</summary>
        internal struct VramSplit
        {
            public long Body;
            public long Outfit;
            public long Items;
            public long Total => Body + Outfit + Items;
        }

        private readonly Dictionary<string, VramSplit> _vramCache = new Dictionary<string, VramSplit>();
        private bool _vramPumpActive;
        private double _nextVramTick;

        internal void ClearVramCache() => _vramCache.Clear();

        internal void StopVramPump()
        {
            _vramPumpActive = false;
            EditorApplication.update -= VramPumpTick;
        }

        /// <summary>Estimated texture VRAM of everything that uploads WITH this outfit, split into
        /// shared body / outfit / items. Returns false while the value is still being computed in
        /// the background.</summary>
        private bool TryGetVramFor(OutfitEntry entry, out VramSplit split)
        {
            split = default;
            if (entry?.Go == null) return true;
            if (_vramCache.TryGetValue(entry.Name, out split)) return true;
            EnsureVramPump();
            return false;
        }

        private void EnsureVramPump()
        {
            if (_vramPumpActive) return;
            _vramPumpActive = true;
            EditorApplication.update -= VramPumpTick;
            EditorApplication.update += VramPumpTick;
        }

        private void VramPumpTick()
        {
            if (_scrollAnimActive) return;   // never do heavy work mid-glide — resume after
            if (EditorApplication.timeSinceStartup < _nextVramTick) return;
            _nextVramTick = EditorApplication.timeSinceStartup + 0.15;

            OutfitEntry missing = null;
            foreach (var o in _outfits)
                if (o?.Go != null && !_vramCache.ContainsKey(o.Name)) { missing = o; break; }

            if (missing == null)
            {
                StopVramPump();
                Repaint();
                return;
            }

            try { ComputeVramFor(missing); }
            catch (Exception ex)
            {
                _vramCache[missing.Name] = default;   // never let the pump die
                Debug.LogWarning($"[OutfitBatchUploader] VRAM estimate failed for '{missing.Name}': {ex.Message}");
            }
            Repaint();
        }

        private void ComputeVramFor(OutfitEntry entry)
        {
            var body = new List<Renderer>(); var outfit = new List<Renderer>(); var items = new List<Renderer>();
            CollectUploadRenderersByBucket(entry, body, outfit, items);

            var seen = new HashSet<Texture2D>();
            long Measure(List<Renderer> renderers)
            {
                long n = 0;
                foreach (var t2d in CollectTextures(renderers))
                    if (t2d != null && seen.Add(t2d)) n += TexBytes(t2d, BppOf(t2d.format), 1f);
                return n;
            }

            // Evaluation order IS the attribution rule for shared textures — see VramSplit.
            var split = new VramSplit();
            split.Body   = Measure(body);
            split.Outfit = Measure(outfit);
            split.Items  = Measure(items);
            _vramCache[entry.Name] = split;
        }

        /// <summary>Splits everything that uploads with this outfit into its three buckets in one
        /// pass, so the VRAM counter, the optimization plan and the dry run can never classify a
        /// renderer differently. "Shared body" is everything under the avatar that sits in neither
        /// the Outfits nor the Items parent — the base mesh, hair, and anything else every outfit
        /// carries. Other outfits and excluded items are dropped entirely.</summary>
        private void CollectUploadRenderersByBucket(OutfitEntry entry,
            List<Renderer> body, List<Renderer> outfit, List<Renderer> items)
        {
            if (_avatarRoot == null || entry?.Go == null) return;
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

                if (ownerOutfit != null)    outfit.Add(r);
                else if (ownerItem != null) items.Add(r);
                else                        body.Add(r);
            }
        }

        /// <summary>All renderers that upload with this outfit, body first — see
        /// CollectUploadRenderersByBucket for the classification.</summary>
        private IEnumerable<Renderer> CollectUploadRenderers(OutfitEntry entry)
        {
            var body = new List<Renderer>(); var outfit = new List<Renderer>(); var items = new List<Renderer>();
            CollectUploadRenderersByBucket(entry, body, outfit, items);
            return body.Concat(outfit).Concat(items);
        }

        // ============================================================
        //  Texture collection
        // ============================================================
        /// <summary>Every texture referenced by these renderers, with duplicates left in —
        /// callers dedupe, because which bucket a shared texture lands in is their decision.
        /// Uses Material.GetTexturePropertyNames rather than walking ShaderUtil's property list,
        /// which is very slow on large shaders (Poiyomi/lilToon).</summary>
        private static IEnumerable<Texture2D> CollectTextures(IEnumerable<Renderer> renderers)
        {
            foreach (var rend in renderers)
            {
                if (rend == null) continue;
                foreach (var mat in rend.sharedMaterials)
                {
                    if (mat == null || mat.shader == null) continue;
                    foreach (var prop in mat.GetTexturePropertyNames())
                        if (mat.GetTexture(prop) is Texture2D t2d)
                            yield return t2d;
                }
            }
        }

        private static IEnumerable<Texture2D> CollectOutfitTextures(GameObject outfitGo) =>
            CollectTextures(outfitGo.GetComponentsInChildren<Renderer>(true));

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

        /// <summary>The extra scopes a plan covers. The body line is deliberately blunt: per the
        /// project rules a destructive texture change that reaches shared textures has to say so.</summary>
        private static string ScopeNote(int itemsIncluded, bool bodyIncluded)
        {
            string note = "";
            if (itemsIncluded > 0)
                note += $"\n• Includes the textures of {itemsIncluded} selected item(s)";
            if (bodyIncluded)
                note += "\n• Includes the SHARED BODY textures — they are used by EVERY outfit, " +
                        "so this changes all of them, not just this one";
            return note;
        }

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
