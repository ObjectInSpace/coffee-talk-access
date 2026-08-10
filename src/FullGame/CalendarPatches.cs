using System;
using System.Text;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeTalkAccess.FullGame
{
    /// <summary>
    /// Speaks the save/load calendar - the grid of days a player picks from to load a story.
    ///
    /// ⚠ THIS IS A FUNCTIONAL SCREEN, NOT A GALLERY, WHICH IS WHY IT WAS BUILT AHEAD OF THE REST
    /// OF PHASE 5'S REMAINING LIST. Calendar, gallery, comics, endings and credits were grouped
    /// together in PLAN.md as one bucket. Measuring them (2026-08-10) split the bucket: gallery,
    /// comics, endings and credits are VISUAL surfaces whose content is images, carrying almost no
    /// localized text at all (the only terms any of those classes reference are `generalUI/page`
    /// and `generalUI/artByGallery` - chrome, not content). The calendar is the exception: it is
    /// how a player LOADS A SAVE, its content is dates and times, and every word of it is readable.
    ///
    /// ⚠ THE OBVIOUS CLASS IS THE WRONG ONE. `TG_CalendarContent` looks like the day cell and is
    /// NOT what the load screen uses - it implements only IPointerEnterHandler/IPointerExitHandler,
    /// so it is genuinely mouse-only and a hook there would never fire on keyboard. The load grid is
    /// built from `TG_CalendarLoadUI`, which extends TG_Button (hence ISelectHandler, hence keyboard
    /// focus) and carries a `hoverAction` - the same shape as the achievement icons. Checking which
    /// of the two the screen actually instantiates is what separates a working hook from a silent
    /// one, and the two classes are one letter apart in a file listing.
    ///
    /// FOCUS IS REAL AND THE GAME OWNS IT. TG_SaveMenuManager wires explicit four-way Navigation
    /// across the grid and deliberately SKIPS DISABLED CELLS (it walks by 7 for up/down - a week
    /// per row - until it finds an enabled one), so the player never lands on a day that has no
    /// save. The mod adds no cursor and never calls Select().
    ///
    /// ⚠ UNTESTED. The demo's main menu may not expose a load screen at all; this may be dead code
    /// until the retail build, exactly like the newspaper. It is still worth building now: the data
    /// and the classes are fully present, and an untestable hook that ATTACHES is a different
    /// problem from one that is missing (see Main.VerifyExpectedPatches).
    /// </summary>
    [HarmonyPatch]
    public static class CalendarPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        /// <summary>
        /// Label for the calendar day that just took focus, consumed by FocusNarrator as the
        /// control's label. Same one-shot read-and-clear channel as PendingAchievement.
        /// </summary>
        internal static string PendingDay;

        /// <summary>
        /// Captures the date panel the game just filled in for the focused day.
        ///
        /// Hooked on TG_CalendarUIManager.SetSelectedData rather than on the hover handler because
        /// this is where the text actually lands: OnHoverCalender does the blinking and then calls
        /// through to here, and this method is also reachable from TG_SaveMenuManager's direct
        /// MouseHoverEvent() calls when the screen opens on the current day. One hook, every route.
        ///
        /// ⚠ SetSelectedData's ELSE branch blanks all four fields, and it does NOT mean "no save".
        /// The condition is `day >= TG_Static.dailyDataList.Count` - a day PAST THE END OF THE
        /// STORY, i.e. a grid cell the calendar drew to fill out its last week. An earlier version
        /// of this file called that "no save" and spoke it as such, which is a different and much
        /// more alarming claim than the truth.
        ///
        /// Days that merely have no save are a SEPARATE mechanism and never reach this branch:
        /// TG_SaveMenuManager.RefreshCalenderLoadSlotUI sets `button.interactable =
        /// profileData.CheckUnlockedDay(i + 1)`, and the navigation graph then walks PAST any cell
        /// whose button is not enabled. So a locked day is normally unreachable by keyboard, and
        /// if one is ever landed on, Describe() already appends ", unavailable".
        ///
        /// It must still be spoken rather than falling silent, or an out-of-range day is
        /// indistinguishable from a broken hook.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_CalendarUIManager), nameof(TG_CalendarUIManager.SetSelectedData))]
        public static void AfterSetSelectedData(TG_CalendarUIManager __instance, int day)
        {
            try
            {
                if (__instance == null) return;

                object selected = AccessTools.Field(typeof(TG_CalendarUIManager), "selectedDataUI")
                    ?.GetValue(__instance);

                string dayNumber = ReadText(selected, "dayNumberText");
                string dayName = ReadText(selected, "dayNameText");
                string date = ReadText(selected, "dateText");

                StringBuilder sb = new StringBuilder();

                // The three fields are blanked together for a day past the end of the story.
                // Reporting the day NUMBER still tells the player where the cursor is, which is the
                // one thing they cannot otherwise know on a cell with no text.
                //
                // ⚠ `day` is a ZERO-BASED index; every number the game DISPLAYS is one higher.
                // TG_CalendarUIManager passes `day + 1` to GetDayNumberFormatLocalization, and
                // TG_SaveMenuManager checks `CheckUnlockedDay(i + 1)`. Speaking the raw index put
                // the mod's only spoken number one behind every label on the screen.
                if (string.IsNullOrEmpty(dayNumber) && string.IsNullOrEmpty(dayName) && string.IsNullOrEmpty(date))
                {
                    sb.Append("Day ").Append(day + 1).Append(", no story");
                }
                else
                {
                    if (!string.IsNullOrEmpty(dayNumber)) sb.Append(dayNumber);

                    // dayNameText carries a trailing comma in the game's own layout
                    // ("Monday,") because it is drawn next to the date. Spoken text does not want
                    // the doubled punctuation that would produce.
                    if (!string.IsNullOrEmpty(dayName))
                    {
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(dayName.TrimEnd(',', ' '));
                    }

                    if (!string.IsNullOrEmpty(date))
                    {
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(date);
                    }
                }

                PendingDay = sb.ToString();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Calendar] day hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Composes the last-played save line - the "Continue" affordance rather than a grid cell.
        ///
        /// Its text fields are assembled by the game into a single popup body
        /// (TG_CalendarUIManager:193 joins them with newlines). Read together for the same reason
        /// the newspaper is read as one announcement: four separate Speaks would interrupt each
        /// other and leave only the last audible.
        ///
        /// ⚠ THIS DOES NOT SPEAK IMMEDIATELY, and that is the fix for a defect this file shipped
        /// with. TG_SaveMenuManager.RefreshSlot calls SetLastPlayedData() from inside OpenSaveMenu,
        /// which THEN waits out a 0.1 s realtime delay and a 0.6 s fade before calling
        /// SelectLastPlayedCalendar(). Speaking here with interrupt:true meant the continue summary
        /// fired ~0.7 s before the grid took focus and was then cut off by the focused day's own
        /// announcement. The file's own newspaper rationale - one control, one utterance - was not
        /// being applied to itself.
        ///
        /// So the line is PARKED and spoken by the entry watcher once the screen is actually live,
        /// without interrupting. Same shape as the achievements entry line, and for the same
        /// reason: the screen becomes usable inside coroutine/DOTween callbacks with no method
        /// worth postfixing.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_CalendarUIManager), nameof(TG_CalendarUIManager.SetLastPlayedData))]
        public static void AfterSetLastPlayedData(TG_CalendarUIManager __instance)
        {
            try
            {
                if (__instance == null) return;

                object lastPlayed = AccessTools.Field(typeof(TG_CalendarUIManager), "lastPlayedDataUI")
                    ?.GetValue(__instance);
                if (lastPlayed == null) return;

                // ⚠ The three fields InitLastPlayInformation actually WRITES are dayNumberText,
                // dayText and clockText. `lastPlayedText` is scene-authored chrome - nothing in the
                // decompiled game ever assigns it - so it must not be the thing that decides
                // whether there is anything to say. Its ELSE branch (no quicksave) blanks the other
                // three and leaves this one set, so keying the emptiness check on the whole
                // StringBuilder would announce a bare "Last played" with no data behind it.
                string caption = ReadText(lastPlayed, "lastPlayedText");
                string dayNumber = ReadText(lastPlayed, "dayNumberText");
                string dayText = ReadText(lastPlayed, "dayText");
                string clock = ReadText(lastPlayed, "clockText");

                // Disarm rather than merely returning: SetLastPlayedData runs on every RefreshSlot,
                // so a profile that HAD a quicksave and then no longer does must not leave the
                // previous run's summary armed and waiting to be spoken.
                if (string.IsNullOrEmpty(dayNumber) && string.IsNullOrEmpty(dayText)
                    && string.IsNullOrEmpty(clock))
                {
                    EntryWatcher.Reset();
                    return;
                }

                StringBuilder sb = new StringBuilder();
                Append(sb, caption);
                Append(sb, dayNumber);
                Append(sb, dayText);
                Append(sb, clock);

                if (sb.Length == 0)
                {
                    EntryWatcher.Reset();
                    return;
                }

                EntryWatcher.Arm(__instance, sb.ToString());
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Calendar] last-played hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Speaks the continue-summary once the load screen is actually on screen.
        ///
        /// Polled rather than spoken from the postfix for the reason in AfterSetLastPlayedData: the
        /// moment the screen becomes usable is inside OpenSaveMenu's coroutine, past a realtime
        /// wait and a DOTween fade, with no method of our own to hook. Same shape as
        /// AchievementPatches.EntryWatcher and NameEntryPatches.NameScreenWatcher.
        /// </summary>
        internal static class EntryWatcher
        {
            private static TG_CalendarUIManager _pending;
            private static string _line;

            internal static void Arm(TG_CalendarUIManager mgr, string line)
            {
                _pending = mgr;
                _line = line;
            }

            /// <summary>Clears any armed announcement. Called when the screen goes away.</summary>
            internal static void Reset()
            {
                _pending = null;
                _line = null;
            }

            internal static void Update()
            {
                try
                {
                    if (_pending == null || string.IsNullOrEmpty(_line)) return;

                    // Checked rather than timed: a fixed delay would drift with frame rate and with
                    // the fade's independent update mode.
                    if (!_pending.gameObject.activeInHierarchy) return;

                    string line = _line;
                    Reset();

                    // interrupt:false - this is context the player did not ask for at this instant,
                    // so it must never cut off a line already being read. If the grid takes focus
                    // first, the day cell is heard first and this follows it, which is the right
                    // order anyway.
                    Announce(line, false);
                }
                catch (Exception e)
                {
                    Reset();
                    MelonLogger.Warning("[Calendar] entry watcher threw: " + e.Message);
                }
            }
        }

        /// <summary>
        /// Drops both parked channels when the player leaves the load screen.
        ///
        /// An un-cleared parked day label becomes the caption of the next control focused, and an
        /// un-spoken entry line would fire on a later, unrelated screen. Same fix the achievements
        /// screen needed on BackToExtrasMenu.
        ///
        /// Both exits are covered: BackToMainMenu from the main-menu route and BackToPauseInGameMenu
        /// from the in-game pause route. TG_CalendarUIManager.Initialize picks between them on
        /// TG_Static.currentScene, so hooking only one would leave the channels armed on the other.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_SaveMenuManager), nameof(TG_SaveMenuManager.BackToMainMenu))]
        public static void AfterBackToMainMenu()
        {
            PendingDay = null;
            EntryWatcher.Reset();
        }

        /// <summary>Same cleanup for the in-game pause route out of the load screen.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_SaveMenuManager), nameof(TG_SaveMenuManager.BackToPauseInGameMenu))]
        public static void AfterBackToPauseInGameMenu()
        {
            PendingDay = null;
            EntryWatcher.Reset();
        }

        /// <summary>Adds one clause if it has content, comma-separated.</summary>
        private static void Append(StringBuilder sb, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(value.TrimEnd(',', ' '));
        }

        /// <summary>
        /// Reads a legacy UI.Text field off one of the calendar's sub-UI objects.
        ///
        /// Every text field on this screen is UI.Text, not TextMeshProUGUI - confirmed by
        /// reflecting over the shipped assembly. That is worth stating because this game mixes the
        /// two freely (the newspaper has three TMP fields and one UI.Text), so the type cannot be
        /// assumed from the screen it belongs to.
        /// </summary>
        private static string ReadText(object owner, string field)
        {
            if (owner == null) return string.Empty;

            object v = AccessTools.Field(owner.GetType(), field)?.GetValue(owner);
            Text text = v as Text;
            if (text == null || string.IsNullOrEmpty(text.text)) return string.Empty;

            return Dialogue.FungusText.ExtractWords(text.text);
        }

        /// <summary>
        /// Names a focused calendar day for FocusNarrator, consuming the parked phrase.
        ///
        /// Supplied as a LABEL rather than spoken here, for the reason recorded on the achievements
        /// and recipe screens: the day cells are icon-ish buttons whose date text lives on a
        /// separate panel, so FocusNarrator would announce them as "unlabeled" a frame later and
        /// interrupt anything this file said.
        /// </summary>
        internal static string TakePendingLabel()
        {
            string pending = PendingDay;
            PendingDay = null;
            return pending;
        }

        private static void Announce(string line, bool interrupt)
        {
            if (string.IsNullOrEmpty(line)) return;

            ISpeechOutput speech = Speech;
            if (speech == null || !speech.IsAvailable) return;

            MelonLogger.Msg("[Calendar] " + line);
            speech.SpeakAs(null, line, TextType.Menu, interrupt);
        }
    }
}
