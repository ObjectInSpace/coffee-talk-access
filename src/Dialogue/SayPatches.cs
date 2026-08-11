using System;
using CoffeeTalkAccess.Speech;
using Fungus;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;

namespace CoffeeTalkAccess.Dialogue
{
    /// <summary>
    /// Speaks Coffee Talk's dialogue.
    ///
    /// HOOK CHOICE: Fungus routes spoken lines through SayDialog.Say(text, ...) - both the Say
    /// command (Say.cs) and TG_EndlessModeDialogManager call it, and nothing subclasses SayDialog.
    /// We patch Say rather than DoSay because DoSay is a compiler-generated iterator (patching it
    /// means patching MoveNext on a state machine).
    ///
    /// DIAGNOSTIC BUILD: Say verified as PATCHED (log shows "Applied to: Fungus.SayDialog.Say")
    /// but never OBSERVED FIRING in a live demo run. Rather than guess again, this build also
    /// hooks the Writer - the layer that actually renders glyphs - and logs BOTH. Whichever one
    /// fires tells us where the demo's text really flows. Speech is emitted from whichever hook
    /// wins, so the player gets dialogue either way.
    /// </summary>
    [HarmonyPatch]
    public static class SayPatches
    {
        // The speaker for the line currently being issued, set by the SetCharacterName hook.
        private static string _pendingSpeaker;

        // Guards against the same line being spoken twice when BOTH hooks fire for one line.
        private static string _lastSpoken;

        /// <summary>
        /// Captures the display name Fungus resolved for the speaking character. This is the
        /// RESOLVED name, so Coffee Talk's "????" for not-yet-introduced characters is preserved
        /// rather than us leaking the real identity early.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SayDialog), nameof(SayDialog.SetCharacterName))]
        public static void AfterSetCharacterName(string name)
        {
            _pendingSpeaker = string.IsNullOrEmpty(name) ? null : name.Trim();
            MelonLogger.Msg("[Hook] SetCharacterName: '" + _pendingSpeaker + "'");
        }

        /// <summary>Speaks each story line as SayDialog issues it.</summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SayDialog), nameof(SayDialog.Say))]
        public static void BeforeSay(string text)
        {
            MelonLogger.Msg("[Hook] SayDialog.Say FIRED, len=" + (text == null ? -1 : text.Length));
            SpeakLine(text, "Say");
        }

        /// <summary>
        /// The Writer is the component that actually renders text to the TMP field. If the demo
        /// drives the writer directly (bypassing SayDialog.Say), this is where the line appears.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Writer), nameof(Writer.Write))]
        public static void BeforeWrite(string content)
        {
            MelonLogger.Msg("[Hook] Writer.Write FIRED, len=" + (content == null ? -1 : content.Length));
            SpeakLine(content, "Write");
        }

        /// <summary>
        /// Shared speech path. Dedups so that if both hooks fire for the same line the player
        /// hears it once.
        /// </summary>
        private static void SpeakLine(string raw, string source)
        {
            try
            {
                ISpeechOutput speech = AccessMod.Speech;
                if (speech == null || !speech.IsAvailable) return;

                // The player's "~" gate. Checked BEFORE the dedup below is updated, on purpose:
                // _lastSpoken must keep describing what was last SPOKEN, not what was last seen.
                // Were it updated while muted, the line on screen when narration is switched back
                // on would be treated as already-said and stay silent.
                if (!DialogueToggle.Enabled) return;

                string spoken = FungusText.ExtractWords(raw);
                if (spoken.Length == 0) return;
                if (spoken == _lastSpoken) return;

                _lastSpoken = spoken;
                MelonLogger.Msg("[Speak/" + source + "] " + spoken);

                // interrupt:true - a new line always supersedes the previous one. Coffee Talk lets
                // the player advance before the typewriter finishes, so without interrupt the
                // backlog would run further and further behind what is on screen.
                speech.SpeakAs(_pendingSpeaker, spoken, TextType.Dialogue, true);
            }
            catch (Exception e)
            {
                // Never let a narration fault propagate into the game's dialogue coroutine.
                MelonLogger.Warning("[Speak] hook threw: " + e.Message);
            }
        }
    }
}
