// ============================================================
//  VRC Outfit Batch Uploader — budget counters (per outfit)
//  (partial class — lives alongside OutfitBatchUploader.cs)
//
//  For each outfit shows, on one line, what will be uploaded WITH
//  that outfit (the outfit's own + its included items + shared body,
//  ignoring EditorOnly and other outfits):
//     ◆ Contacts  — VRC.Dynamics.ContactBase (networked → rank, total/256)
//     ☀ Lights    — Unity Light components (avatars should have 0)
//     ⚙ Params    — expression-parameter memory / 256 (avatar-wide).
//                   If the avatar uses VRCFury, its parameter compressor
//                   handles the limit at build, so it's shown greyed.
//
//  Thresholds are the SDK's own. One scene scan per ~0.5 s buckets
//  contacts and lights by outfit / item / shared.
// ============================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ShiroTools
{
    public partial class OutfitBatchUploader
    {
        private const int CONTACT_HARD_LIMIT = 256;   // AvatarValidation.MAX_AVD_CONTACTS_PER_AVATAR

        private double _budgetCacheTime = -10;
        private bool _hasVRCFury;

        private (int net, int total) _cShared;
        private readonly Dictionary<GameObject, (int net, int total)> _cOutfit = new Dictionary<GameObject, (int, int)>();
        private readonly Dictionary<string, (int net, int total)> _cItem = new Dictionary<string, (int, int)>();

        private int _lShared;
        private readonly Dictionary<GameObject, int> _lOutfit = new Dictionary<GameObject, int>();
        private readonly Dictionary<string, int> _lItem = new Dictionary<string, int>();

        private void RecomputeBudgetsIfStale()
        {
            if (EditorApplication.timeSinceStartup - _budgetCacheTime < 0.5) return;
            _budgetCacheTime = EditorApplication.timeSinceStartup;

            _cShared = (0, 0); _cOutfit.Clear(); _cItem.Clear();
            _lShared = 0; _lOutfit.Clear(); _lItem.Clear();
            _hasVRCFury = false;
            if (_avatarRoot == null) return;

            Transform outfitsT = _outfitsParent != null ? _outfitsParent.transform : null;
            Transform itemsT   = _itemsParent   != null ? _itemsParent.transform   : null;

            foreach (var comp in _avatarRoot.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;

                if (!_hasVRCFury && IsVRCFury(comp)) _hasVRCFury = true;

                bool isContact = IsContactComponent(comp);
                bool isLight   = comp is Light;
                if (!isContact && !isLight) continue;

                Transform tr = comp.transform;
                if (IsUnderEditorOnly(tr)) continue;

                // Where does it live: a specific outfit, a specific item, or shared?
                GameObject outfitGo = null, itemGo = null;
                if (outfitsT != null) { var r = DirectChildUnder(outfitsT, tr); if (r != null) outfitGo = r.gameObject; }
                if (outfitGo == null && itemsT != null) { var r = DirectChildUnder(itemsT, tr); if (r != null) itemGo = r.gameObject; }

                if (isContact)
                {
                    int n = IsContactLocalOnly(comp) ? 0 : 1;
                    if (outfitGo != null)      { _cOutfit.TryGetValue(outfitGo, out var c); _cOutfit[outfitGo] = (c.net + n, c.total + 1); }
                    else if (itemGo != null)   { _cItem.TryGetValue(itemGo.name, out var c); _cItem[itemGo.name] = (c.net + n, c.total + 1); }
                    else                       { _cShared = (_cShared.net + n, _cShared.total + 1); }
                }
                if (isLight)
                {
                    if (outfitGo != null)      { _lOutfit.TryGetValue(outfitGo, out var c); _lOutfit[outfitGo] = c + 1; }
                    else if (itemGo != null)   { _lItem.TryGetValue(itemGo.name, out var c); _lItem[itemGo.name] = c + 1; }
                    else                       { _lShared++; }
                }
            }
        }

        private void ContactsFor(OutfitEntry outfit, out int net, out int total)
        {
            net = _cShared.net; total = _cShared.total;
            if (outfit?.Go != null && _cOutfit.TryGetValue(outfit.Go, out var oc)) { net += oc.net; total += oc.total; }
            if (_items != null && outfit != null)
                foreach (var it in _items)
                {
                    if (it.Go == null) continue;
                    if (_cItem.TryGetValue(it.Name, out var ic) && ItemIncludedFor(outfit.Name, it.Name)) { net += ic.net; total += ic.total; }
                }
        }

        private int LightsFor(OutfitEntry outfit)
        {
            int total = _lShared;
            if (outfit?.Go != null && _lOutfit.TryGetValue(outfit.Go, out var oc)) total += oc;
            if (_items != null && outfit != null)
                foreach (var it in _items)
                {
                    if (it.Go == null) continue;
                    if (_lItem.TryGetValue(it.Name, out var ic) && ItemIncludedFor(outfit.Name, it.Name)) total += ic;
                }
            return total;
        }

        private void GetParamCost(out int cost, out int max)
        {
            cost = 0;
            max = 256;
            try
            {
                int m = VRCExpressionParameters.MAX_PARAMETER_COST;
                max = (m > 9999 || m <= 0) ? 256 : m;   // some modified SDKs report a broken value
                var desc = _avatarRoot != null ? _avatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
                var ep = desc != null ? desc.expressionParameters : null;
                if (ep != null) cost = ep.CalcTotalCost();
            }
            catch { /* leave defaults */ }
        }

        private static Transform DirectChildUnder(Transform parent, Transform t)
        {
            Transform cur = t;
            while (cur != null && cur.parent != parent) cur = cur.parent;
            return cur;
        }

        // ---- type detection (reflection, no hard SDK dependency) ----
        private static bool IsContactComponent(Component c)
        {
            for (Type t = c.GetType(); t != null; t = t.BaseType)
                if (t.Name == "ContactBase") return true;
            return false;
        }

        private static bool IsVRCFury(Component c)
        {
            Type t = c.GetType();
            return t.Name == "VRCFury" || (t.Namespace != null && (t.Namespace == "VF" || t.Namespace.StartsWith("VF.")));
        }

        private static readonly Dictionary<Type, PropertyInfo> _localOnlyCache = new Dictionary<Type, PropertyInfo>();

        private static bool IsContactLocalOnly(Component c)
        {
            Type t = c.GetType();
            if (!_localOnlyCache.TryGetValue(t, out var prop))
            {
                prop = t.GetProperty("IsLocalOnly", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _localOnlyCache[t] = prop;
            }
            if (prop != null && prop.PropertyType == typeof(bool))
            {
                try { return (bool)prop.GetValue(c); } catch { }
            }
            return false;
        }

        // ============================================================
        //  Display — one line per outfit: Contacts | Lights | Params
        // ============================================================
        private static readonly Color _cGreen  = new Color(0.45f, 0.85f, 0.45f);
        private static readonly Color _cYellow = new Color(0.95f, 0.82f, 0.30f);
        private static readonly Color _cRed    = new Color(0.95f, 0.45f, 0.45f);
        private static readonly Color _cGray   = new Color(0.62f, 0.62f, 0.62f);

        private void DrawContactCounter(OutfitEntry entry)
        {
            RecomputeBudgetsIfStale();
            EnsureItemsBuilt();

            ContactsFor(entry, out int net, out int total);
            int lights = LightsFor(entry);
            GetParamCost(out int pCost, out int pMax);
            bool pc = GetCurrentPlatform() == VRCPlatform.Windows;

            using (new EditorGUILayout.HorizontalScope())
            {
                // --- Contacts ---
                int exc = pc ? 8 : 2, good = pc ? 16 : 4, med = pc ? 24 : 8, poor = pc ? 32 : 16;
                string crank; Color ccol;
                if (net <= good)      { crank = net <= exc ? "Excellent" : "Good"; ccol = _cGreen; }
                else if (net <= poor) { crank = net <= med ? "Medium" : "Poor";    ccol = _cYellow; }
                else                  { crank = "Very Poor";                       ccol = _cRed; }
                bool overHard = total >= CONTACT_HARD_LIMIT;
                if (overHard) ccol = _cRed;
                ColoredLabel($"◆ Contacts {net} ({crank}) · {total}/{CONTACT_HARD_LIMIT}{(overHard ? " ⚠" : "")}",
                    $"Networked contacts set the rank ({(pc ? "PC" : "Quest")}: Excellent ≤{exc}, Good ≤{good}, Medium ≤{med}, Poor ≤{poor}). " +
                    $"Local-only contacts (e.g. SPS senders) don't count. Hard cap {CONTACT_HARD_LIMIT}.", ccol);

                GUILayout.Space(12);

                // --- Lights ---
                Color lcol = lights == 0 ? _cGreen : (pc && lights == 1 ? _cYellow : _cRed);
                ColoredLabel($"☀ Lights {lights}",
                    "Avatars should have 0 realtime lights (PC: 1 = Poor, 2+ = Very Poor; Quest: any light = Very Poor).", lcol);

                GUILayout.Space(12);

                // --- Parameters (avatar-wide) ---
                if (_hasVRCFury)
                    ColoredLabel($"⚙ Params {pCost} (VRCFury)",
                        "VRCFury compresses synced parameters at build, so the 256-bit limit is effectively handled. " +
                        "The editor value is the pre-compression cost.", _cGray);
                else
                {
                    Color pcol = pCost >= pMax ? _cRed : (pCost >= pMax * 3 / 4 ? _cYellow : _cGreen);
                    ColoredLabel($"⚙ Params {pCost}/{pMax}",
                        "Expression-parameter memory (avatar-wide). Max 256 bits.", pcol);
                }

                GUILayout.FlexibleSpace();
            }
        }

        private static void ColoredLabel(string text, string tooltip, Color col)
        {
            var old = GUI.color;
            GUI.color = col;
            GUILayout.Label(new GUIContent(text, tooltip), EditorStyles.miniBoldLabel);
            GUI.color = old;
        }
    }
}
