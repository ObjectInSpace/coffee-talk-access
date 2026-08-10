using System;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeTalkAccess.Menus
{
    /// <summary>
    /// Speaks the player-name entry screen (TG_NameKeys, State.INPUT_NAME).
    ///
    /// THE BUG THIS FIXES: the screen announced itself as the single word "Input" and nothing else.
    /// That was not a silent mod - it was FocusNarrator working correctly on a control with no
    /// label. TG_NameKeys.Initialize ends by calling playerNameInput.ActivateInputField(), which
    /// focuses the InputField; the field carries no Text/TMP caption, so FocusNarrator fell through
    /// to its last-resort Prettify(go.name) branch and read the object's name.
    ///
    /// WHAT THE PLAYER CANNOT OTHERWISE KNOW: three things on this screen are invisible without
    /// sight and are not deducible from the field itself.
    ///  1. The field is PREFILLED with TG_Static.PLAYERDEFAULT_NAME, so it is not empty.
    ///  2. `deleteFocusedName` (TG_NameKeys:120-124) wipes that prefill on the FIRST character
    ///     typed. Without narration the name appears to vanish for no reason.
    ///  3. There is a 12-character cap (MAX_CHARACTER_LENGTH); input past it is silently dropped.
    ///
    /// WHY PATCHES AND NOT A FOCUS WATCHER: FocusNarrator is a read-only observer of focus CHANGES.
    /// Everything interesting here happens while focus never moves - characters are typed, the
    /// prefill is cleared, an empty-name error flashes. Those are events, so they need hooks.
    /// </summary>
    [HarmonyPatch]
    public static class NameEntryPatches
    {
        // Text of the field as of the last announcement, so edits are reported as deltas rather
        // than re-reading the whole name on every keystroke.
        private static string _lastText;

        private static ISpeechOutput Speech => AccessMod.Speech;

        private static void Say(string line, bool interrupt = true)
        {
            try
            {
                ISpeechOutput speech = Speech;
                if (speech == null || !speech.IsAvailable) return;
                speech.SpeakAs(null, line, TextType.Menu, interrupt);
                MelonLogger.Msg("[Name] " + line);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Name] speak threw: " + e.Message);
            }
        }

        /// <summary>
        /// Announces the screen once the field is actually live.
        ///
        /// Initialize() does NOT focus the field directly - it fades the cover panel and focuses in
        /// a DOTween OnComplete callback half a second later (TG_NameKeys:78-83). Announcing in a
        /// postfix on Initialize would therefore speak BEFORE the screen is interactive, and the
        /// language-picker lesson applies: an announcement that precedes the state it describes
        /// gets overwritten by whatever the game says next. We instead poll for the state flipping
        /// to INPUT_NAME, which is set in that same callback.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_NameKeys), nameof(TG_NameKeys.Initialize))]
        public static void AfterInitialize(TG_NameKeys __instance)
        {
            _lastText = null;
            NameScreenWatcher.Arm(__instance);
        }

        /// <summary>Speaks the whole prompt: purpose, current contents, and the rules.</summary>
        internal static void AnnounceScreen(TG_NameKeys keys)
        {
            string current = ReadField(keys);
            _lastText = current;

            string line = "Enter your name. Edit box.";
            if (!string.IsNullOrEmpty(current))
                line += " Currently " + Spell(current) + ". Typing replaces it.";
            line += " Up to 12 characters. Press Enter to confirm.";

            Say(line);
        }

        /// <summary>
        /// Reports edits as they happen. OnValueChanged is the game's own listener on the
        /// InputField (wired in Start), so this covers typing, backspace, and the on-screen
        /// keyboard's EnterCharacter/Backspace alike - every path that mutates the field.
        ///
        /// Speaking only the DELTA (the new character) rather than the whole name keeps typing
        /// responsive; the full name is available on demand via the repeat key.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_NameKeys), nameof(TG_NameKeys.OnValueChanged))]
        public static void AfterOnValueChanged(TG_NameKeys __instance)
        {
            try
            {
                ReportEdit(__instance);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Name] value hook threw: " + e.Message);
            }
        }

        /// <summary>Speaks the net change to the field since the last announcement.</summary>
        private static void ReportEdit(TG_NameKeys __instance)
        {
            try
            {
                // Read the FIELD, not the nameValue argument: OnValueChanged rewrites
                // playerNameInput.text through two regexes (stripping punctuation and a leading
                // space) AFTER receiving the raw value, so the argument is the pre-sanitized text.
                // Announcing it would tell the player a character was accepted that was in fact
                // discarded.
                string now = ReadField(__instance);
                string before = _lastText;
                _lastText = now;

                if (before == null || now == before) return;

                if (now.Length == 0)
                {
                    Say("Name cleared.");
                    return;
                }

                // Diff around a common PREFIX and SUFFIX rather than assuming edits land at the
                // end. Live evidence (22:57:24-28) of editing "Drew" mid-string:
                //   deleting the leading D  -> announced "rew"     (should be "D deleted")
                //   inserting r after D     -> announced "Drrew"   (should be "r")
                // Both read the whole field aloud on every keystroke, which is unusable for
                // correcting a typo - exactly when precise feedback matters most. The caret can
                // sit anywhere, so only the changed SPAN is news.
                int prefix = 0;
                int maxPrefix = Math.Min(before.Length, now.Length);
                while (prefix < maxPrefix && before[prefix] == now[prefix]) prefix++;

                int suffix = 0;
                int maxSuffix = Math.Min(before.Length, now.Length) - prefix;
                while (suffix < maxSuffix
                       && before[before.Length - 1 - suffix] == now[now.Length - 1 - suffix]) suffix++;

                string removed = before.Substring(prefix, before.Length - prefix - suffix);
                string added = now.Substring(prefix, now.Length - prefix - suffix);

                if (added.Length > 0 && removed.Length == 0)
                {
                    Say(Spell(added), false);
                }
                else if (removed.Length > 0 && added.Length == 0)
                {
                    Say(Spell(removed) + " deleted", false);
                }
                else if (added.Length > 0)
                {
                    // A true replacement: a selection typed over, or the deleteFocusedName prefill
                    // wipe landing its first character. Say the result, since neither half alone
                    // describes what happened.
                    Say(Spell(added), false);
                }

                if (now.Length >= 12) Say("Maximum length reached.", false);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Name] value hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Speaks the confirm step, including the empty-name refusal.
        ///
        /// ConfirmName has two outcomes and only one of them is textual. An empty name flashes
        /// `noTextAimationImage` (TG_NameKeys:172-179) - a picture, with no string anywhere - so
        /// without this the player presses Enter, nothing happens, and there is no way to learn
        /// why. A non-empty name opens the confirmation popup, whose text the game does localize.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(TG_NameKeys), nameof(TG_NameKeys.ConfirmName))]
        public static void BeforeConfirmName(TG_NameKeys __instance)
        {
            try
            {
                // Mirror the game's own guard: outside INPUT_NAME, ConfirmName returns immediately
                // and speaking here would narrate an action that did not occur.
                if (AccessMod.ReadControllerState() != "INPUT_NAME") return;

                if (string.IsNullOrEmpty(ReadField(__instance)))
                    Say("Name cannot be empty. Type a name first.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Name] confirm hook threw: " + e.Message);
            }
        }

        /// <summary>Announces the return to the main menu, so the screen change is not silent.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_NameKeys), nameof(TG_NameKeys.BackToMainMenu))]
        public static void AfterBackToMainMenu()
        {
            NameScreenWatcher.Disarm();
            _lastText = null;
        }

        /// <summary>
        /// Describes the name currently in the field, for another screen to append to its own
        /// announcement (the popup narrator uses this when a confirmation is dismissed).
        ///
        /// Returns a leading-space fragment, or empty if the name cannot be read - the caller's
        /// sentence must still make sense without it. Lives here rather than in the popup code so
        /// that reading the field stays the responsibility of the class that owns the screen, and
        /// the state it also keeps in sync via _lastText.
        /// </summary>
        internal static string DescribeCurrentName()
        {
            try
            {
                TG_NameKeys keys = TG_GenericSingelton<TG_NameKeys>.Instance;
                if (keys == null) return string.Empty;

                string current = ReadField(keys);
                // Keep _lastText in step: the popup may have been dismissed after an edit, and a
                // stale baseline would make the next keystroke announce a wrong delta.
                _lastText = current;

                return string.IsNullOrEmpty(current)
                    ? " The name is empty."
                    : " Currently " + Spell(current) + ".";
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Reads playerNameInput.text defensively - a null field must not break typing.
        ///
        /// ⚠ READ BY REFLECTION BECAUSE RETAIL CHANGED THIS FIELD, and it is the only member in the
        /// whole mod that retail moved. Measured 2026-08-10 by building against both assemblies:
        ///   demo:   public  InputField           playerNameInput
        ///   retail: private TG_CustomInputField  playerNameInput
        /// Two independent breaks in one field - the TYPE changed and the ACCESSIBILITY changed - so
        /// `keys.playerNameInput` does not compile against retail at all. This is the one place the
        /// "one assembly, different scene data" rule in PLAN.md does NOT hold, and it sits on the
        /// critical path: the name screen is the step straight after the profile picker.
        ///
        /// Casting to InputField is safe on both, since TG_CustomInputField DERIVES from InputField
        /// (it only adds OnSelect/OnDeselect events), so `.text` means the same thing either way.
        /// Reflection over the field NAME - unchanged between the builds - is what spans them.
        /// </summary>
        private static string ReadField(TG_NameKeys keys)
        {
            try
            {
                if (keys == null) return string.Empty;

                InputField field =
                    AccessTools.Field(keys.GetType(), "playerNameInput")?.GetValue(keys) as InputField;

                return field == null || field.text == null ? string.Empty : field.text;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Renders text for speech. Single characters are spoken as themselves (a screen reader
        /// says "a" clearly), but a space would be silence - which sounds identical to a dropped
        /// keystroke - so it is named explicitly.
        /// </summary>
        private static string Spell(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s == " ") return "space";
            return s.Replace(" ", " space ");
        }

        /// <summary>
        /// Waits for the game to actually enter INPUT_NAME before announcing.
        ///
        /// The state is set inside a tween callback, so there is no method to postfix that runs at
        /// the right moment. This polls from OnUpdate for a bounded number of seconds and fires
        /// once - a watcher that could linger forever would eventually announce the name screen
        /// over some unrelated later screen.
        /// </summary>
        internal static class NameScreenWatcher
        {
            private static TG_NameKeys _target;
            private static float _expiry;

            internal static void Arm(TG_NameKeys keys)
            {
                _target = keys;
                // The fade is 0.5s; allow generous margin for a slow load without arming forever.
                _expiry = Time.realtimeSinceStartup + 10f;
            }

            internal static void Disarm()
            {
                _target = null;
            }

            internal static void Update()
            {
                if (_target == null) return;

                if (Time.realtimeSinceStartup > _expiry)
                {
                    MelonLogger.Warning("[Name] INPUT_NAME never arrived; watcher expired.");
                    _target = null;
                    return;
                }

                if (AccessMod.ReadControllerState() != "INPUT_NAME") return;

                TG_NameKeys keys = _target;
                _target = null;
                AnnounceScreen(keys);
            }
        }
    }
}
