// ============================================================
//  VRC Outfit Batch Uploader — Items (accessories) module
//  (partial class — lives alongside OutfitBatchUploader.cs)
//
//  A second, configurable parent (default "Items") holds accessory
//  objects (props, weapons, jewelry, …). Inclusion is chosen
//  PER OUTFIT: each outfit decides which items upload with it.
//     • included → tag "Untagged"   (uploaded)
//     • excluded → tag "EditorOnly" (stripped at build, not uploaded)
//
//  A global "included on every outfit" default per item name lives
//  in the New Outfit Defaults section and seeds each outfit's choice
//  until you override it for that outfit.
//
//  Item states are applied from ActivateOutfit(target), so Select /
//  Upload / Express / Batch all use the active outfit's selection.
// ============================================================

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShiroTools
{
    public partial class OutfitBatchUploader
    {
        private const string ITEMS_PARENT_NAME   = "ShiroItems_ParentName";
        private const string DEFAULT_ITEMS_PARENT = "Items";
        // (Per-outfit / default include states live in OutfitProjectData —
        //  legacy "ShiroItem_*" / "ShiroItemDefault_*" EditorPrefs are migrated there on first read.)

        private class ItemEntry
        {
            public GameObject Go;
            public string Name;
        }

        private string _itemsParentName;
        private GameObject _itemsParent;
        private List<ItemEntry> _items;
        private GameObject _itemsAvatar;       // which avatar _items was built for
        private readonly Dictionary<string, bool> _outfitItemsExpanded = new Dictionary<string, bool>();
        private readonly Dictionary<string, string> _outfitItemsSearch = new Dictionary<string, string>();
        private readonly Dictionary<string, Vector2> _outfitItemsScroll = new Dictionary<string, Vector2>();
        private string _itemDefaultsSearch = "";
        private Vector2 _itemDefaultsScroll;

        // ============================================================
        //  Build
        // ============================================================
        private void EnsureItemsBuilt()
        {
            if (_itemsParentName == null)
                _itemsParentName = EditorPrefs.GetString(ITEMS_PARENT_NAME, DEFAULT_ITEMS_PARENT);
            if (_items == null || _itemsAvatar != _avatarRoot)
                RebuildItemList();
        }

        private void RebuildItemList()
        {
            _items = new List<ItemEntry>();
            _itemsParent = null;
            _itemsAvatar = _avatarRoot;

            if (_itemsParentName == null)
                _itemsParentName = EditorPrefs.GetString(ITEMS_PARENT_NAME, DEFAULT_ITEMS_PARENT);
            if (_avatarRoot == null) return;

            var t = FindDeepChild(_avatarRoot.transform, _itemsParentName);
            if (t == null) return;
            _itemsParent = t.gameObject;

            foreach (Transform child in _itemsParent.transform)
                _items.Add(new ItemEntry { Go = child.gameObject, Name = child.gameObject.name });
        }

        // ============================================================
        //  Per-outfit include state (project-local JSON, survives plugin updates;
        //  legacy EditorPrefs values are migrated on first read)
        // ============================================================
        private string ItemAvatarKey => _avatarRoot != null ? _avatarRoot.name : "";

        private bool ItemDefaultOn(string itemName) =>
            OutfitProjectData.GetItemDefault(ItemAvatarKey, itemName);

        /// <summary>Whether the item uploads with this outfit (per-outfit override, else the every-outfit default).</summary>
        private bool ItemIncludedFor(string outfitName, string itemName) =>
            OutfitProjectData.GetItemIncluded(ItemAvatarKey, outfitName, itemName);

        private void SetItemIncluded(string outfitName, string itemName, bool include)
        {
            OutfitProjectData.SetItemIncluded(ItemAvatarKey, outfitName, itemName, include);
            ClearVramCache();
            MarkBudgetsDirty();
        }

        private void SetItemsIncluded(string outfitName, IEnumerable<string> itemNames, bool include)
        {
            OutfitProjectData.SetItemsIncluded(ItemAvatarKey, outfitName, itemNames, include);
            ClearVramCache();
            MarkBudgetsDirty();
        }

        // ============================================================
        //  Apply (called from ActivateOutfit with the active outfit)
        // ============================================================
        private void ApplyItemStates(OutfitEntry target)
        {
            EnsureItemsBuilt();
            if (_items == null || target == null) return;

            foreach (var it in _items)
            {
                if (it.Go == null) continue;
                string wantTag = ItemIncludedFor(target.Name, it.Name) ? "Untagged" : "EditorOnly";
                if (it.Go.tag != wantTag)
                {
                    Undo.RecordObject(it.Go, "Set item upload state");
                    it.Go.tag = wantTag;
                    EditorUtility.SetDirty(it.Go);
                }
            }
        }

        // ============================================================
        //  Per-outfit Items UI (drawn inside each outfit row)
        // ============================================================
        private void DrawOutfitItems(OutfitEntry entry)
        {
            EnsureItemsBuilt();
            if (_itemsParent == null || _items.Count == 0) return;

            int inc = _items.Count(it => it.Go != null && ItemIncludedFor(entry.Name, it.Name));

            bool exp = _outfitItemsExpanded.TryGetValue(entry.Name, out var e) && e;
            exp = EditorGUILayout.Foldout(exp, $"Items  ({inc}/{_items.Count} uploaded with this outfit)", true);
            _outfitItemsExpanded[entry.Name] = exp;
            if (!exp) return;

            string search = _outfitItemsSearch.TryGetValue(entry.Name, out var sv) ? sv : "";

            // Search row
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14);
                EditorGUILayout.LabelField("Search", GUILayout.Width(46));
                EditorGUI.BeginChangeCheck();
                string ns = EditorGUILayout.TextField(search);
                if (EditorGUI.EndChangeCheck()) { _outfitItemsSearch[entry.Name] = ns; search = ns; }
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
                { _outfitItemsSearch[entry.Name] = ""; search = ""; GUI.FocusControl(null); }
            }

            string filter = (search ?? "").ToLowerInvariant();
            var filtered = _items.Where(it => it.Go != null &&
                (filter.Length == 0 || it.Name.ToLowerInvariant().Contains(filter))).ToList();

            // All / None (apply to the currently filtered items) + shown count
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14);
                if (GUILayout.Button("All", EditorStyles.miniButton, GUILayout.Width(36)))
                    SetItemsIncluded(entry.Name, filtered.Select(it => it.Name), true);
                if (GUILayout.Button("None", EditorStyles.miniButton, GUILayout.Width(40)))
                    SetItemsIncluded(entry.Name, filtered.Select(it => it.Name), false);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"{filtered.Count} shown", EditorStyles.miniLabel, GUILayout.Width(70));
            }

            // Scrollable list (height capped so very long lists don't overflow)
            Vector2 sp = _outfitItemsScroll.TryGetValue(entry.Name, out var v) ? v : Vector2.zero;
            float height = Mathf.Clamp(filtered.Count * 20f + 4f, 24f, 220f);
            sp = EditorGUILayout.BeginScrollView(sp, GUILayout.Height(height));
            foreach (var it in filtered)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(4);
                    bool cur = ItemIncludedFor(entry.Name, it.Name);
                    EditorGUI.BeginChangeCheck();
                    bool nv = EditorGUILayout.ToggleLeft(it.Name, cur);
                    if (EditorGUI.EndChangeCheck())
                        SetItemIncluded(entry.Name, it.Name, nv);

                    if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        EditorGUIUtility.PingObject(it.Go);
                        Selection.activeGameObject = it.Go;
                    }
                }
            }
            EditorGUILayout.EndScrollView();
            RegisterNestedScrollRect();
            _outfitItemsScroll[entry.Name] = sp;
        }

        // ============================================================
        //  Item defaults (in the New Outfit Defaults section)
        //  = items that upload on EVERY outfit unless overridden.
        // ============================================================
        private void DrawItemDefaultsConfig()
        {
            EnsureItemsBuilt();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Items (accessories)", EditorStyles.miniBoldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Items parent", GUILayout.Width(110));
                EditorGUI.BeginChangeCheck();
                string newName = EditorGUILayout.TextField(_itemsParentName);
                if (EditorGUI.EndChangeCheck())
                {
                    _itemsParentName = newName;
                    EditorPrefs.SetString(ITEMS_PARENT_NAME, _itemsParentName);
                    RebuildItemList();
                }
            }

            if (_itemsParent == null || _items == null || _items.Count == 0)
            {
                EditorGUILayout.LabelField(
                    $"No items found under \"{_itemsParentName}\".", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField(
                "Included on every outfit by default (toggle off per outfit as needed):",
                EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Search", GUILayout.Width(46));
                EditorGUI.BeginChangeCheck();
                string ns = EditorGUILayout.TextField(_itemDefaultsSearch);
                if (EditorGUI.EndChangeCheck()) _itemDefaultsSearch = ns;
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
                { _itemDefaultsSearch = ""; GUI.FocusControl(null); }
            }

            string filter = (_itemDefaultsSearch ?? "").ToLowerInvariant();
            var filtered = _items.Where(it =>
                filter.Length == 0 || it.Name.ToLowerInvariant().Contains(filter)).ToList();

            float height = Mathf.Clamp(filtered.Count * 20f + 4f, 24f, 200f);
            _itemDefaultsScroll = EditorGUILayout.BeginScrollView(_itemDefaultsScroll, GUILayout.Height(height));
            foreach (var it in filtered)
            {
                EditorGUI.BeginChangeCheck();
                bool def = EditorGUILayout.ToggleLeft(it.Name, ItemDefaultOn(it.Name));
                if (EditorGUI.EndChangeCheck())
                {
                    OutfitProjectData.SetItemDefault(ItemAvatarKey, it.Name, def);
                    ClearVramCache();
                    MarkBudgetsDirty();
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
