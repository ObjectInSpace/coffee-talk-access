using System;
using System.Text;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;

namespace CoffeeTalkAccess.FullGame
{
    /// <summary>
    /// Speaks the ENDING CUTSCENE epilogues - the per-character "where they ended up" text that
    /// closes out each story arc.
    ///
    /// ⚠ THIS SCREEN WAS TWICE RECORDED AS "CHROME ONLY, NOTHING TO READ". Both times that was
    /// wrong, and the way it was wrong is worth keeping. The first survey counted localization
    /// terms per class and found almost none. The second checked the item types' fields and found
    /// only `Text`/`TextMeshProUGUI` COMPONENTS - render targets, not authored strings - and
    /// concluded the strings must live in the Unity scene. What neither check did was look for a
    /// WRITE to `.text`. There is one, in `TG_EndingCutsceneItem.DOImage1Animation`, and it pulls
    /// from a `credit/` namespace holding **27 keys**.
    ///
    /// ⚠ AND THE NAMESPACE IS `credit/`, SINGULAR. An earlier sweep counted `credits/` (plural,
    /// zero hits) alongside `gallery/`, `comic/` and `ending/` - all guessed prefixes, all zero,
    /// all read as confirmation that these screens had no content. The real keys are things like
    /// `credit/luaBaileysGood1` and `credit/hydeGalaNormal2`: **per-character epilogues that vary
    /// by how the player's story arc resolved.** That is the payoff for a whole playthrough, and it
    /// is the single most content-bearing thing left in phase 5 - not chrome.
    ///
    /// The lesson, recorded because it has now cost two wrong write-ups: a guessed key prefix that
    /// returns zero is not evidence of anything. Grep for the WRITE, then read the prefix off the
    /// code that performs it.
    ///
    /// ⚠ HOOK `GetDialogueEndingCutscene`, NOT THE METHOD THAT WRITES THE TEXT. The two writes
    /// happen inside `DOImage1Animation`, which is an ITERATOR (IEnumerator) - a compiler-generated
    /// state machine whose body does not run when the method is called, so a postfix there fires
    /// before any text exists. This is the same trap that made this project patch `SayDialog.Say`
    /// rather than `DoSay`. `GetDialogueEndingCutscene` is a plain method, it RETURNS the two keys,
    /// and it is called on the line immediately before they are used - so a postfix on it sees
    /// exactly what is about to be shown, with the arc outcome (GOOD / NORMAL / BAD) already
    /// resolved against the player's save.
    ///
    /// ⚠ UNTESTED, and unreachable on the demo - the demo's story stops long before any arc ends.
    /// Built for the retail build, like the newspaper.
    /// </summary>
    [HarmonyPatch]
    public static class EndingPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        /// <summary>
        /// Speaks the two epilogue lines for a character's ending.
        ///
        /// The keys are resolved through the game's own localizer rather than read back off the
        /// TextMeshProUGUI components, because at postfix time the components have not been written
        /// yet - the caller assigns them on the next two lines. Resolving the same keys the caller
        /// is about to resolve gives identical text one instant earlier, which is the difference
        /// between speaking the epilogue and speaking the PREVIOUS character's.
        ///
        /// Both lines are spoken as ONE announcement. They are two halves of a single paragraph
        /// (the game shows them stacked and fades them in together), and two Speaks would interrupt
        /// each other and leave only the second audible - the same reason the newspaper is read as
        /// one line.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_EndingCutsceneItem), nameof(TG_EndingCutsceneItem.GetDialogueEndingCutscene))]
        public static void AfterGetDialogueEndingCutscene(string[] __result)
        {
            try
            {
                if (__result == null || __result.Length == 0) return;

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < __result.Length; i++)
                {
                    string line = Localize(__result[i]);
                    if (string.IsNullOrEmpty(line)) continue;

                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(line);
                }

                if (sb.Length == 0) return;

                // TextType.Narrator, not Menu: this is story prose being narrated over images, not
                // interface feedback. It also means the repeat key stores it, which matters here -
                // the text fades on a TIMER and then is gone, exactly the case that motivated
                // widening the repeat predicate for the opening cutscene's credit lines.
                Announce(sb.ToString());
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Ending] epilogue hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Resolves one `credit/` key through the game's own localizer.
        ///
        /// The prefix is the game's, copied from the call site rather than assumed: it is `credit/`
        /// SINGULAR, which is the detail that made an earlier survey miss this screen entirely.
        ///
        /// A key that fails to resolve returns empty rather than the raw key. Speaking
        /// "credit slash lua baileys good one" would be worse than saying nothing, because the
        /// other half of the paragraph will still be spoken and the player would have no way to
        /// tell a localization gap from the actual prose.
        /// </summary>
        private static string Localize(string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return string.Empty;

                Type tgStatic = AccessTools.TypeByName("TG_Static");
                if (tgStatic == null) return string.Empty;

                object localizer = AccessTools.Field(tgStatic, "localizer")?.GetValue(null);
                if (localizer == null) return string.Empty;

                object resolved = AccessTools.Method(localizer.GetType(), "DirectLocalization", new[] { typeof(string) })
                    ?.Invoke(localizer, new object[] { "credit/" + key });

                string text = Convert.ToString(resolved);
                if (string.IsNullOrEmpty(text)) return string.Empty;

                // An unresolved key commonly comes back as the key itself; that is not prose.
                if (text.StartsWith("credit/", StringComparison.Ordinal)) return string.Empty;

                return Dialogue.FungusText.ExtractWords(text);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static void Announce(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            ISpeechOutput speech = Speech;
            if (speech == null || !speech.IsAvailable) return;

            MelonLogger.Msg("[Ending] " + line);
            speech.SpeakAs(null, line, TextType.Narrator, true);
        }
    }
}
