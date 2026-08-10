using System;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;

namespace CoffeeTalkAccess.Dialogue
{
    /// <summary>
    /// Speaks the cutscene text system - the lines shown BEFORE the game proper starts.
    ///
    /// WHY THE FUNGUS HOOKS MISSED THIS ENTIRELY: TG_CutsceneManager is a second, independent text
    /// system that shares nothing with Fungus. Its lines come from
    /// TG_Static.localizer.OpeningCutsceneLocalization() into `cutsceneTextList`, are selected into
    /// `currentWholeText` by SetDialogueText(), and are typed one glyph at a time into a
    /// TextMeshProUGUI by the DisplayText() coroutine. SayDialog.Say and Writer.Write are never
    /// called. Both hooks were correctly applied and correctly silent - they were watching the
    /// wrong pipe.
    ///
    /// WHY SetDialogueText AND NOT DisplayText: DisplayText appends ONE CHARACTER per frame
    /// (TG_CutsceneManager:154-159). Hooking it would re-speak the line on every glyph. It is the
    /// same trap SayPatches already avoids by hooking Say instead of the writer; SetDialogueText is
    /// the moment the whole line becomes known, which is exactly what we want to read.
    ///
    /// WHY THE DERIVED CLASS: SetDialogueText is virtual and the base implementation is EMPTY - the
    /// real body is TG_OpeningCutSceneManager's override. Patching the base would attach to a
    /// method the opening cutscene never executes, producing another silent-but-applied hook. We
    /// patch the override, and cover the base too for any subclass that does not override it.
    /// </summary>
    [HarmonyPatch]
    public static class CutscenePatches
    {
        private static string _lastSpoken;
        private static string _lastCredit;

        // `type` is one of the TextType constants (a static class of ints, not an enum).
        private static void Speak(string raw, string source, int type)
        {
            try
            {
                ISpeechOutput speech = AccessMod.Speech;
                if (speech == null || !speech.IsAvailable) return;

                string spoken = FungusText.ExtractWords(raw);
                if (spoken.Length == 0) return;
                if (spoken == _lastSpoken) return;
                _lastSpoken = spoken;

                MelonLogger.Msg("[Cutscene/" + source + "] " + spoken);

                // interrupt:true - the player can press Submit to skip ahead mid-typewriter
                // (TG_CutsceneManager:80-91), so a queued backlog would fall behind the screen.
                speech.SpeakAs(null, spoken, type, true);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Cutscene] speak threw: " + e.Message);
            }
        }

        /// <summary>
        /// Speaks each opening-cutscene line as it is issued.
        ///
        /// Postfix, not prefix: SetDialogueText assigns `currentWholeText` from
        /// cutsceneTextList[index][dialogueCutsceneIndex], so the field only holds this line's text
        /// after the original method has run. Reading it in a prefix would speak the PREVIOUS line.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_OpeningCutSceneManager), nameof(TG_OpeningCutSceneManager.SetDialogueText))]
        public static void AfterOpeningSetDialogueText(TG_OpeningCutSceneManager __instance)
        {
            SpeakCurrentLine(__instance, "Opening");
        }

        /// <summary>
        /// Covers any cutscene manager that inherits the base SetDialogueText rather than
        /// overriding it. Harmless for TG_OpeningCutSceneManager (whose override does not call
        /// base), and it means a subclass added later is narrated without a new patch.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_CutsceneManager), nameof(TG_CutsceneManager.SetDialogueText))]
        public static void AfterBaseSetDialogueText(TG_CutsceneManager __instance)
        {
            SpeakCurrentLine(__instance, "Cutscene");
        }

        /// <summary>
        /// Reads the protected `currentWholeText` field via reflection. Protected members are not
        /// accessible to us directly, and AccessTools resolves it up the hierarchy so the same call
        /// works for the base class and every subclass.
        /// </summary>
        private static void SpeakCurrentLine(object manager, string source)
        {
            try
            {
                if (manager == null) return;

                string text = AccessTools.Field(manager.GetType(), "currentWholeText")
                    ?.GetValue(manager) as string;

                if (string.IsNullOrEmpty(text)) return;
                Speak(text, source, TextType.Dialogue);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Cutscene] line hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Speaks the per-panel credit line ("art by...", etc.) shown alongside each cutscene
        /// scene. It is a SEPARATE Text field from the dialogue (TG_OpeningCutSceneManager:62),
        /// updated in DoCutscene, so it is not covered by the line hook above.
        ///
        /// Spoken with interrupt:false and tracked separately from _lastSpoken: the credit and the
        /// first line of a panel are issued close together, and interrupting would drop one of them.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_OpeningCutSceneManager), "GetCreditOpeningText")]
        public static void AfterGetCreditOpeningText(string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;

                string spoken = FungusText.ExtractWords(__result);
                if (spoken.Length == 0 || spoken == _lastCredit) return;
                _lastCredit = spoken;

                ISpeechOutput speech = AccessMod.Speech;
                if (speech == null || !speech.IsAvailable) return;

                MelonLogger.Msg("[Cutscene/Credit] " + spoken);
                speech.SpeakAs(null, spoken, TextType.Menu, false);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Cutscene] credit hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Speaks the blinking "press any button" / "press any button to skip" prompts.
        ///
        /// These are pure UI chrome with no focusable control behind them, so neither FocusNarrator
        /// nor any menu patch can see them - yet they are the only indication that the game is
        /// WAITING for the player rather than still loading. Both classes set their text through
        /// SetTextLocalization, which the game also re-fires when the input device changes (so the
        /// prompt can switch between "button" and "key" wording).
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_PressAnyBlinkTextUI), nameof(TG_PressAnyBlinkTextUI.SetTextLocalization))]
        public static void AfterPressAnyText(TG_PressAnyBlinkTextUI __instance)
        {
            SpeakPrompt(__instance, "PressAny");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_PressAnyToSkipBlinkTextUI), nameof(TG_PressAnyToSkipBlinkTextUI.SetTextLocalization))]
        public static void AfterPressAnyToSkipText(TG_PressAnyToSkipBlinkTextUI __instance)
        {
            SpeakPrompt(__instance, "PressAnyToSkip");
        }

        private static string _lastPrompt;

        /// <summary>
        /// Reads the prompt's own Text component (`blinkTextArea`, declared on TG_BlinkingText) and
        /// speaks it. Deduped separately because SetTextLocalization re-fires on every input-device
        /// change, which would otherwise repeat the prompt when the player merely touches the pad.
        /// </summary>
        private static void SpeakPrompt(object prompt, string source)
        {
            try
            {
                if (prompt == null) return;

                object field = AccessTools.Field(prompt.GetType(), "blinkTextArea")?.GetValue(prompt);
                string text = null;

                if (field is UnityEngine.UI.Text uiText) text = uiText.text;
                else if (field is TMPro.TextMeshProUGUI tmp) text = tmp.text;

                if (string.IsNullOrEmpty(text)) return;

                string spoken = FungusText.ExtractWords(text);
                if (spoken.Length == 0 || spoken == _lastPrompt) return;
                _lastPrompt = spoken;

                ISpeechOutput speech = AccessMod.Speech;
                if (speech == null || !speech.IsAvailable) return;

                MelonLogger.Msg("[Cutscene/" + source + "] " + spoken);
                speech.SpeakAs(null, spoken, TextType.Menu, false);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Cutscene] prompt hook threw: " + e.Message);
            }
        }
    }
}
