using System;
using CoffeeTalkAccess.Dialogue;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;
using UnityEngine;

namespace CoffeeTalkAccess.Menus
{
    /// <summary>
    /// Speaks the confirmation and load popups (TG_PopUpManager).
    ///
    /// WHY FocusNarrator CANNOT SEE THESE. Every call that gives a popup button EventSystem focus
    /// is wrapped in a JOYSTICK check - TG_PopUpManager.SelectButtonPopUpConfirmation:66 and
    /// SelectButtonPopUpLoad:98 both return immediately on keyboard, as does the deferred
    /// SelectButton* call inside DoSetActiveDelay:142/151. On a keyboard, therefore, NOTHING is
    /// ever selected while a popup is open: a focus watcher has literally nothing to observe. This
    /// is not a timing problem to be polled around; it is structural, so these hooks read the
    /// popup's text directly.
    ///
    /// WHAT THE PLAYER MUST BE TOLD, AND WHY IT IS NOT "USE THE ARROWS". On keyboard there is no
    /// cursor between Yes and No, because the game never creates one. The two answers are bound to
    /// SEPARATE KEYS instead (TG_KeyboardHotkeyManager):
    ///   ConfirmButtonHandler:192-198 -> InvokeYesButton*  (Confirm = Return)
    ///   HandlerBButton:145-152       -> InvokeNoButton*   (Back/Escape)
    /// Announcing "left and right to choose" would describe a control that does not exist here and
    /// leave the player pressing arrows at an unresponsive dialog. We announce the keys that
    /// actually answer it. On a gamepad the game does its own selecting and A/B do the same job,
    /// so the wording stays true for both.
    ///
    /// WHY THE ANNOUNCEMENT IS DEFERRED. The text is set BEFORE the popup is visible, and
    /// DoSetActiveDelay:130-135 then activates, DEACTIVATES, and reactivates the object across two
    /// frames. Speaking at text-set time would describe a dialog that is not on screen yet, and the
    /// state (POP_UP_CONFIRMATION / POP_UP_LOAD) is only set afterwards. We record the text on the
    /// setter and speak once the state confirms the popup is genuinely up - the same pattern
    /// NameEntryPatches uses for its tween callback, and for the same reason.
    /// </summary>
    [HarmonyPatch]
    public static class PopUpPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        // Text captured from the setters, awaiting the state that says the popup is live.
        private static string _pendingTitle;
        private static string _pendingInfo;
        private static string _lastAnnounced;

        /// <summary>
        /// Captures the popup's title.
        ///
        /// Patching the BASE class covers both popups: TG_PopUpLoadUI extends TG_PopUpUI and does
        /// not override SetPopUpTitleText, so one hook serves the confirmation and load dialogs
        /// alike. (This is the same base-class trick OptionPatches uses on TG_UIMenuContent - and
        /// the inverse of the cutscene case, where the base body was empty and the OVERRIDE had to
        /// be patched instead. Which one is correct depends on where the real body lives.)
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_PopUpUI), nameof(TG_PopUpUI.SetPopUpTitleText))]
        public static void AfterSetPopUpTitleText(string text)
        {
            Capture(text);
        }

        /// <summary>
        /// Captures a title supplied as a localization TERM rather than literal text.
        ///
        /// SetTextTerm resolves the term through TG_Static.localizer.DirectLocalization and assigns
        /// the SAME popUpText field, so the argument here is a key ("POPUP_QUIT"), not something a
        /// player should hear. We re-read the resolved field instead of speaking the argument.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_PopUpUI), nameof(TG_PopUpUI.SetTextTerm))]
        public static void AfterSetTextTerm(TG_PopUpUI __instance)
        {
            try
            {
                if (__instance != null && __instance.popUpText != null)
                    Capture(__instance.popUpText.text);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[PopUp] term hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Captures the load popup's secondary line (day/chapter detail shown under the title).
        /// Held separately from the title so the two are spoken as one announcement rather than as
        /// two, which would let the second interrupt the first.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_PopUpLoadUI), nameof(TG_PopUpLoadUI.SetInfoText))]
        public static void AfterSetInfoText(string text)
        {
            try
            {
                string clean = FungusText.ExtractWords(text ?? string.Empty);
                if (clean.Length == 0) return;

                _pendingInfo = clean;
                PopUpWatcher.Arm();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[PopUp] info hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Records popup text and arms the watcher.
        ///
        /// EMPTY TEXT IS NOT AN ERROR AND MUST NOT ARM ANYTHING. Callers legitimately pre-register
        /// a popup without showing it - TG_NameKeys.InitPopUp:97-103 calls SetPopUpConfirmation
        /// with "" and showPopUp:false purely to attach its Yes/No handlers ahead of time. Arming
        /// on that would leave a live 5-second watcher with no text, and if some other popup opened
        /// inside that window it would be announced with whatever was captured last. Requiring
        /// non-empty text means only a real dialog ever arms the watcher.
        /// </summary>
        private static void Capture(string text)
        {
            try
            {
                string clean = FungusText.ExtractWords(text ?? string.Empty);
                if (clean.Length == 0) return;

                _pendingTitle = clean;
                PopUpWatcher.Arm();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[PopUp] title hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Joins two spoken fragments with exactly one sentence break.
        ///
        /// Blindly appending ". " produced "Is Drew correct?. Press Enter for yes" live
        /// (26-8-9_23-20-27.log:23:21:42) - the game's own text already ends in punctuation, and a
        /// screen reader renders the doubled stop as an audible stumble. Game text is localized and
        /// authored per-string, so we cannot assume it does or does not end in a mark: check.
        /// </summary>
        private static string Join(string first, string second)
        {
            if (string.IsNullOrEmpty(first)) return second;
            if (string.IsNullOrEmpty(second)) return first;

            string left = first.TrimEnd();
            char last = left[left.Length - 1];
            bool ended = last == '.' || last == '!' || last == '?' || last == ':' || last == ';';

            return ended ? left + " " + second : left + ". " + second;
        }

        /// <summary>
        /// Speaks the popup once it is actually on screen. Called by the watcher, not by a setter.
        /// </summary>
        private static void Announce()
        {
            try
            {
                if (string.IsNullOrEmpty(_pendingTitle)) return;

                string line = _pendingTitle;
                if (!string.IsNullOrEmpty(_pendingInfo)) line = Join(line, _pendingInfo);

                // The keys that actually answer this dialog - see the class comment. Stated every
                // time rather than once per session: a popup is a modal interruption, and the
                // player who most needs the hint is the one who did not expect the dialog at all.
                line = Join(line, "Press Enter for yes, Escape for no.");

                // Guard against the setters firing more than once for the same dialog (the game
                // re-sets text on some paths); the popup should be described once per appearance.
                if (line == _lastAnnounced) return;
                _lastAnnounced = line;

                ISpeechOutput speech = Speech;
                if (speech == null || !speech.IsAvailable) return;

                MelonLogger.Msg("[PopUp] " + line);
                speech.SpeakAs(null, line, TextType.Menu, true);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[PopUp] announce threw: " + e.Message);
            }
        }

        /// <summary>
        /// Clears the captured text when the popup closes, so the next one is announced fresh
        /// rather than suppressed by the dedup above.
        /// </summary>
        private static void Reset()
        {
            _pendingTitle = null;
            _pendingInfo = null;
            _lastAnnounced = null;
        }

        /// <summary>
        /// Speaks the return to the name field after a popup is dismissed, including the name as it
        /// currently stands - which is the thing the player was being asked to confirm, and the
        /// thing they will now want to edit.
        /// </summary>
        private static void AnnounceReturnToName()
        {
            try
            {
                ISpeechOutput speech = Speech;
                if (speech == null || !speech.IsAvailable) return;

                string line = "Back to name entry." + NameEntryPatches.DescribeCurrentName();
                MelonLogger.Msg("[PopUp] " + line);
                speech.SpeakAs(null, line, TextType.Menu, true);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[PopUp] return announce threw: " + e.Message);
            }
        }

        /// <summary>
        /// Waits for the game to enter POP_UP_CONFIRMATION / POP_UP_LOAD before announcing, and
        /// notices when it leaves.
        ///
        /// The popup's own activation is a two-frame dance inside a coroutine (DoSetActiveDelay),
        /// and the state is assigned partway through it, so there is no single method whose postfix
        /// is the right moment. Polling the state is what tells us the dialog is really up.
        /// </summary>
        internal static class PopUpWatcher
        {
            private static bool _armed;
            private static bool _open;
            private static float _expiry;

            internal static void Arm()
            {
                _armed = true;
                // Generous vs. the two-frame activation, but bounded: an armed watcher that never
                // saw its state must not fire hours later over an unrelated screen.
                _expiry = Time.realtimeSinceStartup + 5f;
            }

            internal static void Update()
            {
                try
                {
                    string state = AccessMod.ReadControllerState();
                    bool isPopUp = state == "POP_UP_CONFIRMATION" || state == "POP_UP_LOAD";

                    if (isPopUp && _armed)
                    {
                        _armed = false;
                        _open = true;
                        Announce();
                        return;
                    }

                    // The popup closed: forget the text so an identical dialog next time is still
                    // announced. Without this the dedup would silence a repeated confirmation -
                    // and "are you sure?" asked twice is exactly when silence is most dangerous.
                    if (_open && !isPopUp)
                    {
                        _open = false;
                        Reset();

                        // Let the main menu re-announce the entry the cursor returns to. Its dedup
                        // would otherwise treat "same button as before the popup" as nothing to
                        // report, and the player would come back from a modal to silence.
                        MenuPatches.ForgetLastAnnouncement();

                        // Say where the player has landed. Dismissing a modal returns them to a
                        // screen they cannot see, and the game says nothing: TG_NameKeys.NoButton
                        // silently calls ActivateInputField() and hands focus back to the text
                        // field. Without this the player answers "no" and is left with no evidence
                        // that anything happened - the silent-stop failure mode again. Only the
                        // name screen is named specifically, because it is the one the confirm
                        // popup interrupts on the critical path; elsewhere the underlying screen's
                        // own narrator takes over as focus moves.
                        if (state == "INPUT_NAME")
                            AnnounceReturnToName();
                    }

                    if (_armed && Time.realtimeSinceStartup > _expiry)
                    {
                        _armed = false;
                        MelonLogger.Warning("[PopUp] text was set but no popup state arrived; not announced.");
                    }
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[PopUp] watcher threw: " + e.Message);
                }
            }
        }
    }
}
