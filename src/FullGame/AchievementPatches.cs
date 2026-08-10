using System;
using System.Text;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using TMPro;
using UnityAccessibilityLib;
using UnityEngine;

namespace CoffeeTalkAccess.FullGame
{
    /// <summary>
    /// Speaks the achievements screen: which achievement has focus, what it does, and how to
    /// unlock it.
    ///
    /// ⚠ UNTESTED - no run has reached this screen. But it is NOT untestable: the demo ships the
    /// FULL retail achievement set (72 `achievements/` localization keys in resources.assets,
    /// including entries the demo can never award), so the data behind every line spoken here is
    /// present. "The demo cannot EARN it" and "the data is not THERE" are different claims and only
    /// the second would be a reason to defer.
    ///
    /// WHY ONE HOOK COVERS THE WHOLE SCREEN. TG_AchievementMenuManager.SetSelectedData is the sole
    /// convergence point: it fills the name, description and how-to-unlock panels together, and it
    /// is reached from exactly one place - the `hoverAction` delegate that Init wires onto every
    /// icon. Every route into that panel (mouse, keyboard, gamepad) goes through the icon's
    /// MouseHoverEvent, so patching the sink catches them all rather than one patch per input path.
    ///
    /// ⚠ CONVERGING IS NOT THE SAME AS FIRING ONCE - it runs at least TWICE per focus move.
    /// TG_AchievementIconUI.MouseHoverEvent calls `button.OnSelect(null)` and then `button.Select()`,
    /// and TG_Button.OnSelect's body is a bare MouseHoverEvent() - so the override re-enters itself
    /// and hoverAction fires again, with Select() able to raise a further EventSystem select on top.
    /// This design is unharmed (the parked phrase is idempotent: last write wins, and the label is
    /// consumed once by the focus watcher), but do NOT add anything here that counts calls, speaks
    /// directly, or accumulates. Same trap as TG_DrinkRecipesApp.RefreshList calling DisplayDrinks
    /// twice, and as SayDialog.Say firing 2x - check the CALLERS for re-entry, not only that they
    /// all converge.
    ///
    /// ⚠ MOUSEHOVEREVENT IS NOT MOUSE-ONLY - the same verified fact StatsPatches depends on.
    /// TG_AchievementIconUI extends TG_Button, which implements ISelectHandler with a bare
    /// MouseHoverEvent() body, so ordinary keyboard focus already runs this whole chain. The grid's
    /// explicit Navigation (built in Init, wrapping in all four directions) means the EventSystem
    /// really does move between icons. Nothing here drives focus.
    ///
    /// ⚠ WHY THIS PARKS A PHRASE INSTEAD OF SPEAKING. The icons are Selectables, so FocusNarrator
    /// sees them take focus and announces them from Update() a frame later with interrupt:true - and
    /// it would announce them as "unlabeled", because achievement icons carry no Text at all (the
    /// same icon-button shape as the brewing ingredients). A self-speaking hook here would be cut
    /// off mid-word by that useless line. So this writes to PendingAchievement and FocusNarrator
    /// uses it as the LABEL, exactly as GetIngredientName supplies one for an ingredient icon: one
    /// control, one utterance.
    ///
    /// ⚠ HIDDEN ACHIEVEMENTS ARE MASKED, AND THE MASK IS MIRRORED, NOT INVENTED. A hidden,
    /// un-earned achievement shows literal "????" in all three panels. Speaking the underlying
    /// TG_AchievementData strings instead would leak content the sighted player does not have -
    /// the same rule the chat log follows for un-introduced speaker names. We read the RESOLVED
    /// panel text (what the game decided to show), never the source data, so the masking cannot
    /// drift from the game's own.
    /// </summary>
    [HarmonyPatch]
    public static class AchievementPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        /// <summary>
        /// Label for the achievement icon that just took focus, consumed by FocusNarrator on the
        /// next Update. Same one-shot read-and-clear channel as StatsPatches.PendingStats: written
        /// by whoever knows the fact, cleared by whoever speaks it.
        /// </summary>
        internal static string PendingAchievement;

        /// <summary>
        /// Captures the achievement panel the game just filled in.
        ///
        /// Postfix, and reading the three PANEL components rather than the TG_AchievementData
        /// fields, because the panels are where the game's hidden-achievement masking has already
        /// been applied. Reading the data would re-implement that decision - and get it wrong the
        /// first time the retail build changes the rule.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_AchievementMenuManager), nameof(TG_AchievementMenuManager.SetSelectedData))]
        public static void AfterSetSelectedData(TG_AchievementMenuManager __instance, TG_AchievementIconUI achievementIConUI)
        {
            try
            {
                if (__instance == null) return;

                string name = ReadTmp(__instance, "achievementNameText");
                string description = ReadTmp(__instance, "achievementDescriptionText");
                string howTo = ReadTmp(__instance, "achievementHowToUnlockText");

                PendingAchievement = Compose(name, description, howTo, achievementIConUI)
                    + DescribePosition(__instance, achievementIConUI);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Achievement] select hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Arms the entry announcement when the screen is built.
        ///
        /// ⚠ INIT IS NOT THE MOMENT THE SCREEN OPENS - it runs roughly a second too early.
        /// TG_MainMenuManager.OpenAchievement calls Init() FIRST, then fades a cover panel in over
        /// 0.5s, only then activates the manager's GameObject, fades back out over another 0.5s, and
        /// finally calls SelectFirstButton(). Speaking from this postfix put "Achievements, 14 of 72"
        /// into the middle of the extras menu, a second before the grid existed and before any icon
        /// had focus - and with interrupt:true it could cut the extras-menu line that was still
        /// being read. So this only ARMS; the watcher below speaks when the panel is really up.
        ///
        /// ⚠ Init also RE-RUNS on every entry to the screen: its `initialized` flag guards only the
        /// prefab instantiation, while the per-icon loop and the progress text are rebuilt each
        /// time. That is correct for re-entry (the counts may have changed) and is why the watcher
        /// re-arms rather than latching once for the session.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_AchievementMenuManager), nameof(TG_AchievementMenuManager.Init))]
        public static void AfterInit(TG_AchievementMenuManager __instance)
        {
            if (__instance == null) return;
            EntryWatcher.Arm(__instance);
        }

        /// <summary>
        /// Speaks the screen's entry line once the achievement panel is actually on screen.
        ///
        /// Polled rather than postfixed for the reason in AfterInit: the moment the screen becomes
        /// usable is inside a chain of DOTween callbacks with no method of our own to hook. The same
        /// shape as NameEntryPatches.NameScreenWatcher, which exists for exactly this problem.
        /// </summary>
        internal static class EntryWatcher
        {
            private static TG_AchievementMenuManager _pending;

            internal static void Arm(TG_AchievementMenuManager mgr)
            {
                _pending = mgr;
            }

            /// <summary>Clears any armed announcement. Called when the screen goes away.</summary>
            internal static void Reset()
            {
                _pending = null;
            }

            internal static void Update()
            {
                try
                {
                    if (_pending == null) return;

                    // The manager's own GameObject is what OpenAchievement activates after the first
                    // fade, so its activeInHierarchy IS the "screen is up" signal. Checked rather
                    // than timed: a fixed delay would drift with frame rate and with the tween's
                    // independent update mode.
                    if (!_pending.gameObject.activeInHierarchy) return;

                    TG_AchievementMenuManager mgr = _pending;
                    _pending = null;

                    object progress = AccessTools.Field(typeof(TG_AchievementMenuManager), "achievementProgressNumberText")
                        ?.GetValue(mgr);

                    // The progress label is a legacy UI.Text while the three detail panels are
                    // TextMeshProUGUI. Mixing the two in one screen is this game's habit (the
                    // newspaper does the same), and assuming one type here would read nothing.
                    UnityEngine.UI.Text text = progress as UnityEngine.UI.Text;
                    string count = text != null ? text.text : null;

                    // "14 of 72" sits on screen permanently for a sighted player, so it is context
                    // they hold the whole time they browse and we have no way to glance at. Read off
                    // the TEXT rather than recomputing CalculateGameProgression(): the number the
                    // player can be told about is the one actually displayed.
                    //
                    // Spoken even when the count is unreadable: entering a screen and hearing
                    // nothing is indistinguishable from the mod being dead.
                    Announce(string.IsNullOrEmpty(count)
                        ? "Achievements. Arrow keys to move through the grid."
                        : "Achievements, " + count.Replace("/", " of ") + " unlocked. Arrow keys to move through the grid.",
                        false);
                }
                catch (Exception e)
                {
                    _pending = null;
                    MelonLogger.Warning("[Achievement] entry watcher threw: " + e.Message);
                }
            }
        }

        /// <summary>
        /// Drops the screen's state when the player leaves.
        ///
        /// Both an armed-but-unspoken entry line and a parked icon label must die here. A pending
        /// label that outlives the screen would be taken by FocusNarrator as the caption for
        /// whatever control the extras menu focuses next - an achievement's description spoken over
        /// a menu button, which is worse than saying nothing.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_AchievementMenuManager), nameof(TG_AchievementMenuManager.BackToExtrasMenu))]
        public static void AfterBackToExtrasMenu()
        {
            EntryWatcher.Reset();
            PendingAchievement = null;
        }

        /// <summary>
        /// Builds the spoken line for one achievement.
        ///
        /// The ORDER is name, then state, then description, then how-to-unlock. A player arrowing
        /// across a 72-icon grid is scanning names; putting the unlock condition first would make
        /// every icon start with a paragraph. Screen readers interrupt on the next arrow press
        /// anyway, so the front of the line is the part that reliably gets heard.
        ///
        /// ⚠ "Locked"/"unlocked" is stated EXPLICITLY, because it is not otherwise audible. A
        /// sighted player sees it instantly - the icon is a different sprite (achieved vs
        /// unachieved vs the hidden placeholder). Without it the player cannot tell what they have
        /// already done, which is the entire question this screen answers.
        /// </summary>
        private static string Compose(string name, string description, string howTo, TG_AchievementIconUI icon)
        {
            StringBuilder sb = new StringBuilder();

            bool unlocked = ReadUnlocked(icon);

            // ⚠ A masked name means hidden ONLY while the achievement is still locked. The game
            // masks on `hiddenAchievement && !unclocked` (TG_AchievementMenuManager:164-166), so an
            // UNLOCKED achievement is never masked by that rule - and if its name still comes back
            // "????" the cause is a missing localization key, not secrecy. DirectLocalization on an
            // absent term is exactly how that surfaces. Testing the mask alone announced such a row
            // as "Hidden achievement, locked": both halves wrong, and the second half wrong about
            // the one question this screen exists to answer. Read the STATE first, and let the mask
            // decide only what to say about an achievement already known to be locked.
            bool hidden = !unlocked && IsMasked(name);

            if (hidden)
            {
                // Say "hidden" rather than reading "????" out, which a screen reader voices as a
                // run of question marks or as nothing at all. This is a real, meaningful state -
                // the game is deliberately withholding it - so it gets a word, not a symbol.
                sb.Append("Hidden achievement, locked");

                // A hidden achievement masks all three panels, so there is nothing further to say
                // and appending the masked description would repeat "????" twice more.
                return sb.ToString();
            }

            // A "????" reaching here is NOT the secrecy mask (that was handled above, and requires
            // the row to be locked) - it is a missing localization term. Speaking it raw would make
            // a screen reader read out a run of question marks, or nothing; naming it as a gap keeps
            // it diagnosable by ear, the same reason unlabeled controls say so out loud.
            if (IsMasked(name)) sb.Append("Achievement, name unavailable");
            else if (!string.IsNullOrEmpty(name)) sb.Append(name);
            else sb.Append("Achievement, unlabeled");

            sb.Append(unlocked ? ", unlocked" : ", locked");

            if (!string.IsNullOrEmpty(description) && !IsMasked(description))
                sb.Append(". ").Append(description);

            // The how-to-unlock line is only useful while the achievement is still locked; once
            // earned it describes something the player already did.
            if (!unlocked && !string.IsNullOrEmpty(howTo) && !IsMasked(howTo))
                sb.Append(". To unlock: ").Append(howTo);

            return sb.ToString();
        }

        /// <summary>
        /// Appends the icon's place in the grid, as ", 14 of 72".
        ///
        /// ⚠ THIS IS A 72-CELL GRID AND EVERY EDGE WRAPS. Init builds explicit four-way Navigation
        /// in which the last icon's selectOnRight is icon 0, and selectOnDown past the bottom row
        /// returns to `i % GRIDHORIZONTALLENGTH` at the top (TG_AchievementMenuManager:108-139).
        /// A sighted player sees the cursor jump the length of the screen; without a spoken index
        /// the wrap is completely inaudible, and the player has no way to know they have looped or
        /// how far through the set they are. Position is the one piece of context a grid this size
        /// cannot do without - every other list in this mod already speaks it.
        ///
        /// Index comes from the manager's OWN list rather than from the icon's `indexButton`:
        /// TG_Button.SetMenuIndex is what fills that field and nothing in Init ever calls it, so it
        /// is 0 for every achievement. The list is the same one Init walks to build the navigation,
        /// so its ordering IS the grid's ordering.
        /// </summary>
        private static string DescribePosition(TG_AchievementMenuManager mgr, TG_AchievementIconUI icon)
        {
            try
            {
                if (icon == null) return string.Empty;

                object v = AccessTools.Field(typeof(TG_AchievementMenuManager), "achievementIconUIs")
                    ?.GetValue(mgr);
                System.Collections.IList list = v as System.Collections.IList;
                if (list == null || list.Count == 0) return string.Empty;

                int idx = list.IndexOf(icon);
                if (idx < 0) return string.Empty;

                return ", " + (idx + 1) + " of " + list.Count;
            }
            catch
            {
                // A missing index is a lost nicety, not a reason to lose the whole announcement -
                // the name and locked state matter more than the position.
                return string.Empty;
            }
        }

        /// <summary>
        /// True when the game masked this panel. Matched on the literal string the game writes
        /// (TG_AchievementMenuManager's ACHIEVEMENTS_HIDDEN const), trimmed because a localizer or
        /// a layout pass can pad it.
        /// </summary>
        private static bool IsMasked(string text)
        {
            return !string.IsNullOrEmpty(text) && text.Trim() == "????";
        }

        /// <summary>
        /// Reads the icon's earned flag.
        ///
        /// ⚠ The field is spelled `unclocked` in the shipped assembly - a typo for "unlocked",
        /// confirmed by reflecting over the real Assembly-CSharp rather than trusting the spelling
        /// to be sensible. Written out so nobody "fixes" it into a silent null read.
        /// </summary>
        private static bool ReadUnlocked(TG_AchievementIconUI icon)
        {
            if (icon == null) return false;

            object v = AccessTools.Field(typeof(TG_AchievementIconUI), "unclocked")?.GetValue(icon);
            return v is bool && (bool)v;
        }

        /// <summary>Reads one of the private TextMeshProUGUI detail panels.</summary>
        private static string ReadTmp(TG_AchievementMenuManager mgr, string field)
        {
            object v = AccessTools.Field(typeof(TG_AchievementMenuManager), field)?.GetValue(mgr);
            TextMeshProUGUI tmp = v as TextMeshProUGUI;
            return tmp == null ? string.Empty : Dialogue.FungusText.ExtractWords(tmp.text);
        }

        /// <summary>
        /// Names a focused achievement icon for FocusNarrator, consuming the parked phrase.
        ///
        /// Exposed rather than announced here for the reason in the class comment: the icons carry
        /// no Text, so FocusNarrator would otherwise speak "unlabeled" over the top of anything this
        /// file said. Supplying the label instead means the two cannot talk over each other.
        /// </summary>
        internal static string TakePendingLabel()
        {
            string pending = PendingAchievement;
            PendingAchievement = null;
            return pending;
        }

        /// <summary>
        /// Speaks a line for this screen.
        ///
        /// `interrupt` is a parameter rather than a constant because the entry line must NOT cut
        /// whatever the previous screen was still reading, while a per-icon line always should -
        /// the player's own arrow press supersedes it.
        /// </summary>
        private static void Announce(string line, bool interrupt)
        {
            if (string.IsNullOrEmpty(line)) return;

            ISpeechOutput speech = Speech;
            if (speech == null || !speech.IsAvailable) return;

            MelonLogger.Msg("[Achievement] " + line);
            speech.SpeakAs(null, line, TextType.Menu, interrupt);
        }
    }
}
