// ============================================================
//  VRC Outfit Batch Uploader — First-run onboarding
//  (partial class — lives alongside OutfitBatchUploader.cs)
//
//  Who sees it: only people who have never used the tool. On the
//  first open on a machine, a project where some outfit already has
//  a Blueprint ID or an upload (or an upload log exists) marks the
//  person as experienced — they never get the card or the tips, not
//  after an update and not in a later, empty project.
//
//  What it is: a card in the window, not a wizard dialog. It sits
//  where a new user used to meet a bare "no Outfits parent" warning.
//  Every step reads the real scene state and ticks itself off, so
//  people learn by doing the setup rather than reading about it —
//  step 2 can even create the empty Outfits / Items objects.
//  Skippable, and restartable from the ? in the header.
//
//  Afterwards, one-time tips (Items, Batch, VRAM) appear next to the
//  part of the window they explain, each dismissed on its own.
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShiroTools
{
    public partial class OutfitBatchUploader
    {
        // Per machine (EditorPrefs), not per project: "has this person used the tool?"
        private const string PREFS_ONBOARDING = "ShiroOutfitUploader_Onboarding";
        private const string PREFS_TIP_PREFIX = "ShiroOutfitUploader_Tip_";

        private const int ONB_UNKNOWN     = 0;   // not decided yet — decided on first open
        private const int ONB_CARD        = 1;   // new user, card showing
        private const int ONB_TIPS        = 2;   // new user, card done or skipped — tips still show
        private const int ONB_EXPERIENCED = 3;   // used the tool before — no card, no tips

        private static readonly string[] TIP_IDS = { "items", "batch", "vram" };

        private int _onboardingState;
        private readonly Dictionary<string, bool> _tipDismissed = new Dictionary<string, bool>();
        private readonly HashSet<string> _tipsDrawnThisPass = new HashSet<string>();

        private bool OnboardingCardVisible => _onboardingState == ONB_CARD;

        /// <summary>Called from OnEnable. Decides once per machine whether this is a new user.</summary>
        private void InitOnboardingState()
        {
            _onboardingState = EditorPrefs.GetInt(PREFS_ONBOARDING, ONB_UNKNOWN);
            if (_onboardingState != ONB_UNKNOWN) return;

            // Opening the window alone creates outfit records, so "has records" proves nothing —
            // a Blueprint ID, a recorded upload or the upload log does.
            bool usedBefore = OutfitProjectData.HasUsageHistory() || System.IO.File.Exists(UPLOAD_LOG_PATH);
            SetOnboardingState(usedBefore ? ONB_EXPERIENCED : ONB_CARD);
        }

        private void SetOnboardingState(int state)
        {
            _onboardingState = state;
            EditorPrefs.SetInt(PREFS_ONBOARDING, state);
        }

        /// <summary>The ? in the header: show the card again, and the tips after it.</summary>
        private void RestartOnboarding()
        {
            foreach (var id in TIP_IDS) EditorPrefs.DeleteKey(PREFS_TIP_PREFIX + id);
            _tipDismissed.Clear();
            SetOnboardingState(ONB_CARD);
            RefreshForOnboarding();
        }

        /// <summary>While the card is up, its steps have to tick off as people drop an avatar or an
        /// outfit into the Hierarchy. Outside onboarding the list stays manual (↺), as before.</summary>
        private void RefreshForOnboarding()
        {
            if (_avatarRoot == null) ScanScene();
            else                     RebuildOutfitList();
            Repaint();
        }

        // IMGUI draws each event twice (layout, then input/repaint). An action that changes what
        // the window shows must wait until the pass is over, or the two passes disagree.
        private void AfterGui(Action action)
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;   // window closed in between
                action();
                Repaint();
            };
        }

        // ============================================================
        //  Card
        // ============================================================
        private void DrawOnboardingCard()
        {
            bool s1 = _avatarRoot != null;
            bool s2 = s1 && _outfitsParent != null;
            bool s3 = s2 && _outfits.Count > 0;
            bool s4 = s3 && _outfits.Any(o => o != null && !string.IsNullOrWhiteSpace(o.BlueprintId));
            int current = !s1 ? 1 : !s2 ? 2 : !s3 ? 3 : !s4 ? 4 : 5;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Space(4);
                EditorGUILayout.LabelField(
                    current == 5 ? "You're set up" : "Welcome — four steps to your first upload",
                    EditorStyles.boldLabel);
                GUILayout.Space(4);

                DrawOnboardingStep(1, current, "Choose your avatar",
                    s1 ? $"Avatar: {_avatarRoot.name}" : null, DrawStepAvatar);
                DrawOnboardingStep(2, current, "Add a place for outfits and items",
                    s2 ? $"Outfits object: \"{_outfitsParent.name}\"" : null, DrawStepFolders);
                DrawOnboardingStep(3, current, "Add your first outfit",
                    s3 ? $"{_outfits.Count} outfit(s) found" : null, DrawStepFirstOutfit);
                DrawOnboardingStep(4, current, "Connect it to VRChat",
                    s4 ? "Blueprint ID saved" : null, DrawStepUpload);

                GUILayout.Space(4);
                if (current == 5)
                {
                    EditorGUILayout.LabelField(
                        "From now on, tick the outfits you changed and press Upload in Batch Upload at the " +
                        "bottom — they go up one after another. Press ? at the top to see this again.",
                        EditorStyles.wordWrappedLabel);
                    GUILayout.Space(4);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Done", GUILayout.Width(80), GUILayout.Height(24)))
                            AfterGui(() => SetOnboardingState(ONB_TIPS));
                    }
                }
                else
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(new GUIContent("Skip Introduction",
                                "Hide this guide. It won't come back on its own — the ? at the top brings it back."),
                                EditorStyles.miniButton))
                            AfterGui(() => SetOnboardingState(ONB_TIPS));
                    }
                }
                GUILayout.Space(2);
            }
        }

        private void DrawOnboardingStep(int n, int current, string title, string doneSummary, Action body)
        {
            bool done   = n < current;
            bool active = n == current;

            using (new EditorGUILayout.HorizontalScope())
            {
                // Shape, not only colour, carries the state: ✓ done, ● now, ○ later.
                var oldColor = GUI.contentColor;
                GUI.contentColor = done ? _cGreen : active ? oldColor : _cGray;
                GUILayout.Label(done ? "✓" : active ? "●" : "○", GUILayout.Width(16));
                GUI.contentColor = done || active ? oldColor : _cGray;
                GUILayout.Label(done && doneSummary != null ? $"{n}  {doneSummary}" : $"{n}  {title}",
                    active ? EditorStyles.boldLabel : EditorStyles.label);
                GUI.contentColor = oldColor;
            }

            if (!active) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(22);
                using (new EditorGUILayout.VerticalScope())
                {
                    body();
                    GUILayout.Space(6);
                }
            }
        }

        private void DrawStepAvatar()
        {
            EditorGUILayout.LabelField(
                _avatarsInScene.Count == 0
                    ? "No avatar in the open scene. Open the scene with your avatar — it needs a " +
                      "VRC Avatar Descriptor. This step ticks itself off once it's there."
                    : _avatarsInScene.Count > 1
                        ? "There are several avatars in this scene. Pick yours under Quick pick above."
                        : "Drag your avatar into Avatar root above.",
                EditorStyles.wordWrappedLabel);
        }

        private void DrawStepFolders()
        {
            string itemsName = ItemsParentNameForSetup();
            EditorGUILayout.LabelField(
                $"Outfits go in one empty object under your avatar, accessories in a second one. " +
                $"Every child of \"{_outfitsParentName}\" becomes one upload.",
                EditorStyles.wordWrappedLabel);
            GUILayout.Space(4);
            if (GUILayout.Button($"Create \"{_outfitsParentName}\" and \"{itemsName}\"", GUILayout.Height(24)))
                AfterGui(CreateSetupFolders);
            EditorGUILayout.LabelField(
                "Already have such an object under another name? Type that name into Outfits parent above.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawStepFirstOutfit()
        {
            EditorGUILayout.LabelField(
                $"Drag an outfit — the clothing object — onto \"{_outfitsParent.name}\" in the Hierarchy. " +
                "Accessories you want to switch per outfit go under the items object.",
                EditorStyles.wordWrappedLabel);
            GUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button($"Show \"{_outfitsParent.name}\" in Hierarchy", GUILayout.Height(24)))
                {
                    Selection.activeGameObject = _outfitsParent;
                    EditorGUIUtility.PingObject(_outfitsParent);
                }
                // Someone who brought their own Outfits object may still lack the items one.
                EnsureItemsBuilt();
                if (_itemsParent == null)
                {
                    string itemsName = ItemsParentNameForSetup();
                    if (GUILayout.Button($"Create \"{itemsName}\"", GUILayout.Height(24)))
                        AfterGui(CreateSetupFolders);
                }
            }
        }

        private void DrawStepUpload()
        {
            EditorGUILayout.LabelField(
                "Not on VRChat yet? Open New Outfit and use Express — it uploads the outfit as a new " +
                "avatar and saves its Blueprint ID. Uploaded before? Paste its Blueprint ID (avtr_…) " +
                "into the outfit's row below.",
                EditorStyles.wordWrappedLabel);
            GUILayout.Space(4);
            using (new EditorGUI.DisabledScope(_mainPage == 1))
            {
                if (GUILayout.Button("Go to New Outfit", GUILayout.Height(24)))
                    AfterGui(() => _mainPage = 1);
            }
        }

        // ============================================================
        //  Creating the empty objects
        // ============================================================
        private string ItemsParentNameForSetup() =>
            _itemsParentName ?? EditorPrefs.GetString(ITEMS_PARENT_NAME, DEFAULT_ITEMS_PARENT);

        /// <summary>Creates whichever of the outfits / items objects is missing, as one undo step.</summary>
        private void CreateSetupFolders()
        {
            if (_avatarRoot == null) return;

            Undo.SetCurrentGroupName("Create outfit and item objects");
            int group = Undo.GetCurrentGroup();

            if (FindDeepChild(_avatarRoot.transform, _outfitsParentName) == null)
                CreateEmptyChild(_outfitsParentName);
            string itemsName = ItemsParentNameForSetup();
            if (FindDeepChild(_avatarRoot.transform, itemsName) == null)
                CreateEmptyChild(itemsName);

            Undo.CollapseUndoOperations(group);
            RebuildOutfitList();
            _items = null;   // EnsureItemsBuilt picks the new items object up
        }

        private void CreateEmptyChild(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_avatarRoot.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        // ============================================================
        //  One-time tips
        // ============================================================
        /// <summary>A small hint next to the part of the window it explains. New users only, after the
        /// card; each tip is dismissed on its own and drawn at most once per GUI pass.</summary>
        private void DrawTip(string id, string text)
        {
            if (_onboardingState != ONB_TIPS) return;
            if (!_tipsDrawnThisPass.Add(id)) return;
            if (!_tipDismissed.TryGetValue(id, out bool dismissed))
                _tipDismissed[id] = dismissed = EditorPrefs.GetBool(PREFS_TIP_PREFIX + id, false);
            if (dismissed) return;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Got It", EditorStyles.miniButton, GUILayout.Width(52)))
                {
                    EditorPrefs.SetBool(PREFS_TIP_PREFIX + id, true);
                    _tipDismissed[id] = true;
                }
            }
        }
    }
}
