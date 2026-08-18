using System;
using System.Collections.Generic;
using System.Text;
using CoffeeTalkAccess.Dialogue;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;
using UnityEngine;

namespace CoffeeTalkAccess.FullGame
{
    /// <summary>
    /// Reads the SMARTPHONE newspaper app - the archive of past days' articles.
    ///
    /// ⚠ NOT THE SAME SCREEN AS NewspaperPatches. That file reads the physical morning paper
    /// (TG_NewspaperManager.GenerateNewspaper: a headline plus two short news items, generated from
    /// the newspaper/* term dictionaries and shown once at the start of a day). THIS file reads the
    /// phone app, whose content comes from an entirely different source - newspaperApp/title{N} and
    /// newspaperApp/content{N}, a 14-day archive of long-form columns. The two share a name and
    /// nothing else: different data, different keys, different screens. They are separate classes so
    /// that neither is tempted to "reuse" the other's text extraction, which would silently read the
    /// wrong paper.
    ///
    /// ⚠⚠ CANNOT BE REACHED ON THE DEMO, AND THIS WAS A DELIBERATE CHOICE (player, 2026-08-10).
    /// TG_SmartPhoneManager denies the phone in-fiction on the demo (canOpenSmartPhone == false), so
    /// the four app panels never activate and TG_NewspaperApp.Open never runs - see
    /// docs/PLAN.md phase 5 and the class comment in SmartPhonePatches. On top of that, this app's
    /// own Open() checks TG_ExpoBuildManager.Instance FIRST and, on a demo/expo build, shows
    /// pleaseSubscribeObject and returns WITHOUT running the day logic at all. So there are two
    /// independent gates between a demo player and this text.
    ///
    /// The offered alternative was a mod hotkey that would open this reader anywhere and be
    /// exercisable on the demo today; the player chose to hook only the game's own app, keeping the
    /// mod's "narrate the real screen, invent nothing" shape. That is a legitimate call and it is
    /// implemented faithfully here - but it means EVERY LINE BELOW IS UNTESTED AND UNTESTABLE UNTIL
    /// THE RETAIL BUILD IS AVAILABLE. It must not be quietly upgraded to "working" later; this
    /// comment is the record. What IS verified offline (2026-08-10) is the DATA: all 28 keys
    /// newspaperApp/title1..14 and newspaperApp/content1..14 are present in the demo's
    /// resources.assets, with real article bodies behind them.
    ///
    /// WHY A PARAGRAPH CURSOR. These are not headlines. content2 is a multi-page short story with
    /// song lyrics and quoted dialogue; several run past a dozen paragraphs. Spoken as one
    /// utterance an article is minutes long, cannot be paused, and gives a player no way to find
    /// their place again after an interruption - and the backquote repeat key would hand back the
    /// entire thing. So the article is split into paragraphs and stepped one at a time, the same
    /// entry-cursor shape ChatLogPatches uses.
    ///
    /// A SECOND CURSOR IS SAFE HERE, FOR THE SAME REASON IT WAS IN THE CHAT LOG. The standing rule
    /// is "announce what the game focuses, never build a second cursor" - it exists because a mod
    /// cursor that disagreed with the game's cursorIdx once quit the game. It does not apply to the
    /// article body: TG_NewspaperApp scrolls it with a Scrollbar (ScrollNews nudges
    /// scrollViewNews.verticalScrollbar.value by a continuous float from Up/Down). There is no row
    /// index, no Selectable and no EventSystem focus inside the article for ANY input device, so
    /// there is no selection to disagree with. The day arrows are the opposite case - leftButton and
    /// rightButton are real Buttons the game owns - so this class does NOT move between days itself.
    /// It reports the day the GAME switched to, by hooking the game's own SetNewsOnApp.
    /// </summary>
    [HarmonyPatch]
    public static class NewspaperAppPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        /// <summary>The article split into paragraphs, rebuilt every time the game sets a day.</summary>
        private static List<string> _paragraphs = new List<string>();

        /// <summary>Title of the article currently loaded, for the header and for re-orientation.</summary>
        private static string _title = string.Empty;

        /// <summary>
        /// Where the player is in the article. -1 means "not started reading yet", which is the
        /// state right after a day loads: the header has been spoken but no paragraph has, so the
        /// first Down should land on paragraph 1 rather than paragraph 2.
        /// </summary>
        private static int _cursor = -1;

        /// <summary>True while the app's panel is actually on screen.</summary>
        private static bool _open;

        /// <summary>
        /// The live app, captured when it opens so Update() can re-check that it is still on
        /// screen. Held as the typed instance rather than re-resolved through the singleton chain
        /// every frame, because the app is a plain MonoBehaviour with no singleton of its own - the
        /// only handle on it is the one the game hands us in a hook.
        /// </summary>
        private static TG_NewspaperApp _app;

        // Edge detection. The game's UpdateFunction uses IsPressed (continuous, for smooth
        // scrolling); a cursor must move ONCE per press or a single tap races through the article.
        private static bool _upHeld;
        private static bool _downHeld;

        /// <summary>
        /// Announces the app opening, and resets state.
        ///
        /// ⚠ Open() is patched rather than relying on SmartPhonePatches' base-class hook to carry
        /// the whole job: that one announces WHICH app opened, this one has to reset the cursor.
        /// Both fire, and that is intended - "Newspaper archive." followed by the day header reads
        /// naturally, and the base hook is what keeps an unknown future app audible.
        ///
        /// The subscribe-nag branch is detected and SAID OUT LOUD. On a demo/expo build Open()
        /// shows pleaseSubscribeObject and returns before loading any day, so the reader has nothing
        /// to read. Falling silent there is indistinguishable from a broken hook - the exact
        /// confusion that has cost this project live runs - so the player is told the build itself
        /// is withholding the content.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_NewspaperApp), nameof(TG_NewspaperApp.Open))]
        public static void AfterOpen(TG_NewspaperApp __instance)
        {
            try
            {
                // ⚠ DO NOT Reset() HERE. SetNewsOnApp runs INSIDE Open (TG_NewspaperApp:117, before
                // base.Open at :119), so by the time this POSTFIX runs, AfterSetNewsOnApp has
                // already loaded _title/_paragraphs and announced the header. Resetting now threw
                // all of that away and left _paragraphs EMPTY with _open true, so Update's
                // `_paragraphs.Count == 0` guard swallowed every arrow press and the article could
                // not be read.
                //
                // ⚠ THE SYMPTOM NAMED THE CAUSE, once the ordering was read: opening the app was
                // dead, but changing DAYS worked - because Left/Right call SetNewsOnApp with no
                // Open postfix behind them to undo it. Reported as "when I open the newspaper...
                // down arrow does not scroll" and "if I focus a different article then it works"
                // (2026-08-17). A screen that works on the second entry and not the first is this
                // shape of bug: something on the entry path is undoing the setup.
                //
                // Clearing the HELD-KEY flags is still right, and is the only part of the old Reset
                // that belongs here: the keypress that opened the app must not be read as a step.
                if (__instance == null)
                {
                    Reset();
                    return;
                }

                _upHeld = false;
                _downHeld = false;

                _app = __instance;
                _open = true;

                // Read the flag off the object the game actually toggled, rather than re-testing
                // TG_ExpoBuildManager ourselves: if the retail build ever reaches this branch by
                // another route, we still describe what is on screen.
                GameObject nag = __instance.pleaseSubscribeObject;
                if (nag != null && nag.activeInHierarchy)
                {
                    Announce("Newspaper archive. This build asks you to subscribe; no articles are available.");
                    _open = false;
                    return;
                }

                // Nothing else to say here: SetNewsOnApp runs inside Open() and announces the day
                // header itself. Speaking again would talk over it.
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[NewsApp] open hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Stops reading once the app is dismissed.
        ///
        /// ⚠ TG_NewspaperApp DOES NOT OVERRIDE Close() - it inherits it from TG_SmartPhoneApps,
        /// which takes a HistoryBackButtonData. Patching `typeof(TG_NewspaperApp), "Close"` would
        /// therefore fail to resolve a method at patch time. The base is patched instead, and the
        /// instance is filtered here, so the hook only acts when it is OUR app being closed.
        ///
        /// Closing the phone entirely is a separate path (CloseSmartPhone) that never calls this,
        /// which is why Update() re-checks the panel below rather than trusting this flag alone.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_SmartPhoneApps), nameof(TG_SmartPhoneApps.Close))]
        public static void AfterClose(TG_SmartPhoneApps __instance)
        {
            if (!(__instance is TG_NewspaperApp)) return;

            if (_open) MelonLogger.Msg("[NewsApp] closed.");
            Reset();
        }

        /// <summary>
        /// Loads the article for whichever day the game just switched to, and speaks the header.
        ///
        /// SetNewsOnApp IS THE RIGHT HOOK because it is the single convergence point for every way
        /// the displayed day can change: Open() calls it on entry, and LeftButtonClick /
        /// RightButtonClick both call it after moving `displayday`. Hooking the two button handlers
        /// instead would miss the initial load and duplicate the logic.
        ///
        /// ⚠ THE TEXT IS READ BACK OFF THE UI COMPONENTS, NOT RE-DERIVED FROM THE KEYS. The app has
        /// already resolved newspaperApp/content{day+1} through the localizer AND applied its own
        /// empty-string fallback to day 1 by the time this postfix runs. Re-doing that lookup here
        /// would mean maintaining a second copy of the day-index and fallback rules - two places to
        /// drift instead of one - and would read the wrong day the moment either changes. Reading
        /// newsDetailContentUI is how we inherit all of it for free, in whatever language the player
        /// has selected.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_NewspaperApp), nameof(TG_NewspaperApp.SetNewsOnApp))]
        public static void AfterSetNewsOnApp(TG_NewspaperApp __instance)
        {
            try
            {
                if (__instance == null) return;

                // SetNewsOnApp also runs from inside Open(), so this is the hook that reliably has
                // the instance even if the open postfix ordering ever changes.
                _app = __instance;
                _open = true;
                _cursor = -1;

                TG_NewsDetailContentUI ui = __instance.newsDetailContentUI;
                if (ui == null)
                {
                    Announce("Newspaper archive, article unavailable.");
                    return;
                }

                _title = CleanLine(TextOf(ui.newsTitleText));
                _paragraphs = SplitParagraphs(TextOf(ui.newsContentText));

                StringBuilder sb = new StringBuilder("Newspaper");

                // The date is what the screen puts at the top, and it is how the player knows which
                // day's paper they landed on after pressing an arrow.
                string date = CleanLine(TextOf(__instance.dateText));
                if (date.Length > 0) sb.Append(", ").Append(date);

                // Which day of how many. Read off the app's own private fields, because that is the
                // ground truth for what the arrows will do; it degrades silently if absent rather
                // than blocking the announcement.
                string position = DescribePosition(__instance);
                if (position.Length > 0) sb.Append(". ").Append(position);

                if (_title.Length > 0) sb.Append(". ").Append(_title);

                if (_paragraphs.Count == 0)
                {
                    // The screen exists but carried no readable body. Say so - silence is
                    // indistinguishable from the hook not firing.
                    sb.Append(". No article text.");
                    Announce(sb.ToString());
                    return;
                }

                sb.Append(". ").Append(_paragraphs.Count)
                  .Append(_paragraphs.Count == 1 ? " paragraph" : " paragraphs")
                  .Append(". Down arrow to read, left and right for other days.");

                Announce(sb.ToString());
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[NewsApp] day hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Steps the paragraph cursor.
        ///
        /// Driven from OnUpdate rather than from a patch on the app's UpdateFunction, matching
        /// ChatLogPatches: a fault here can then never throw inside the game's own frame callback,
        /// and the cursor keeps working if the retail build routes the app's update differently.
        ///
        /// The arrows are deliberately NOT consumed - the game's ScrollNews still pixel-scrolls the
        /// view underneath. They are not fighting: the scroll moves what a sighted onlooker sees,
        /// the cursor moves what is spoken, and nothing reads the scrollbar back, so the two cannot
        /// desynchronise into a wrong announcement.
        /// </summary>
        internal static void Update()
        {
            if (!_open) return;

            try
            {
                if (_paragraphs.Count == 0) return;

                // The app can leave the screen by paths that never call Close() - closing the whole
                // phone (CloseSmartPhone) tears the panel down directly, and the demo's
                // CloseUnaccesablePhone slides it away on a timer. Trust the panel, not our own
                // flag: a stale cursor would keep speaking article paragraphs at a player who is
                // back at the counter pressing Down to brew.
                if (!IsPanelOpen())
                {
                    _open = false;
                    return;
                }

                // ⚠ A PANEL THAT IS STILL ON SCREEN DOES NOT MEAN IT STILL OWNS THE ARROWS.
                //
                // Pausing does NOT tear the phone down: PauseGame raises pauseOverlayPanel and
                // gamePausedCanvasPanel over it and flips the state to MENU_IN_GAME
                // (TG_OptionsUIManager:551-557), leaving the newspaper panel active underneath. So
                // IsPanelOpen stays true and this reader kept stepping paragraphs while the pause
                // menu moved its own cursor - reported 2026-08-17 as "when I press escape and move
                // up or down, it moves both cursors".
                //
                // This is the same shape as the popup case FocusRecovery documents: the screen
                // behind a modal keeps responding because nothing disabled it. The answer there and
                // here is to consult the game's own STATE rather than the panel's visibility - the
                // state is what the game itself uses to decide who receives input.
                //
                // ⚠ DO NOT clear _open here. Pausing is not leaving the app: the player resumes back
                // into the same article, and dropping the cursor would restart them at the header
                // having lost their place. We stand down for as long as something else owns input
                // and pick up exactly where we were.
                string state = AccessMod.ReadControllerState();
                if (state != "PHONE_NEWSPAPER" && state != "PHONE_NEWSPAPER_DETAIL")
                {
                    // Forget the held-key edges too, so the arrow still being down when the pause
                    // menu closes is not read as a fresh step the moment we resume.
                    _upHeld = false;
                    _downHeld = false;
                    return;
                }

                bool up = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W);
                bool down = Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S);

                if (up && !_upHeld) Step(-1);
                if (down && !_downHeld) Step(+1);

                _upHeld = up;
                _downHeld = down;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[NewsApp] update threw: " + e.Message);
            }
        }

        /// <summary>
        /// Moves the cursor one paragraph and speaks it.
        ///
        /// At either end we say so rather than silently refusing: a player who cannot see the
        /// scrollbar has no other way to learn they have reached the end of the article, and an
        /// unexplained non-response reads as a broken control.
        /// </summary>
        private static void Step(int delta)
        {
            int count = _paragraphs.Count;
            if (count <= 0) return;

            // First Down from the header should land on paragraph 1, not skip it. First Up from the
            // header re-reads the last paragraph, which is the only sensible target.
            if (_cursor < 0)
            {
                _cursor = delta > 0 ? 0 : count - 1;
                Announce(Describe(_cursor));
                return;
            }

            int next = _cursor + delta;
            if (next < 0)
            {
                Announce("Start of article. " + Describe(0));
                _cursor = 0;
                return;
            }
            if (next >= count)
            {
                Announce("End of article. " + Describe(count - 1));
                _cursor = count - 1;
                return;
            }

            _cursor = next;
            Announce(Describe(_cursor));
        }

        /// <summary>
        /// Renders one paragraph for speech, with a position counter only where it earns its place.
        ///
        /// ⚠ A COUNTER ON EVERY PARAGRAPH IS WRONG FOR THIS CONTENT, and the real articles are what
        /// showed it. Several are quoted dialogue: day 10 splits into 38 paragraphs, of which the
        /// shortest are `"What?"` and `"Not this again."` - six to sixteen characters. Prefixing
        /// those with "2 of 38." makes the announcement mostly bookkeeping, and a player reading a
        /// back-and-forth exchange hears more numbers than story.
        ///
        /// So the counter is spoken on the paragraphs where a player actually needs to re-orient -
        /// the first, every tenth, and the last - and on any paragraph long enough that the prefix
        /// is a small fraction of it. In between, the prose speaks for itself. The position is never
        /// truly lost: the header gives the total up front, the ends announce themselves, and the
        /// counter returns on a round number soon after.
        /// </summary>
        private static string Describe(int index)
        {
            if (index < 0 || index >= _paragraphs.Count) return string.Empty;

            string text = _paragraphs[index];
            int number = index + 1;

            bool landmark = number == 1
                            || number == _paragraphs.Count
                            || number % 10 == 0
                            || text.Length >= 200;

            return landmark
                ? number + " of " + _paragraphs.Count + ". " + text
                : text;
        }

        /// <summary>
        /// Reports which day is displayed, as "day 3 of 14", plus which arrows still do something.
        ///
        /// displayday and maxDay are PRIVATE fields of the app, read by reflection and treated as
        /// optional: if the retail build renames them this returns an empty string and the rest of
        /// the announcement still goes out. A missing nicety must not silence the article.
        ///
        /// Naming the DEAD arrow matters as much as the live one. The game hides leftButton or
        /// rightButton at the ends of the range (CheckArrows / the click handlers), which a sighted
        /// player sees instantly and a blind one cannot - without this, pressing right at the newest
        /// day is silence that reads as a broken key.
        /// </summary>
        private static string DescribePosition(TG_NewspaperApp app)
        {
            try
            {
                object dayObj = AccessTools.Field(typeof(TG_NewspaperApp), "displayday")?.GetValue(app);
                object maxObj = AccessTools.Field(typeof(TG_NewspaperApp), "maxDay")?.GetValue(app);
                if (!(dayObj is int) || !(maxObj is int)) return string.Empty;

                int day = (int)dayObj;
                int max = (int)maxObj;
                if (max < 0) return string.Empty;

                StringBuilder sb = new StringBuilder();
                sb.Append("Day ").Append(day + 1).Append(" of ").Append(max + 1);

                if (max > 0)
                {
                    if (day <= 0) sb.Append(", oldest");
                    else if (day >= max) sb.Append(", newest");
                }

                return sb.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Splits an article body into speakable paragraphs.
        ///
        /// ⚠ THIS CANNOT GO THROUGH FungusText.ExtractWords WHOLESALE, AND THAT IS THE WHOLE POINT
        /// OF THIS METHOD. ExtractWords ends in Collapse(), which flattens every newline into a
        /// single space - correct for a dialogue line, fatal here: it would hand back one enormous
        /// paragraph and the cursor would have nothing to step through. So the raw text is split on
        /// blank lines FIRST, and each paragraph is cleaned individually afterwards.
        ///
        /// Blank-line splitting matches how the articles are actually written (verified against
        /// resources.assets): paragraphs are separated by an empty line, while a single newline is
        /// used for line breaks inside a verse. Song lyrics and quoted dialogue therefore stay
        /// intact as one unit instead of being shattered into one-line fragments.
        ///
        /// The bodies also carry TextMeshPro rich text - the app writes &lt;i&gt; blocks for lyrics
        /// and emphasis - which ExtractWords' cleaner removes per paragraph.
        /// </summary>
        private static List<string> SplitParagraphs(string body)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(body)) return result;

            // Normalize line endings so the blank-line test is a single case. The strings come out
            // of a spreadsheet-driven localizer and have carried \r\n in this game's data before.
            string normalized = body.Replace("\r\n", "\n").Replace('\r', '\n');

            string[] blocks = normalized.Split('\n');
            StringBuilder current = new StringBuilder();

            for (int i = 0; i < blocks.Length; i++)
            {
                string line = blocks[i].Trim();

                if (line.Length == 0)
                {
                    Flush(result, current);
                    continue;
                }

                if (current.Length > 0) current.Append(' ');
                current.Append(line);
            }
            Flush(result, current);

            return result;
        }

        /// <summary>
        /// Cleans an accumulated paragraph and adds it, dropping any that turn out to be pure
        /// markup - an &lt;i&gt; on a line of its own is layout, not content, and would otherwise
        /// become an empty entry the player has to arrow past for no reason.
        /// </summary>
        private static void Flush(List<string> result, StringBuilder current)
        {
            if (current.Length == 0) return;

            string cleaned = CleanLine(current.ToString());
            current.Length = 0;

            if (cleaned.Length > 0) result.Add(cleaned);
        }

        /// <summary>
        /// Strips markup from a single already-delimited paragraph.
        ///
        /// Safe to use ExtractWords here precisely BECAUSE the paragraph boundaries have already
        /// been decided: the newline-collapsing that would have destroyed them is now doing the
        /// useful half of its job, joining a wrapped line into flowing prose.
        /// </summary>
        private static string CleanLine(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : FungusText.ExtractWords(value);
        }

        /// <summary>
        /// Reads a TextMeshPro component, tolerating a null reference.
        ///
        /// ⚠ THIS SCREEN MIXES TEXT COMPONENT TYPES, exactly as the physical newspaper does. The
        /// article's title and body are TextMeshProUGUI (on TG_NewsDetailContentUI) while the date
        /// is a legacy UI.Text (on TG_NewspaperApp itself). A single accessor assuming either type
        /// would compile against one field and silently drop the other, so both overloads exist and
        /// the compiler picks per call site.
        /// </summary>
        private static string TextOf(TMPro.TextMeshProUGUI text)
        {
            return text == null ? null : text.text;
        }

        /// <summary>Reads a legacy UI.Text component (the date), tolerating a null reference.</summary>
        private static string TextOf(UnityEngine.UI.Text text)
        {
            return text == null ? null : text.text;
        }

        /// <summary>
        /// True when the newspaper app's own GameObject is still active in the scene.
        ///
        /// activeInHierarchy rather than activeSelf, because the phone hides the whole app stack by
        /// deactivating a PARENT: an app whose own flag is still true can already be off screen.
        /// The Unity null check also covers a destroyed object on a scene change.
        /// </summary>
        private static bool IsPanelOpen()
        {
            try
            {
                return _app != null && _app.gameObject != null && _app.gameObject.activeInHierarchy;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Reset()
        {
            _open = false;
            _app = null;
            _cursor = -1;
            _title = string.Empty;
            _paragraphs = new List<string>();
            _upHeld = false;
            _downHeld = false;
        }

        /// <summary>
        /// Speaks a line from the newspaper app.
        ///
        /// interrupt:true because stepping is a navigation action - a player who presses Down twice
        /// wants the second paragraph now, not after the first finishes reading.
        ///
        /// ⚠ TextType.Menu, NOT Dialogue. This was Dialogue, on the reasoning that only Dialogue is
        /// stored for the backquote repeat key - which is TRUE of UnityAccessibilityLib's DEFAULT
        /// predicate but NOT of this mod, because Main.OnInitializeMelon overrides
        /// ShouldStoreForRepeatPredicate to store everything except System. So Menu is stored just
        /// as reliably, and the wanted behaviour (repeat returns the current paragraph alone,
        /// because the cursor holds position) is unaffected.
        ///
        /// What Dialogue DID cost is real: it is the one type UAL gives a "Speaker: " prefix to.
        /// A newspaper article has no speaker, so the header was being announced as though someone
        /// were saying it (visible in the log as `[UAL] [Dialogue] Newspaper, September 22nd...`).
        /// Menu is what every other UI announcement in this mod uses.
        /// See memory: ual-repeat-key-storage.
        /// </summary>
        private static void Announce(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            ISpeechOutput speech = Speech;
            if (speech == null || !speech.IsAvailable) return;

            MelonLogger.Msg("[NewsApp] " + line);
            speech.SpeakAs(null, line, TextType.Menu, true);
        }
    }
}
