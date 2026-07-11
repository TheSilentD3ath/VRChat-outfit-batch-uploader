// ============================================================
//  VRC Outfit Batch Uploader — per-outfit FaceEmo integration
//  (partial class — lives alongside OutfitBatchUploader.cs)
//
//  FaceEmo (jp.suzuryg.face-emo, MIT) generates a single Modular-
//  Avatar object named "FaceEmoPrefab" under the avatar root; at
//  build, Modular Avatar merges its face-expression menu/animator.
//  FaceEmo natively supports only ONE such config per avatar.
//
//  This makes it PER OUTFIT via "capture + tag-swap":
//    • Capture: after you Generate in FaceEmo, the freshly created
//      "FaceEmoPrefab" is renamed to "FaceEmo__<outfit>" and stored
//      for that outfit. The next Generate makes a fresh FaceEmoPrefab
//      you capture for the next outfit.
//    • On activation (Select/Upload/Express/Batch), the active
//      outfit's FaceEmo object is set Untagged (uploaded) and all
//      other captured ones EditorOnly (stripped) — so each outfit
//      uploads only its own face expressions.
//
//  No FaceEmo types are referenced (only the object name + a menu
//  item string), so this compiles even without FaceEmo installed.
// ============================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShiroTools
{
    public partial class OutfitBatchUploader
    {
        private const string FACEEMO_PREFAB_NAME = "FaceEmoPrefab"; // FaceEmo's AV3Constants.MARootObjectName
        private const string FACEEMO_MENU_NEW    = "FaceEmo/New Menu";
        // (Captured object names live in OutfitProjectData — legacy "ShiroFaceEmo_*"
        //  EditorPrefs are migrated there on first read.)

        private readonly Dictionary<string, bool> _faceEmoExpanded = new Dictionary<string, bool>();

        private string GetFaceEmoName(string outfitName) =>
            OutfitProjectData.GetFaceEmoName(_avatarRoot != null ? _avatarRoot.name : "", outfitName);

        private void SetFaceEmoName(string outfitName, string value) =>
            OutfitProjectData.SetFaceEmoName(_avatarRoot != null ? _avatarRoot.name : "", outfitName, value);

        private GameObject FindAvatarChild(string name)
        {
            if (_avatarRoot == null || string.IsNullOrEmpty(name)) return null;
            var t = FindDeepChild(_avatarRoot.transform, name);
            return t != null ? t.gameObject : null;
        }

        // ============================================================
        //  Apply (called from ActivateOutfit with the active outfit)
        // ============================================================
        private void ApplyFaceEmoStates(OutfitEntry target)
        {
            if (_avatarRoot == null || _outfits == null) return;

            foreach (var o in _outfits)
            {
                if (o == null) continue;
                string nm = GetFaceEmoName(o.Name);
                if (string.IsNullOrEmpty(nm)) continue;

                var go = FindAvatarChild(nm);
                if (go == null) continue;

                bool active = (o == target);
                string wantTag = active ? "Untagged" : "EditorOnly";
                if (go.tag != wantTag)
                {
                    Undo.RecordObject(go, "Set FaceEmo upload state");
                    go.tag = wantTag;
                    EditorUtility.SetDirty(go);
                }
                if (active && !go.activeSelf)
                {
                    Undo.RecordObject(go, "Activate FaceEmo");
                    go.SetActive(true);
                    EditorUtility.SetDirty(go);
                }
            }
        }

        // ============================================================
        //  Capture / clear
        // ============================================================
        private void CaptureFaceEmoFor(OutfitEntry entry)
        {
            if (_avatarRoot == null) { SetStatus("No avatar selected.", MessageType.Error); return; }

            var src = FindAvatarChild(FACEEMO_PREFAB_NAME);
            if (src == null)
            {
                SetStatus("No freshly generated 'FaceEmoPrefab' found. In FaceEmo: configure your expressions, " +
                          "click Generate, then Capture here.", MessageType.Warning);
                return;
            }

            string newName = "FaceEmo__" + entry.Name;

            // Remove a previous capture for this outfit (same target name) to avoid duplicates
            var existing = FindAvatarChild(newName);
            if (existing != null && existing != src)
                Undo.DestroyObjectImmediate(existing);

            Undo.RecordObject(src, "Capture FaceEmo");
            src.name = newName;
            EditorUtility.SetDirty(src);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            SetFaceEmoName(entry.Name, newName);
            SetStatus($"✓ Captured FaceEmo for '{entry.Name}'  →  {newName}", MessageType.Info);
        }

        private void ClearFaceEmoFor(OutfitEntry entry)
        {
            SetFaceEmoName(entry.Name, "");
            SetStatus($"Cleared FaceEmo assignment for '{entry.Name}' (the object was left in the scene).", MessageType.Info);
        }

        private static void OpenFaceEmoWindow()
        {
            if (!EditorApplication.ExecuteMenuItem(FACEEMO_MENU_NEW))
                Debug.LogWarning("[OutfitBatchUploader] Could not open FaceEmo (menu '" + FACEEMO_MENU_NEW +
                                 "' not found — is FaceEmo installed?).");
        }

        // ============================================================
        //  Per-outfit FaceEmo UI (drawn inside each outfit row)
        // ============================================================
        private void DrawOutfitFaceEmo(OutfitEntry entry)
        {
            string assigned = GetFaceEmoName(entry.Name);

            bool exp = _faceEmoExpanded.TryGetValue(entry.Name, out var e) && e;
            exp = EditorGUILayout.Foldout(exp,
                string.IsNullOrEmpty(assigned) ? "FaceEmo  (none)" : $"FaceEmo  ({assigned})", true);
            _faceEmoExpanded[entry.Name] = exp;
            if (!exp) return;

            GameObject assignedGo = FindAvatarChild(assigned);
            GameObject stray = FindAvatarChild(FACEEMO_PREFAB_NAME);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14);
                if (GUILayout.Button("Open FaceEmo", EditorStyles.miniButton, GUILayout.Width(100)))
                    OpenFaceEmoWindow();

                using (new EditorGUI.DisabledScope(stray == null))
                {
                    var old = GUI.backgroundColor;
                    if (stray != null) GUI.backgroundColor = new Color(0.45f, 0.75f, 1f);
                    if (GUILayout.Button("Capture", EditorStyles.miniButton, GUILayout.Width(70)))
                        CaptureFaceEmoFor(entry);
                    GUI.backgroundColor = old;
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(assigned)))
                {
                    if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(54)))
                        ClearFaceEmoFor(entry);
                    if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(44)) && assignedGo != null)
                    {
                        EditorGUIUtility.PingObject(assignedGo);
                        Selection.activeGameObject = assignedGo;
                    }
                }
                GUILayout.FlexibleSpace();
            }

            if (!string.IsNullOrEmpty(assigned) && assignedGo == null)
                EditorGUILayout.HelpBox($"Assigned FaceEmo object \"{assigned}\" is not in the scene anymore.", MessageType.Warning);
            else
                EditorGUILayout.LabelField(
                    "In FaceEmo: build this outfit's expressions and click Generate, then press Capture. " +
                    "On upload only the active outfit's FaceEmo is included.",
                    EditorStyles.wordWrappedMiniLabel);

            if (stray != null)
                EditorGUILayout.LabelField(
                    "• An uncaptured \"FaceEmoPrefab\" exists — Capture it for an outfit, or delete it so it isn't merged on every outfit.",
                    EditorStyles.wordWrappedMiniLabel);
        }
    }
}
