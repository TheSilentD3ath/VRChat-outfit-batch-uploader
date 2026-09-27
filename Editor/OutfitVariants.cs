// ============================================================
//  VRC Outfit Batch Uploader — Outfit variants
//  (partial class — lives alongside OutfitBatchUploader.cs)
//
//  A variant uploads the SAME outfit object again as its own
//  avatar with a different item (accessory) selection — e.g.
//  "Black Dress – With Bag" next to "Black Dress".
//
//  Own per variant:   name, item selection, Blueprint ID,
//                     batch tick, upload history.
//  From the base:     blendshapes, FaceEmo, build platforms —
//                     change them on the base and every variant
//                     follows, so a variant can't go stale.
//
//  Storage: an ordinary outfit record in ShiroOutfit_data.json
//  with baseOutfit / variantName set, named "<base> – <variant>".
//  Everything keyed by outfit name (items, Blueprint ID, Express
//  naming via {outfit}, the batch queue) works on it unchanged.
// ============================================================

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShiroTools
{
    public partial class OutfitBatchUploader
    {
        private const string VARIANT_SEPARATOR = " – ";

        // Which of several entries sharing one outfit object was activated last (Select/Upload).
        [SerializeField] private string _activatedName;
        // True while the New Outfit page draws its list — variants appear there without their base.
        private bool _listSetupOnly;
        private readonly Dictionary<string, string> _newVariantDraft = new Dictionary<string, string>();

        private GUIStyle _activeVariantRowStyle;
        private GUIStyle _inactiveVariantRowStyle;

        private void ResetVariantUiState()
        {
            _newVariantDraft.Clear();
            _activatedName = null;
        }

        private static string VariantRecordName(string baseName, string variantName) =>
            baseName + VARIANT_SEPARATOR + variantName.Trim();

        private bool HasVariants(OutfitEntry entry) =>
            !entry.IsVariant && _outfits.Any(o => o != null && o.IsVariant && o.Go == entry.Go);

        /// <summary>The "Active" badge. The object's tag says whether the outfit is on; when variants
        /// share that object, only the entry activated last wears the badge (the base by default).</summary>
        private bool IsShownActive(OutfitEntry entry)
        {
            if (entry.Go == null || !entry.Go.CompareTag("Untagged")) return false;
            if (!entry.IsVariant && !HasVariants(entry)) return true;

            bool activatedSharesObject = _activatedName != null &&
                _outfits.Any(o => o != null && o.Name == _activatedName && o.Go == entry.Go);
            return entry.Name == (activatedSharesObject ? _activatedName : entry.SettingsName);
        }

        private string RowLabel(OutfitEntry entry)
        {
            if (!entry.IsVariant) return entry.Name;
            // Under its base the short name reads best; alone (New Outfit page) it needs the full one.
            return _listSetupOnly ? entry.Name : "↳ " + entry.VariantName;
        }

        private GUIStyle RowStyleFor(OutfitEntry entry, bool isActive)
        {
            if (!entry.IsVariant || _listSetupOnly) return isActive ? _activeRowStyle : _inactiveRowStyle;

            if (_activeVariantRowStyle == null)
            {
                // Indent through the margin, not a wrapper: the motion-blur ghosts read the card's own rect.
                _activeVariantRowStyle   = new GUIStyle(_activeRowStyle)   { margin = new RectOffset(18, 0, 0, 0) };
                _inactiveVariantRowStyle = new GUIStyle(_inactiveRowStyle) { margin = new RectOffset(18, 0, 0, 0) };
            }
            return isActive ? _activeVariantRowStyle : _inactiveVariantRowStyle;
        }

        /// <summary>Base platform toggles changed — variants build for the same platforms.</summary>
        private void SyncVariantPlatforms(OutfitEntry baseEntry)
        {
            foreach (var v in _outfits.Where(o => o != null && o.IsVariant && o.Go == baseEntry.Go))
            {
                v.BuildWindows = baseEntry.BuildWindows;
                v.BuildAndroid = baseEntry.BuildAndroid;
                v.BuildIOS     = baseEntry.BuildIOS;
                if (v.Data != null)
                {
                    v.Data.buildWindows = baseEntry.BuildWindows;
                    v.Data.buildAndroid = baseEntry.BuildAndroid;
                    v.Data.buildIOS     = baseEntry.BuildIOS;
                }
            }
        }

        // ============================================================
        //  Row parts
        // ============================================================
        /// <summary>Base outfit's details: add a variant.</summary>
        private void DrawAddVariantRow(OutfitEntry baseEntry)
        {
            _newVariantDraft.TryGetValue(baseEntry.Name, out string draft);
            string error = ValidateVariantName(baseEntry, draft);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("New variant:",
                    "Upload this outfit again as its own avatar with a different item selection. " +
                    "Blendshapes, FaceEmo and platforms stay shared with this outfit."),
                    GUILayout.Width(82));
                _newVariantDraft[baseEntry.Name] = EditorGUILayout.TextField(draft ?? "");
                using (new EditorGUI.DisabledScope(error != null || _isBatchUploading || _isExpressBusy))
                {
                    if (GUILayout.Button(new GUIContent("Add Variant", error ?? ""), GUILayout.Width(90)))
                    {
                        string name = draft.Trim();
                        AfterGui(() => AddVariant(baseEntry, name));
                    }
                }
            }
            if (error != null && !string.IsNullOrWhiteSpace(draft))
                EditorGUILayout.LabelField(error, EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>Variant's details: what it shares, and a way to remove it.</summary>
        private void DrawVariantFooter(OutfitEntry variant)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    $"Blendshapes, FaceEmo and platforms come from \"{variant.BaseName}\".",
                    EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(_isBatchUploading || _isExpressBusy))
                {
                    if (GUILayout.Button("Remove Variant", EditorStyles.miniButton, GUILayout.Width(100)))
                        AfterGui(() => RemoveVariant(variant));
                }
            }
        }

        /// <summary>Null when the name can be used, otherwise why not.</summary>
        private string ValidateVariantName(OutfitEntry baseEntry, string draft)
        {
            if (string.IsNullOrWhiteSpace(draft)) return "Enter a name for the variant.";
            if (_avatarRoot == null) return "No avatar selected.";

            string record = VariantRecordName(baseEntry.Name, draft);
            bool clashesWithOutfit = _outfitsParent != null &&
                _outfitsParent.transform.Cast<Transform>().Any(t => t.name == record);
            if (clashesWithOutfit || OutfitProjectData.HasOutfit(_avatarRoot.name, record))
                return $"\"{record}\" already exists — pick another name.";
            return null;
        }

        // ============================================================
        //  Add / remove
        // ============================================================
        private void AddVariant(OutfitEntry baseEntry, string variantName)
        {
            if (_avatarRoot == null || ValidateVariantName(baseEntry, variantName) != null) return;

            string avatar = _avatarRoot.name;
            string record = VariantRecordName(baseEntry.Name, variantName);
            OutfitProjectData.AddVariant(avatar, baseEntry.Name, variantName.Trim(), record);

            // Start from what the base uploads with, so only the difference needs ticking.
            EnsureItemsBuilt();
            if (_items != null && _items.Count > 0)
            {
                var names = _items.Where(it => it.Go != null).Select(it => it.Name).ToList();
                OutfitProjectData.SetItemsIncluded(avatar, record,
                    names.Where(n => ItemIncludedFor(baseEntry.Name, n)), true);
                OutfitProjectData.SetItemsIncluded(avatar, record,
                    names.Where(n => !ItemIncludedFor(baseEntry.Name, n)), false);
            }

            _newVariantDraft.Remove(baseEntry.Name);
            RebuildOutfitList();

            // Open it with its items showing — choosing them is the next thing to do.
            var added = _outfits.FirstOrDefault(o => o.Name == record);
            if (added != null) added.DetailsExpanded = true;
            _outfitItemsExpanded[record] = true;
            MarkBudgetsDirty();
            SetStatus($"Added variant \"{record}\". Tick its items, then upload it as a new avatar with Express.",
                MessageType.Info);
        }

        private void RemoveVariant(OutfitEntry variant)
        {
            if (_avatarRoot == null || !variant.IsVariant) return;
            bool ok = EditorUtility.DisplayDialog(
                "Remove variant",
                $"Remove \"{variant.Name}\" from this tool?\n\n" +
                "Its item selection and saved Blueprint ID are forgotten here. " +
                "The avatar on VRChat is not deleted.",
                "Remove", "Cancel");
            if (!ok) return;

            OutfitProjectData.RemoveOutfit(_avatarRoot.name, variant.Name);
            if (_activatedName == variant.Name) _activatedName = null;
            RebuildOutfitList();
            MarkBudgetsDirty();
            SetStatus($"Removed variant \"{variant.Name}\".", MessageType.Info);
        }
    }
}
