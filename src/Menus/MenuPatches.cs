using System;
using System.Reflection;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeTalkAccess.Menus
{
    /// <summary>
    /// Speaks Coffee Talk's menus by observing the game's OWN cursor.
    ///
    /// THE KEY DISCOVERY (2026-08-08, after three silent live runs): Coffee Talk is ALREADY fully
    /// keyboard/gamepad navigable. TG_ControllerInputManager implements a complete menu system -
    /// ButtonUp/ButtonDown/SelectButtonPressed dispatched across ~20 screen states (MAIN_MENU,
    /// OPTION_MAIN_MENU, BREWING, PHONE_*, LOAD_GAME, ...), and TG_MainMenuManager keeps its own
    /// `cursorIdx` into `buttonList`, skipping inactive entries and wrapping correctly.
    ///
    /// It just never touches Unity's EventSystem. That is why an EventSystem watcher saw
    /// focused=NULL on the main menu and heard nothing: there was real, working navigation
    /// happening that simply did not go through the channel we were listening to.
    ///
    /// So we hook the game's own "cursor landed on this button" call (MouseOverManager) instead of
    /// providing or overriding navigation. The player uses the game's real controls; we narrate.
    /// This also fixes the ordering bug from the position-sorted fallback, which announced
    /// "Button Exit, 1 of 3" - the game's buttonList order is the authored menu order.
    /// </summary>
    [HarmonyPatch]
    public static class MenuPatches
    {
        private static string _lastSpoken;

        // The last ENTRY announced, and whether it was selectable. Deduping on the button rather
        // than on the spoken sentence - the two announcement paths word the same button
        // differently, so a text-only comparison misses the duplicate. See Announce().
        private static string _lastObject;
        private static bool _lastAvailable = true;

        /// <summary>
        /// True while MouseOverManager is running. It calls buttonList[idx].MouseHover()
        /// SYNCHRONOUSLY, so our MouseHover hook fires nested inside it for the very same move.
        /// A re-entrancy flag suppresses exactly that nested call and nothing else, which is
        /// tighter than a global "who is driving" mode.
        ///
        /// ⚠ It is NOT sufficient on its own. It only covers the SAME-FRAME nested call; a
        /// physical mouse resting over the menu makes Unity re-fire hover tens of milliseconds
        /// later, outside the flag's lifetime. That produced a real double announcement live
        /// (23:21:36.699 / .744). The per-entry dedup in Announce() is what catches that case,
        /// which is why the earlier claim here - that hover should still announce "even a moment
        /// later on the same button" - has been withdrawn: for a blind player the second, less
        /// informative announcement is noise, not confirmation.
        /// </summary>
        private static bool MouseOverActive;

        /// <summary>
        /// The button-level highlight event, and the one that actually covers every screen.
        ///
        /// MouseOverManager (below) is only reached from TG_MainMenuManager.ButtonUp/ButtonDown,
        /// i.e. ONLY while currentState == MAIN_MENU. The live run showed it never firing, because
        /// options / extras / load-game / profile screens route through their own managers. But
        /// EVERY one of those paths ends at TG_MainMenuButton.MouseHover() - it is what draws the
        /// selection arrow - so hooking the button itself narrates all of them with one patch.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_MainMenuButton), nameof(TG_MainMenuButton.MouseHover))]
        public static void AfterMouseHover(TG_MainMenuButton __instance)
        {
            try
            {
                if (__instance == null) return;

                // Keyboard and gamepad moves on the main menu reach MouseHover only THROUGH
                // MouseOverManager, which announces them with a position ("2 of 5"). Announcing
                // here as well would say the same move twice, once without the position. So this
                // hook exists for the paths MouseOverManager does NOT cover - notably mouse hover,
                // and the option/extras/load-game panels, which drive TG_MainMenuButton directly
                // through their own managers.
                if (MouseOverActive) return;

                // NOTE: MouseHover() itself early-returns when button.interactable is false, so
                // this hook never fires for a dimmed entry. We deliberately do NOT re-check
                // interactable here - see AfterMouseOverManager, which is the path that has to
                // handle dimmed entries, because the game's cursor CAN land on them.
                string label = ReadCaption(__instance);
                if (string.IsNullOrEmpty(label)) label = __instance.gameObject.name;

                // menuIdx is the button's own position within its panel, maintained by
                // TG_MainMenuButtonPanel.RefreshIndexButton().
                Announce(label, __instance.menuIdx, CountSiblings(__instance), __instance.gameObject.name, true);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Menu] MouseHover hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Counts the interactable buttons in this button's own panel, so the spoken position
        /// ("2 of 5") matches the list the player is actually moving through.
        /// </summary>
        private static int CountSiblings(TG_MainMenuButton entry)
        {
            try
            {
                Transform parent = entry.transform.parent;
                if (parent == null) return 0;

                int n = 0;
                for (int i = 0; i < parent.childCount; i++)
                {
                    GameObject child = parent.GetChild(i).gameObject;
                    if (!child.activeInHierarchy) continue;
                    if (child.GetComponent<TG_MainMenuButton>() != null) n++;
                }
                return n;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Fires whenever the main menu's cursor highlights an entry - by keyboard, gamepad, or
        /// mouse hover. `menuIdx` is the index into TG_MainMenuManager.buttonList.
        /// </summary>
        /// <summary>
        /// Raises the re-entrancy flag BEFORE MouseOverManager runs, because the MouseHover call
        /// we need to suppress happens inside it - by the time the postfix runs, that nested
        /// announcement would already have been spoken.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(TG_MainMenuManager), nameof(TG_MainMenuManager.MouseOverManager))]
        public static void BeforeMouseOverManager()
        {
            MouseOverActive = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_MainMenuManager), nameof(TG_MainMenuManager.MouseOverManager))]
        public static void AfterMouseOverManager(TG_MainMenuManager __instance, int menuIdx)
        {
            try
            {
                object listObj = AccessTools.Field(typeof(TG_MainMenuManager), "buttonList")?.GetValue(__instance);
                System.Collections.IList buttonList = listObj as System.Collections.IList;
                if (buttonList == null || buttonList.Count == 0) return;

                // MouseOverManager(-1) means "re-highlight wherever the cursor already is", so
                // resolve the real index from the manager's own cursorIdx.
                int idx = menuIdx;
                if (idx < 0)
                {
                    object cur = AccessTools.Field(typeof(TG_MainMenuManager), "cursorIdx")?.GetValue(__instance);
                    idx = cur == null ? -1 : Convert.ToInt32(cur);
                }
                if (idx < 0 || idx >= buttonList.Count) return;

                MonoBehaviour entry = buttonList[idx] as MonoBehaviour;
                if (entry == null) return;

                string label = ReadCaption(entry);
                if (string.IsNullOrEmpty(label)) label = entry.gameObject.name;

                // Say so when the cursor lands somewhere that cannot be chosen.
                //
                // ButtonUp/ButtonDown skip entries on gameObject.activeSelf, NOT on interactable,
                // so the cursor genuinely stops on active-but-dimmed buttons. TG_MainMenuButton.
                // MouseHover() wraps its whole body in `if (button.interactable)`, so on those
                // entries the game draws no arrow, plays no sound, and our MouseHover hook never
                // fires - the cursor moves and NOTHING is announced. That silence is
                // indistinguishable from a dead key, which is the worst outcome for a blind
                // player. Announcing "unavailable" turns it into information, and is strictly
                // more than a sighted player gets from a dimmed label.
                bool available = true;
                Button b = AccessTools.Field(entry.GetType(), "button")?.GetValue(entry) as Button;
                if (b != null) available = b.interactable;

                Announce(label, idx, buttonList.Count, entry.gameObject.name, available);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Menu] MouseOverManager hook threw: " + e.Message);
            }
            finally
            {
                // MUST be in a finally: the body above has several early returns, and any path
                // that left this flag raised would silence mouse hover for the rest of the
                // session - a permanent, silent regression from a transient failure.
                MouseOverActive = false;
            }
        }

        /// <summary>
        /// Reads a TG_MainMenuButton's caption from its `textComponentToMove` field - the game
        /// keeps the label there (I2.Loc writes the localized string onto it), and it is not
        /// reliably a child of the button object, so a hierarchy scan misses it.
        /// </summary>
        private static string ReadCaption(MonoBehaviour entry)
        {
            FieldInfo f = entry.GetType().GetField("textComponentToMove");
            if (f == null) return string.Empty;

            Text label = f.GetValue(entry) as Text;
            if (label == null || string.IsNullOrEmpty(label.text)) return string.Empty;

            return label.text.Trim();
        }

        private static void Announce(string label, int idx, int count, string objectName, bool available)
        {
            // Suppress a second announcement of the SAME ENTRY, even when the two paths word it
            // differently.
            //
            // Live, 26-8-9_23-20-27.log:
            //   23:21:36.699  OPTIONS, 2 of 2      <- MouseOverManager (keyboard move)
            //   23:21:36.744  OPTIONS              <- MouseHover, 45 ms later
            // Not the nested call MouseOverActive already guards (that one is same-frame): this is
            // Unity re-firing pointer hover because the physical mouse happened to be resting over
            // the button the keyboard cursor moved onto. The text dedup below could not catch it,
            // because the two paths built DIFFERENT strings for one button - MouseHover's
            // CountSiblings returned 0, so it dropped the ", 2 of 2" and the strings no longer
            // matched. Keying on the object name compares the thing itself rather than the
            // sentence about it.
            //
            // Deliberately NOT time-based: a real move back to the same entry must still be
            // announced, and that can happen within any window.
            if (objectName == _lastObject && available == _lastAvailable) return;
            _lastObject = objectName;
            _lastAvailable = available;

            // Only claim a position when both parts are trustworthy. A bogus "1 of 0" is worse
            // than no position at all.
            string spoken = (count > 0 && idx >= 0 && idx < count)
                ? label + ", " + (idx + 1) + " of " + count
                : label;
            if (!available) spoken += ", unavailable";
            if (FocusNarrator.IsDestructive(objectName)) spoken += ", warning, this quits or erases";

            if (spoken == _lastSpoken) return;
            _lastSpoken = spoken;

            ISpeechOutput speech = AccessMod.Speech;
            if (speech == null || !speech.IsAvailable) return;

            speech.SpeakAs(null, spoken, TextType.Menu, true);
            MelonLogger.Msg("[Menu] " + spoken);
        }

        /// <summary>
        /// Forgets which entry was last announced, so the next landing on the main menu is spoken
        /// even if the cursor comes back to the very same button.
        ///
        /// Both dedups are "same as last time, stay quiet" - correct within one screen, wrong
        /// across a screen change. Answer No to the confirmation popup while sitting on PLAY GAME
        /// and the cursor returns to PLAY GAME: object AND text both match, and the menu would come
        /// back silent, leaving the player with no evidence of where they are. That is the
        /// "stale focus strands the player" rule from project_status.md, in dedup form.
        ///
        /// Called when a popup closes rather than on every state change: a popup is the one thing
        /// that reliably interrupts the menu and hands the cursor straight back to where it was.
        /// </summary>
        internal static void ForgetLastAnnouncement()
        {
            _lastSpoken = null;
            _lastObject = null;
            _lastAvailable = true;
        }
    }
}
