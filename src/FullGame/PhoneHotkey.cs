using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace CoffeeTalkAccess.FullGame
{
    /// <summary>
    /// Supplies the missing Tab -> open smartphone trigger in the café.
    ///
    /// THE GAP (traced by decompile 2026-08-11, reported live as "Tab should move focus to the
    /// smartphone"). Tab IS bound to the phone by the game - KeyboardPlayerActions:53 binds Key.Tab
    /// to SmartPhoneToggle, and TG_KeyboardHotkeyManager.SmartPhoneToggle (:202) opens the phone in
    /// BREWING and IN_DIALOGUE. That handler is correct and we do not replace it. The problem is
    /// that it is never REACHED:
    ///
    ///   ControllerUpdateFunction (TG_ControllerInputManager:265-275)
    ///       if (currentTypeController == JOYSTICK)  HandlerControllerPress();
    ///       else                                    keyboardHotkeyManager.HandlerKeyboard();
    ///
    /// SmartPhoneToggle hangs off HandlerKeyboard, i.e. off the ELSE branch only. This mod's
    /// navigation depends on the game sitting in JOYSTICK mode, and that mode is also what a
    /// connected pad forces: CheckActiveController reads axis LEVELS rather than press edges, so it
    /// flips back to JOYSTICK continuously. KeyboardNav:76-85 records a whole live session that
    /// logged `mode = JOYSTICK` at EVERY sample. In that mode HandlerKeyboard never runs, so Tab
    /// never reaches SmartPhoneToggle and the phone cannot be opened from the keyboard at all.
    ///
    /// ⚠ THE ASYMMETRY IS THE TELL, and it is why this looked like a partial failure rather than a
    /// dead key: Tab CLOSES the phone perfectly well. The phone's own Tab reads
    /// (TG_SmartPhoneManager:146, TG_SmartPhoneApps:52) are raw Input.GetKeyDown inside
    /// UpdateFunction, which bypass the action set entirely and therefore bypass the mode gate too.
    /// Only the OPEN direction goes through HandlerKeyboard. Do not "fix" the close direction.
    ///
    /// This is the same class of defect as the satellite buttons and the brewing cursor: the game
    /// has a WORKING implementation gated behind an input mode the keyboard cannot hold. The fix is
    /// to supply the missing trigger and call the game's own handler - never to rebuild the screen.
    /// See MainMenuHotkeys and BrewingPatches.AfterSetIngredientsButton.
    ///
    /// WHAT WE DELIBERATELY DO NOT DO:
    ///  - We do not re-implement SmartPhoneToggle's state/scene matrix. It branches on BREWING vs
    ///    IN_DIALOGUE, on InGameScene/InGameDemoScene vs EndlessModeScene (a different manager owns
    ///    the phone in endless mode), and on comicPanelActive. Copying that would be a second copy
    ///    of the game's rules, free to drift. We invoke the game's method and inherit all of it.
    ///  - We do not check canOpenSmartPhone here, and more generally we do not decide in advance
    ///    whether the phone may open. OpenSmartPhone guards itself (:228) on four conditions, three
    ///    of them transient, and the story can also block the phone per-scene via
    ///    TG_ToggleBlockSmartPhoneCommand; a refusal is the game's intent, not a fault. We call and
    ///    then MEASURE, rather than reimplementing the matrix - 0.9.2 predicted it and shipped a
    ///    key that worked exactly once per brew. SmartPhonePatches already SAYS "Smartphone
    ///    unavailable right now" on the blocked branch, so the player hears a reason, not silence.
    ///  - We do not announce or move focus. OpenSmartPhone only STARTS a 0.6s tween; the screen is
    ///    not live until its OnComplete. PhoneEntryWatcher already owns the announcement and the
    ///    keyboard selection, for reasons written up in SmartPhonePatches - announcing here would
    ///    describe a screen that does not exist yet.
    /// </summary>
    [HarmonyPatch]
    public static class PhoneHotkey
    {
        /// <summary>
        /// Tab, matching the game's own binding rather than inventing a key. The player already
        /// uses Tab to close the phone, so open/close stay symmetrical.
        ///
        /// ⚠ TAB IS CLAIMED ON OTHER SCREENS and the separation is by STATE, not by key:
        /// MainMenuHotkeys reads Tab only in MAIN_MENU, ModMenuPatches only in MOD_MENU, and this
        /// only in BREWING/IN_DIALOGUE. One press can therefore match at most one of them. If any
        /// of those gates is relaxed they WILL fight - check all three before touching any.
        /// </summary>
        private const KeyCode PhoneKey = KeyCode.Tab;

        /// <summary>
        /// Hosted on ControllerUpdateFunction, not on HandlerKeyboard - hosting on HandlerKeyboard
        /// would reintroduce the exact mode dependency this class exists to work around.
        /// ControllerUpdateFunction runs unconditionally from Update() in BOTH modes.
        ///
        /// Read with Input.GetKeyDown rather than by binding onto the joystick action set. Binding
        /// would route Tab through HandlerControllerPress, whose X-button router dispatches on many
        /// screens - opening the phone is not idempotent, so we gate it ourselves instead.
        ///
        /// ⚠ MUST NOT DOUBLE-FIRE. In KEYBOARD mode the game DOES reach SmartPhoneToggle on its
        /// own, and a second open on the same frame would toggle the phone straight back shut. The
        /// mode check below is what keeps this to exactly one trigger per press; it is load-bearing,
        /// not defensive.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ControllerInputManager), "ControllerUpdateFunction")]
        public static void AfterControllerUpdate(TG_ControllerInputManager __instance)
        {
            try
            {
                if (__instance == null) return;
                if (!Input.GetKeyDown(PhoneKey)) return;

                // In KEYBOARD mode the game already ran HandlerKeyboard this frame and has handled
                // Tab itself. Acting again would open and immediately re-close the phone.
                object mode = AccessTools.Field(__instance.GetType(), "currentTypeController")
                    ?.GetValue(__instance);
                if (mode == null || mode.ToString() != "JOYSTICK") return;

                object state = AccessTools.Field(__instance.GetType(), "currentState")
                    ?.GetValue(__instance);
                string s = state != null ? state.ToString() : null;

                // ⚠ VIEWING_GLASS IS THE SERVE-OPTIONS STEP OF MAKING A DRINK, and reaching it is
                // NOT rare - it is where you land after closing the phone mid-brew. OpenSmartPhone
                // saves the state it was opened from (:234) and CloseSmartPhone hands it back via
                // ChangeToPreviousState (TG_GameManager:463), so once the drink flow has advanced
                // into serve options, EVERY later press arrives here rather than in BREWING.
                //
                // ⚠ DO NOT SPEAK A REFUSAL FROM THIS BRANCH ON THE WAY IN. 0.9.2 did, and it turned
                // a working key into a permanently stuck one: reported live as "it only fixes it the
                // first time - if I close the phone and re-open it the focus goes back to the drink
                // options". The message was correct about the state and wrong about the situation,
                // and being CONFIDENTLY wrong is worse than silence, because it tells the player to
                // stop pressing.
                //
                // The deeper mistake was predicting the game's answer instead of measuring it.
                // OpenSmartPhone (:228) refuses on FOUR conditions - TWEENING, VIEWING_GLASS,
                // !ingameButtonInteractable and isTweening - and three of those are transient. Any
                // copy of that matrix here is a second set of rules free to drift from the first.
                // So we no longer decide: we call the game's handler and then look at whether the
                // phone actually opened. See [[measure-dont-predict]].

                // Gate on the states SmartPhoneToggle acts in, PLUS VIEWING_GLASS, which we now let
                // through to the call so the game itself can accept or refuse it. This keeps us off
                // MAIN_MENU and MOD_MENU, where Tab means something else entirely.
                if (s != "BREWING" && s != "IN_DIALOGUE" && s != "VIEWING_GLASS") return;

                // The game's own handler, with the game's own scene and comic-panel branches.
                object hotkeys = AccessTools.Field(__instance.GetType(), "keyboardHotkeyManager")
                    ?.GetValue(__instance);
                if (hotkeys == null) return;

                MelonLogger.Msg("[Hotkey] Tab -> smartphone (" + s + ")");
                AccessTools.Method(hotkeys.GetType(), "SmartPhoneToggle")?.Invoke(hotkeys, null);

                // MEASURE THE RESULT. OpenSmartPhone changes state to TWEENING before starting its
                // 0.6s tween, so a state that MOVED means the open was accepted; a state still
                // sitting where it was means one of the four guards refused. Read it after the
                // call, never before - a proxy read on the way in reported "fine" every time.
                //
                // We only speak on the refusal, and only from VIEWING_GLASS, where the player is
                // holding a glass and has a reason to hear one. PhoneEntryWatcher still owns the
                // announcement for the accepted case; saying anything here would describe a screen
                // that is still 0.6s from existing.
                object after = AccessTools.Field(__instance.GetType(), "currentState")
                    ?.GetValue(__instance);
                string t = after != null ? after.ToString() : null;
                if (t == s && s == "VIEWING_GLASS")
                {
                    MelonLogger.Msg("[Hotkey] phone refused by game, still " + t);
                    Speak("Smartphone unavailable while serving. Finish or cancel the drink first.");
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Hotkey] phone hotkey threw: " + e.Message);
            }
        }

        /// <summary>
        /// Speaks and logs, matching MainMenuHotkeys.Speak. Interrupts (final argument true) because
        /// this is a direct answer to a keypress the player just made.
        /// </summary>
        private static void Speak(string text)
        {
            MelonLogger.Msg("[Hotkey] " + text);
            CoffeeTalkAccess.Speech.ISpeechOutput speech = AccessMod.Speech;
            if (speech == null || !speech.IsAvailable) return;
            speech.SpeakAs(null, text, UnityAccessibilityLib.TextType.Menu, true);
        }
    }
}
