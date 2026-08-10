using System;
using System.Text;
using CoffeeTalkAccess.Dialogue;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using TMPro;
using UnityAccessibilityLib;
using UnityEngine.UI;

namespace CoffeeTalkAccess.FullGame
{
    /// <summary>
    /// Reads the daily newspaper.
    ///
    /// ⚠ UNTESTED AND UNTESTABLE ON THE DEMO. The demo's story stops before the day-cycle that
    /// generates a newspaper, so no run can exercise this. It is written from the decompiled source
    /// against the real Assembly-CSharp (every member below verified by reflection), and must stay
    /// marked untested until the retail build is available. Per the standing rule, "untested" is
    /// not allowed to block the phases that CAN be tested - but it also must not be quietly
    /// upgraded to "working" later, so this comment is the record.
    ///
    /// WHY GenerateNewspaper IS THE HOOK. It resolves all four pieces of the paper into the
    /// template's fields and then instantiates it:
    ///     mainHeadline (TMP) / bigNews (TMP) / smallNews (TMP) / dateText (legacy UI.Text)
    /// Postfixing it means we read the fully-substituted, localized strings the player will see,
    /// rather than re-deriving them from the news term dictionaries (which requires replicating the
    /// day-index and DEFAULT_NEWS_DAY fallback logic - two places to drift instead of one).
    ///
    /// ⚠ MIXED TEXT COMPONENTS. The three news fields are TextMeshProUGUI and the date is a legacy
    /// UI.Text. A hook that assumed one type would silently read three quarters of the paper. They
    /// are read through separate accessors for that reason.
    ///
    /// NAVIGATION IS NOT NEEDED HERE, AND MUST NOT BE ADDED. The newspaper is a read-then-dismiss
    /// screen: TG_KeyboardHotkeyManager.EnterButtonHandler already handles READING_NEWSPAPER and
    /// invokes newspaperObj.newspaperButton, and the controller path does the same at
    /// TG_ControllerInputManager:1317-1322. The player can already dismiss it from either device.
    /// The only gap is that nothing is spoken - so this class only speaks.
    /// </summary>
    [HarmonyPatch]
    public static class NewspaperPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        /// <summary>
        /// Speaks the whole paper once it has been generated.
        ///
        /// Read as ONE announcement rather than four, because each Speak interrupts the last: four
        /// calls would leave the player hearing only the date. The paper is short (a headline, two
        /// stories and a date), so a single utterance is the right granularity - and the repeat key
        /// can bring the whole thing back, since it is stored as one entry.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_NewspaperManager), nameof(TG_NewspaperManager.GenerateNewspaper))]
        public static void AfterGenerateNewspaper(TG_NewspaperManager __instance)
        {
            try
            {
                if (__instance == null) return;

                // activeNewspaperDisplay false means the paper was generated but deliberately not
                // shown (HideNewspaper is called instead). Announcing it then would describe a
                // screen the player is not looking at.
                if (!__instance.activeNewspaperDisplay) return;

                TG_NewspaperContent paper = __instance.newspaperObj;
                if (paper == null)
                {
                    MelonLogger.Warning("[News] generated but newspaperObj was null; nothing to read.");
                    return;
                }

                StringBuilder sb = new StringBuilder();
                Append(sb, "Newspaper");
                Append(sb, TextOf(paper.dateText));
                Append(sb, TextOf(paper.mainHeadline));
                Append(sb, TextOf(paper.bigNews));
                Append(sb, TextOf(paper.smallNews));

                string line = sb.ToString();
                if (line.Length == 0)
                {
                    // The paper exists but carried no readable text. Say so - silence here is
                    // indistinguishable from the hook not firing at all.
                    Announce("Newspaper, no readable text.");
                    return;
                }

                Announce(line + " Press Enter to start the day.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[News] hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Joins a fragment onto the announcement with exactly one sentence break, matching the
        /// punctuation-aware behaviour PopUpPatches needed: the game's own strings already end in
        /// their own punctuation, and a blindly appended full stop reads as an audible stumble.
        /// </summary>
        private static void Append(StringBuilder sb, string fragment)
        {
            if (string.IsNullOrEmpty(fragment)) return;

            string clean = FungusText.ExtractWords(fragment);
            if (clean.Length == 0) return;

            if (sb.Length > 0)
            {
                char last = sb[sb.Length - 1];
                bool ended = last == '.' || last == '!' || last == '?' || last == ':' || last == ';';
                sb.Append(ended ? " " : ". ");
            }
            sb.Append(clean);
        }

        /// <summary>Reads a TextMeshPro field, tolerating a null component.</summary>
        private static string TextOf(TextMeshProUGUI tmp)
        {
            return tmp == null ? null : tmp.text;
        }

        /// <summary>Reads a legacy UI.Text field, tolerating a null component.</summary>
        private static string TextOf(Text text)
        {
            return text == null ? null : text.text;
        }

        private static void Announce(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            ISpeechOutput speech = Speech;
            if (speech == null || !speech.IsAvailable) return;

            MelonLogger.Msg("[News] " + line);
            speech.SpeakAs(null, line, TextType.Menu, true);
        }
    }
}
