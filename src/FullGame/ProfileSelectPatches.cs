using System;
using System.Text;
using CoffeeTalkAccess.Dialogue;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeTalkAccess.FullGame
{
    /// <summary>
    /// Narrates the profile-select screen - the save-slot picker retail shows at startup.
    ///
    /// ⚠ THIS IS THE FIRST INTERACTIVE SCREEN OF THE RETAIL GAME, AND IT IS THE ONE SCREEN A BLIND
    /// PLAYER CANNOT GET PAST WITHOUT THIS FILE. `TG_MainMenuManager:230` sends PRESS_ANY_KEY
    /// straight into `OpenSelectProfile(0f)`, so on retail the player meets it before the main menu
    /// even exists. The demo never shows it: `SetMainMenuButton` routes to `MainMenuDemoBuild()`
    /// while retail (no TG_ExpoBuildManager in the scene, `activeSaveLoadFeature` true) routes to
    /// `MainMenuNormalBuild()`, which is the only path that wires `changeProfileButton`.
    ///
    /// ⚠ THE TYPES EXIST IN BOTH BUILDS - verified by reflection over BOTH shipped assemblies.
    /// TG_ProfileSlotFlipUI and TG_ProfileUIManager are present in the demo DLL with identical
    /// members; only the SCENE DATA differs. So these hooks bind and attach on the demo too, they
    /// simply never fire there. That is why they are hooked by type rather than by string, unlike
    /// the mod-manager screen (whose types are genuinely absent from the demo).
    ///
    /// THE KEYBOARD BUG, and why FocusRecovery is the fix rather than anything here:
    /// `SetNavigation()` ends by calling `SelectFirstButton()`, whose whole body sits inside
    /// `if (CurrentTypeControllerState == JOYSTICK)` with NO else branch - the exact bug class
    /// FocusRecovery exists for (see its header). On a keyboard the screen opens with no selection,
    /// so the arrow keys have nothing to move and FocusNarrator has nothing to narrate: unnavigable
    /// AND silent, from one cause. `SELECT_PROFILE` is therefore added to FocusRecovery's whitelist,
    /// and this file only supplies the WORDS.
    ///
    /// ⚠ NAVIGATION IS LEFT/RIGHT, NOT UP/DOWN. `SetNavigation` assigns only `selectOnLeft` and
    /// `selectOnRight` across the three slots. Announcing "up and down" would send the player
    /// pressing keys that genuinely do nothing on this screen.
    ///
    /// ⚠ THE CARD FLIPS, AND THE SECOND FACE HAS NO NAVIGABLE BUTTONS. Activating a slot runs
    /// `OnClikButton` -> `FlipToOptionAnimation`, revealing Load and Delete. Those are NOT part of
    /// any navigation graph: `HandleAButtonCurrentSelectProfileButton` / `HandleBButton...` /
    /// `HandleXButton...` invoke them directly off `currentSelected`, gated on `GetStatusOpen()`.
    /// A sighted player sees the card turn over; on a keyboard there is no cursor movement, no focus
    /// change and no sound, so the flip is completely inaudible. The open card is announced WITH ITS
    /// KEYS for that reason - they are not discoverable by pressing arrows.
    ///
    /// ⚠ DELETE HAS NO KEYBOARD ROUTE AT ALL, and we deliberately do not add one.
    /// `TG_KeyboardHotkeyManager.HandlerKeyboard` reads only Submit/Confirm/SmartPhoneToggle/Escape
    /// (project rule 2), so nothing reaches `XButtonPressed`. Rather than invent a delete key - a
    /// destructive action, on a screen where the mod supplies the cursor - the open card says delete
    /// is gamepad-only. Stating the gap is this project's standing preference over silence, and
    /// binding an unprompted key to profile deletion is the last place to be clever.
    /// </summary>
    [HarmonyPatch]
    public static class ProfileSelectPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        /// <summary>
        /// Dedup for the flip announcement. Cleared when the screen closes, per the standing rule
        /// that an uncleared dedup turns a duplicate into permanent silence
        /// (memory: coffee-talk-dedup-on-identity).
        /// </summary>
        private static string _lastFlip;

        /// <summary>
        /// Announces the card that just flipped open, together with the keys that act on it.
        ///
        /// Hooked on the ANIMATION COMPLETION rather than on `OnClikButton`, because the buttons are
        /// not interactable until `DoFilpToOptionAnimation` finishes: it sets `button.interactable`
        /// false on entry and only restores it, along with `isOpen = true`, after two 0.3 s tweens.
        /// Announcing at click time would name a Load button that cannot yet be pressed - and would
        /// also be wrong about which face is showing.
        ///
        /// `SelectInfoButton` is the completion callback both flips pass to `FlipTo*Animation`, so
        /// it is the single point where the animation has settled and `isOpen` is final. Its own
        /// body is JOYSTICK-gated (it re-hovers for the pad), but a POSTFIX runs regardless of what
        /// the body did - which is exactly what is needed on a keyboard, where the body no-ops.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ProfileSlotFlipUI), nameof(TG_ProfileSlotFlipUI.SelectInfoButton))]
        public static void AfterSelectInfoButton(TG_ProfileSlotFlipUI __instance)
        {
            try
            {
                if (__instance == null) return;

                string line = __instance.isOpen
                    ? DescribeOpenCard(__instance)
                    : DescribeSlot(__instance);
                if (string.IsNullOrEmpty(line)) return;

                if (line == _lastFlip) return;
                _lastFlip = line;

                // interrupt:false - the focus line for this same card may still be being read, and
                // the flip is additional information about it rather than a replacement for it.
                // Same reasoning as the social-media detail pane.
                // SpeakAs(null, ...) with TextType.Menu: interface feedback, so no speaker prefix,
                // and the repeat key still stores it (Main's ShouldStoreForRepeatPredicate stores
                // everything except System) - which matters on a card that describes keys the player
                // may want repeated.
                ISpeechOutput speech = Speech;
                if (speech == null) return;

                speech.SpeakAs(null, line, TextType.Menu, false);
                MelonLogger.Msg("[Profile] " + line);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Profile] flip hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Clears the dedup when the picker closes, by either route.
        ///
        /// Both exits are hooked for the reason the calendar screen needed both of its own: leaving
        /// by one route while only the other is hooked leaves stale state armed for the next visit.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ProfileUIManager), nameof(TG_ProfileUIManager.BackToMainMenu))]
        public static void AfterBackToMainMenu() { _lastFlip = null; }

        /// <summary>Same clear, for the route taken when a profile is chosen.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ProfileUIManager), nameof(TG_ProfileUIManager.CloseProfileSelect))]
        public static void AfterCloseProfileSelect() { _lastFlip = null; }

        /// <summary>
        /// Names a focused profile slot for FocusNarrator: which slot, whose profile, and how far
        /// through the story it is.
        ///
        /// A LABELLER rather than a narrator, matching every other row screen in this mod: the
        /// EventSystem focuses the card's child `button` while the two Texts live on the
        /// TG_ProfileSlotFlipUI above it, so FocusNarrator walks the parent chain and speaks this as
        /// the control's caption. A self-speaking hook here would be cut off by the focus line a
        /// frame later.
        /// </summary>
        internal static string DescribeSlot(MonoBehaviour slot)
        {
            try
            {
                if (slot == null) return string.Empty;

                // An OPEN card is a different control with different affordances, so it is described
                // as one rather than re-read as a plain slot.
                if (ReadBool(slot, "isOpen")) return DescribeOpenCard(slot);

                StringBuilder sb = new StringBuilder();

                // "Slot 2 of 3" first: it is the one thing that orients the player on a row of three
                // otherwise interchangeable cards, and it is carried only by horizontal position.
                string position = SlotPosition(slot);
                if (!string.IsNullOrEmpty(position)) sb.Append(position);

                // An EMPTY slot shows the create-new face. Its profileNameText is not the player's
                // name (it is the "new profile" caption), so the panel's own active state is what
                // distinguishes the two - not the text, which reads plausibly either way.
                if (IsCreateNewSlot(slot))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append("empty, create new profile");
                    return sb.ToString();
                }

                string name = ReadText(slot, "profileNameText");
                if (!string.IsNullOrEmpty(name))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(Clean(name));
                }

                // The information line is the game's own localized "last played <date> <time>"
                // string, already resolved and formatted by InitProfileList. Read it back rather
                // than rebuilding it: re-deriving would mean a second copy of the date format and
                // ordinal rules, in every language.
                string info = ReadText(slot, "informationText");
                if (!string.IsNullOrEmpty(info))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(Clean(info));
                }

                // Story progress is shown ONLY as a slider fill, so it is inaudible otherwise, and
                // it is how a player tells two profiles of the same name apart.
                string progress = ProgressPercent(slot);
                if (!string.IsNullOrEmpty(progress))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(progress);
                }

                // "Currently playing" is carried by a separate panel a sighted player sees at a
                // glance, and it is the difference between resuming and starting over.
                if (IsCurrentlyPlaying(slot))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append("currently loaded");
                }

                if (sb.Length == 0) return string.Empty;

                sb.Append(". Left and right to choose, Enter to open.");
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Describes a card that has flipped to its options face, naming the keys that act on it.
        ///
        /// The keys are spoken because they are genuinely undiscoverable: Load and Delete are not in
        /// any navigation graph, so pressing arrows reveals nothing, and the flip itself makes no
        /// sound. Delete is named as gamepad-only rather than omitted - a player who knows the
        /// option exists and cannot reach it is better served than one who thinks it is missing.
        /// </summary>
        private static string DescribeOpenCard(MonoBehaviour slot)
        {
            StringBuilder sb = new StringBuilder();

            if (IsCreateNewSlot(slot))
            {
                sb.Append("Create new profile, open");
            }
            else
            {
                string name = ReadText(slot, "profileNameText");
                sb.Append(string.IsNullOrEmpty(name) ? "Profile" : Clean(name));
                sb.Append(", open");
            }

            sb.Append(". Enter to load, Escape to go back");

            // Only mention delete when it can actually happen. `HandleXButtonCurrentSelectProfileButton`
            // checks deleteProfileButton.button.interactable, and InitCreateNewProfile turns it off
            // for an empty slot - naming a disabled destructive action would be false speech.
            if (CanDelete(slot)) sb.Append(", X on a gamepad to delete");

            sb.Append('.');
            return sb.ToString();
        }

        /// <summary>
        /// "Slot 2 of 3", read from the manager's own list so it matches the order the navigation
        /// graph walks.
        ///
        /// Uses `profileSlotUIList` rather than the transform sibling index (which the music rows
        /// use) because this list is the same object `SetNavigation` iterates to build the
        /// left/right chain, so the spoken number cannot disagree with the movement. Falls back to
        /// `indexButton` ONLY as a last resort: that field is a PROFILE ID for a filled slot
        /// (`InitProfileData` sets it from `profileData.IdProfileData`) and a mere position for an
        /// empty one, so it is not reliably an index into the row.
        /// </summary>
        private static string SlotPosition(MonoBehaviour slot)
        {
            try
            {
                Type mgrType = AccessTools.TypeByName("TG_ProfileUIManager");
                if (mgrType == null) return string.Empty;

                UnityEngine.Object mgr = UnityEngine.Object.FindObjectOfType(mgrType);
                if (mgr == null) return string.Empty;

                System.Collections.IList list =
                    AccessTools.Field(mgrType, "profileSlotUIList")?.GetValue(mgr) as System.Collections.IList;
                if (list == null || list.Count == 0) return string.Empty;

                for (int i = 0; i < list.Count; i++)
                {
                    if (ReferenceEquals(list[i], slot))
                        return "Slot " + (i + 1) + " of " + list.Count;
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// True when this card is showing its "create new profile" face.
        ///
        /// Read from `createNewProfilePanel.activeSelf`, which `InitCreateNewProfile` and
        /// `InitProfileData` toggle against each other - the game's own signal, so it holds in every
        /// language, unlike matching the caption text.
        /// </summary>
        private static bool IsCreateNewSlot(MonoBehaviour slot)
        {
            GameObject panel = ReadObject<GameObject>(slot, "createNewProfilePanel");
            return panel != null && panel.activeSelf;
        }

        /// <summary>True when the "currently playing" marker panel is showing.</summary>
        private static bool IsCurrentlyPlaying(MonoBehaviour slot)
        {
            GameObject panel = ReadObject<GameObject>(slot, "currentPlayingPanel");
            return panel != null && panel.activeSelf;
        }

        /// <summary>
        /// True when deleting this profile is actually possible, mirroring the game's own guard in
        /// HandleXButtonCurrentSelectProfileButton.
        /// </summary>
        private static bool CanDelete(MonoBehaviour slot)
        {
            try
            {
                object del = AccessTools.Field(slot.GetType(), "deleteProfileButton")?.GetValue(slot);
                if (del == null) return false;

                Button b = AccessTools.Field(del.GetType(), "button")?.GetValue(del) as Button;
                return b != null && b.interactable;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Story progress as a percentage, from the slot's own Slider.
        ///
        /// Normalized via `Slider.normalizedValue` rather than `value`, so a non-zero min or a max
        /// other than 1 cannot turn a fraction into a nonsense percentage.
        /// </summary>
        private static string ProgressPercent(MonoBehaviour slot)
        {
            try
            {
                Slider s = ReadObject<Slider>(slot, "progressSlider");
                if (s == null) return string.Empty;

                return Mathf.RoundToInt(s.normalizedValue * 100f) + " percent complete";
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>Reads a private UI.Text field and returns its content.</summary>
        private static string ReadText(MonoBehaviour owner, string member)
        {
            Text t = ReadObject<Text>(owner, member);
            return t == null ? null : t.text;
        }

        /// <summary>
        /// Reads a private member by name, PROPERTY first and then field.
        ///
        /// The fields read here are all genuine FIELDS on both shipped assemblies (verified by
        /// reflection), but the order is kept because this codebase has been bitten by the opposite
        /// assumption more than once: AccessTools.Field returns null on a property WITHOUT throwing,
        /// so a field-only lookup fails silently (memory: coffee-talk-decompile-field-vs-property).
        /// </summary>
        private static T ReadObject<T>(object owner, string member) where T : class
        {
            try
            {
                if (owner == null) return null;
                Type t = owner.GetType();

                // ⚠ FIELD FIRST ON THIS SCREEN, deliberately inverting the usual order. Every
                // member read here is a real FIELD on both shipped assemblies (verified by
                // reflection), and AccessTools.Property LOGS A WARNING when it finds nothing - so
                // asking for a property first printed five "Could not find property for type
                // TG_ProfileSlotFlipUI" lines on EVERY focus move, five more per card, burying the
                // real content of the log (seen throughout 26-8-10_17-54-25).
                //
                // The property fallback is KEPT, because the field-vs-property trap is real
                // elsewhere in this codebase (memory: coffee-talk-decompile-field-vs-property) and
                // costs nothing when the field hits first. Only the ORDER changed, and only here:
                // where a member genuinely is a property, the other readers still try that first.
                System.Reflection.FieldInfo f = AccessTools.Field(t, member);
                if (f != null) return f.GetValue(owner) as T;

                System.Reflection.PropertyInfo p = AccessTools.Property(t, member);
                return p?.GetValue(owner, null) as T;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Reads a private bool field, defaulting to false when it cannot be read.</summary>
        private static bool ReadBool(object owner, string member)
        {
            try
            {
                if (owner == null) return false;
                object raw = AccessTools.Field(owner.GetType(), member)?.GetValue(owner);
                return raw is bool && (bool)raw;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Strips markup the same way every other spoken string in this mod is cleaned.</summary>
        private static string Clean(string raw)
        {
            return FungusText.ExtractWords(raw);
        }
    }
}
