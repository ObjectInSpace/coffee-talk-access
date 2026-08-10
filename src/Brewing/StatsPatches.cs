using System;
using System.Text;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;
using UnityEngine;

namespace CoffeeTalkAccess.Brewing
{
    /// <summary>
    /// Speaks the four brewing stats: what is in the glass now, and what the focused ingredient
    /// WOULD add before the player commits to it.
    ///
    /// WHY THE PREVIEW IS THE HALF THAT MATTERS. Coffee Talk's brewing puzzles are stat-tier
    /// targets ("something warm and not sweet"), and the sighted loop is: move over an ingredient,
    /// watch the extra segments BLINK on the bars, and only then press it. That is a shop-by-feel
    /// loop. A query key alone answers "what is in the glass NOW" and misses the entire decision
    /// the player is actually making, which is between ingredients they have not added yet.
    ///
    /// ⚠ MouseHoverEvent IS NOT MOUSE-ONLY - VERIFIED, AND THIS IS WHAT MAKES THE PREVIEW CHEAP.
    /// The name suggests a pointer-only path, and a preview that only worked under the mouse would
    /// be useless here. It is not: TG_Button implements ISelectHandler and its OnSelect body is a
    /// bare `MouseHoverEvent()` call, identical to OnPointerEnter's. So keyboard and gamepad focus
    /// already run the same preview the mouse does - the game computes it for us on every focus
    /// move, and the mod only has to READ it. Nothing here drives focus or re-implements the
    /// stat math.
    ///
    /// ⚠ WHY THE PREVIEW DOES NOT SPEAK FOR ITSELF. MouseHoverEvent runs SYNCHRONOUSLY inside
    /// OnSelect, whereas FocusNarrator announces the same button from Update() a frame later, with
    /// interrupt:true. A preview that spoke directly would be cut off mid-word by the ingredient's
    /// own name - the exact talk-over that silenced the name-entry screen (FocusNarrator.cs:71).
    /// Instead the preview parks its phrase in PendingStats and FocusNarrator appends it to the
    /// label it was already going to speak, so the player hears ONE line:
    ///     "Honey, adds 2 sweetness".
    ///
    /// STATS ARE SPOKEN AS COUNTS, NOT TIERS. SetIndicatorAllStasBars' parameters are NAMED
    /// tierSweetness etc., but its callers pass the RAW accumulated stat (SetStatsDrinkUI passes
    /// the sweetness/bitterness/warmth/coolness fields straight through), and it lights exactly one
    /// segment per unit: `if (i &lt; tierSweetness) FillBar(i)`. So a sighted player is counting lit
    /// segments out of fillBarStatsImage.Length, and "sweetness 3 of 5" mirrors the screen exactly.
    /// GetTier's 0/1/2/3 banding is a SEPARATE thing used to match drink orders, not what the bars
    /// show - speaking tiers here would report a number that is on nobody's screen.
    /// </summary>
    [HarmonyPatch]
    public static class StatsPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        /// <summary>
        /// Preview phrase for the ingredient that just took focus, consumed by FocusNarrator on the
        /// very next Update. Same one-shot channel shape as PendingPosition: written by whoever
        /// knows the fact, cleared by whoever speaks it, so it can never be spoken twice.
        /// </summary>
        internal static string PendingStats;

        /// <summary>
        /// Captures the game's own preview computation for the focused ingredient.
        ///
        /// Postfix, and reading the manager's previewSweet/previewBitter/previewWarm/previewCool
        /// fields rather than re-deriving them from the arguments: SetPreviewStatsDrinkUI is GATED
        /// (`glass_value &lt; 3 &amp;&amp; !isPreviewStatsActive`) and simply returns when the glass is full or
        /// a preview is already live. Reading the fields it just wrote means we announce a preview
        /// exactly when the game actually shows one, instead of describing a blink that never
        /// happened - the arguments are what was ASKED for, the fields are what was DONE.
        ///
        /// The per-slot indexing matters and is the game's, not ours: an ingredient's stats are
        /// int[] arrays indexed by glass_value, so honey contributes differently as the first
        /// ingredient than as the third. Reading the resolved fields inherits that for free.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_DrinkManager), nameof(TG_DrinkManager.SetPreviewStatsDrinkUI))]
        public static void AfterSetPreviewStats(TG_DrinkManager __instance)
        {
            try
            {
                // The gate rejected it - the glass is full, or this preview is already showing.
                // Say nothing rather than describing a blink the player cannot see.
                if (!GetBool(__instance, "isPreviewStatsActive")) return;

                int sweet = GetInt(__instance, "previewSweet");
                int bitter = GetInt(__instance, "previewBitter");
                int warm = GetInt(__instance, "previewWarm");
                int cool = GetInt(__instance, "previewCool");

                PendingStats = DescribeDelta(sweet, bitter, warm, cool);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Stats] preview hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Drops a stale preview when focus leaves the ingredient without the player committing.
        ///
        /// Without this, a preview parked in PendingStats by an ingredient the player has already
        /// moved off could be appended to the NEXT control's label - attaching honey's numbers to
        /// the Brew button. The game itself pairs every hover with an unhover for exactly this
        /// reason (it has to stop the blink), so we mirror its lifetime rather than inventing one.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_DrinkManager), nameof(TG_DrinkManager.KillAnimationPreviewStatsDrinkUI))]
        public static void AfterKillPreviewStats()
        {
            PendingStats = null;
        }

        /// <summary>
        /// Announces the brew-information toggle, and - the part that actually matters - that
        /// opening it TURNS THE STAT PREVIEWS OFF.
        ///
        /// The button appeared in the live log as "Info Brew Button, unlabeled": it is a bare Button
        /// in the scene with no Text of its own, so the generic scan had nothing to read and
        /// correctly reported a gap. This supplies the label the scene does not.
        ///
        /// ⚠ IT IS NOT AN INFO SCREEN, IT IS A MODE SWITCH. BrewInformationClick swaps the stats bar
        /// panel for the info panel (TG_DrinkManager:294-308), and while the info panel is open
        /// `informationInfoOpen` is true, which GATES the preview update at TG_DrinkManager:746 -
        /// the game stops calling PreviewIndicator*StatsBar entirely. For a sighted player that is
        /// obvious: the bars they were watching are no longer on screen. For a blind player the
        /// consequence is invisible and awful - every ingredient would simply stop announcing "adds
        /// 2 sweetness" with nothing said about why. So we say why, once, at the moment it changes.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_DrinkManager), nameof(TG_DrinkManager.BrewInformationClick))]
        public static void AfterBrewInformationClick(TG_DrinkManager __instance)
        {
            try
            {
                bool open = GetBool(__instance, "informationInfoOpen");

                // Stale previews belong to the bars that just went away.
                if (open) PendingStats = null;

                Announce(open
                    ? "Brewing information open. Ingredient stat previews are off while this is open."
                    : "Brewing information closed. Ingredient stat previews are back on.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Stats] brew info hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Drops the preview once the ingredient is actually COMMITTED.
        ///
        /// ⚠ AddIngredient DOES NOT GO THROUGH THE KILL METHOD. It clears isPreviewStatsActive by
        /// assigning the field directly and never calls KillAnimationPreviewStatsDrinkUI, so the
        /// hook above cannot see this path - the game does not need it to, because the bars are
        /// about to be redrawn wholesale by SetStatsDrinkUI anyway.
        ///
        /// Without this the announcement is actively WRONG rather than merely stale. AddIngredient
        /// ends by calling Select() on either the Brew button or the ingredient, which runs
        /// OnSelect -> MouseHoverEvent -> SetPreviewStatsDrinkUI again, parking a fresh preview.
        /// FocusNarrator would then append that preview to whatever label it speaks next, so
        /// filling the glass could announce "Brew, adds 2 sweetness" - a stat reading attached to a
        /// button that has no stats. Clearing on the commit path is what keeps the preview bound to
        /// the ingredient the player is CONSIDERING rather than one they already used.
        ///
        /// POSTFIX, and this ordering is the whole point. AddIngredient's re-focus (and therefore
        /// the re-preview it triggers) happens INSIDE the method, before it returns - Select() runs
        /// OnSelect synchronously. A prefix would clear the old preview and then let the new one be
        /// parked anyway, which is the bug this guards against. Clearing on the way out is what
        /// actually leaves the channel empty when FocusNarrator reads it next frame.
        ///
        /// The player still hears the commit itself: BrewingPatches.AfterAddIngredient announces
        /// "Added honey. Glass: ... 2 of 3." That line reports the glass, which is the question a
        /// just-added ingredient raises - and F9 gives the exact stat totals on demand.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_DrinkManager), nameof(TG_DrinkManager.AddIngredient))]
        public static void AfterAddIngredientClearPreview()
        {
            PendingStats = null;
        }

        /// <summary>
        /// Phrases what an ingredient would ADD, in the game's own stat vocabulary.
        ///
        /// Only non-zero stats are named. Most ingredients move one or two of the four, so naming
        /// all four would bury the signal in "0 bitterness, 0 coolness" every single time - and the
        /// player is scanning a row of ten ingredients with this line repeating on each one.
        ///
        /// An ingredient that changes NOTHING is called out explicitly rather than passed over in
        /// silence. Silence is indistinguishable from a broken hook, and "adds nothing" is real
        /// information: it is how the player learns an ingredient is wrong for this slot.
        /// </summary>
        private static string DescribeDelta(int sweet, int bitter, int warm, int cool)
        {
            StringBuilder sb = new StringBuilder();

            Append(sb, sweet, "sweetness");
            Append(sb, bitter, "bitterness");
            Append(sb, warm, "warmth");
            Append(sb, cool, "coolness");

            if (sb.Length == 0) return "adds nothing";
            return "adds " + sb;
        }

        /// <summary>Adds one "N stat" clause if the stat actually moves.</summary>
        private static void Append(StringBuilder sb, int value, string statName)
        {
            if (value == 0) return;
            if (sb.Length > 0) sb.Append(", ");

            // Negative contributions exist in the data (an ingredient can cool a warm drink), and
            // "adds -1 warmth" is a number a screen reader reads as "adds minus one". Say it as a
            // removal, which is what the bar does.
            if (value < 0) sb.Append("removes ").Append(-value).Append(' ').Append(statName);
            else sb.Append(value).Append(' ').Append(statName);
        }

        /// <summary>
        /// Speaks the glass's current stats on demand.
        ///
        /// A QUERY KEY IS THE RIGHT SHAPE HERE, not a running commentary. The stats panel is a
        /// TOGGLE for sighted players too - BrewInformationClick swaps statsBarManager against
        /// brewInfoPanel - so they are not always on screen even for someone who can see. A key
        /// that answers "where am I now" on request matches the game's own model, and keeps the
        /// per-ingredient preview line short enough to scan a row with.
        ///
        /// Reads the manager's committed sweetness/bitterness/warmth/coolness fields - the same
        /// four ints SetStatsDrinkUI hands to the bars - so what is spoken and what is drawn come
        /// from one source and cannot drift.
        /// </summary>
        internal static void SpeakCurrentStats()
        {
            try
            {
                TG_DrinkManager mgr = FindDrinkManager();
                if (mgr == null)
                {
                    // Naming the gap, rather than staying silent, is what makes a wrong-screen
                    // keypress diagnosable by ear instead of by log.
                    Announce("Brewing stats are not available here.");
                    return;
                }

                int max = SegmentCount(mgr);

                StringBuilder sb = new StringBuilder();
                AppendStat(sb, "Sweetness", GetInt(mgr, "sweetness"), max);
                AppendStat(sb, "bitterness", GetInt(mgr, "bitterness"), max);
                AppendStat(sb, "warmth", GetInt(mgr, "warmth"), max);
                AppendStat(sb, "coolness", GetInt(mgr, "coolness"), max);

                Announce(sb.ToString());
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Stats] query threw: " + e.Message);
                Announce("Could not read brewing stats.");
            }
        }

        /// <summary>Adds one "stat N of M" clause. Every stat is named on the query path.</summary>
        private static void AppendStat(StringBuilder sb, string name, int value, int max)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(name).Append(' ').Append(value);

            // The denominator is only spoken when we actually know it. Inventing "of 5" from a
            // guess would be worse than omitting it: the player would calibrate against a scale
            // the game does not use.
            if (max > 0) sb.Append(" of ").Append(max);
        }

        /// <summary>
        /// How many segments each bar has - the denominator a sighted player is counting against.
        ///
        /// Measured off the live fillBarStatsImage array rather than hardcoded, because it is
        /// authored per-prefab in the Unity scene and the demo build is not evidence for what the
        /// retail build ships. Returns 0 (meaning "unknown") if anything in the chain is missing,
        /// and the caller then omits the denominator rather than stating a wrong one.
        /// </summary>
        private static int SegmentCount(TG_DrinkManager mgr)
        {
            try
            {
                object bars = AccessTools.Field(typeof(TG_DrinkManager), "statsBarManager")?.GetValue(mgr);
                if (bars == null) return 0;

                object bar = AccessTools.Field(bars.GetType(), "sweetnessStatsBar")?.GetValue(bars);
                if (bar == null) return 0;

                Array images = AccessTools.Field(bar.GetType(), "fillBarStatsImage")?.GetValue(bar) as Array;
                return images == null ? 0 : images.Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Finds the active drink manager.
        ///
        /// Goes through TG_GameManager's singleton first because that is the reference the game's
        /// own TG_IngredientButton.Init falls back to, so it is the manager the buttons are wired
        /// to. FindObjectOfType is the backstop for scenes where the singleton is not up yet, and
        /// it also picks up TG_EndlessModeDrinkManager, which subclasses TG_DrinkManager and holds
        /// the same four stat fields.
        /// </summary>
        private static TG_DrinkManager FindDrinkManager()
        {
            try
            {
                TG_GameManager gm = TG_GenericSingelton<TG_GameManager>.Instance;
                if (gm != null && gm.drinkManager != null) return gm.drinkManager;
            }
            catch (Exception)
            {
                // Singleton not up in this scene - fall through.
            }

            try
            {
                return UnityEngine.Object.FindObjectOfType<TG_DrinkManager>();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Reads a private int off the manager. All four stats are genuine FIELDS
        /// (confirmed by reflecting over the shipped Assembly-CSharp.dll, not from the decompile,
        /// which renders some properties as fields and would silently read null).</summary>
        private static int GetInt(object target, string field)
        {
            object v = AccessTools.Field(target.GetType(), field)?.GetValue(target);
            return v is int ? (int)v : 0;
        }

        /// <summary>Reads a private bool off the manager.</summary>
        private static bool GetBool(object target, string field)
        {
            object v = AccessTools.Field(target.GetType(), field)?.GetValue(target);
            return v is bool && (bool)v;
        }

        /// <summary>
        /// Speaks an on-demand stats line. TextType.Menu (interface feedback, no speaker prefix)
        /// and interrupt:true, matching BrewingPatches - the player asked for this right now.
        /// </summary>
        private static void Announce(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            ISpeechOutput speech = Speech;
            if (speech == null || !speech.IsAvailable) return;

            MelonLogger.Msg("[Stats] " + line);
            speech.SpeakAs(null, line, TextType.Menu, true);
        }
    }
}
