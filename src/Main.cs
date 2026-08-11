// Coffee Talk Access - a screen reader mod for Coffee Talk.
// Copyright (C) 2026 amock
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT
// ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
// FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with
// this program. If not, see <https://www.gnu.org/licenses/>.

using System;
using CoffeeTalkAccess.Dialogue;
using CoffeeTalkAccess.Menus;
using CoffeeTalkAccess.Speech;
using MelonLoader;
using UnityAccessibilityLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(CoffeeTalkAccess.AccessMod), "Coffee Talk Access", "0.9.2", "amock")]
[assembly: MelonGame("Toge Productions", "CoffeeTalk")]

namespace CoffeeTalkAccess
{
    /// <summary>
    /// Mod entry point.
    ///
    /// PHASE 0 (this build) is a SMOKE TEST, deliberately minimal: prove the loader boots on this
    /// game, prove speech reaches NVDA, and prove a Harmony patch on Fungus's central dialogue
    /// sink actually fires. Feature work (brewing narration, choices) only starts once those three
    /// are confirmed live - a silent stop with nothing spoken is the worst possible failure mode,
    /// so each layer announces itself rather than failing quietly.
    /// </summary>
    public sealed class AccessMod : MelonMod
    {
        internal static ISpeechOutput Speech { get; private set; }

        private FocusNarrator _focus;

        // Menu navigation is supplied by KeyboardNav, a Harmony postfix on the game's own
        // HandlerKeyboard - there is no object to hold and nothing to drive from OnUpdate.
        //
        // It replaced MenuCursor (491 lines) and then JoystickBridge, both retired to .retired/:
        //  - MenuCursor re-implemented navigation per screen and kept a SECOND cursor beside the
        //    game's `cursorIdx`. When the two disagreed the mod announced one entry while Enter
        //    activated another; that quit the game once.
        //  - JoystickBridge forced currentTypeController to JOYSTICK and added its own key
        //    bindings. It had to fight CheckActiveController every frame, and its bindings
        //    contended with the EventSystem's action set, which broke the language picker.
        //
        // KeyboardNav does neither: it adds no bindings and never touches the controller mode, so
        // the game keeps detecting keyboard vs gamepad by itself.

        // F8 = manual speech test (does the channel work at all, right now?).
        // Backquote = repeat the last spoken line. F10 = dump the live UI state to the log
        // (what is focused, what selectables exist) for diagnosing a screen that stays silent.
        // Coffee Talk binds none of these.
        private const KeyCode TestKey = KeyCode.F8;
        private const KeyCode RepeatKey = KeyCode.BackQuote;
        private const KeyCode DumpKey = KeyCode.F10;

        // Shift+Backquote ("~") = toggle automatic story narration on/off. See DialogueToggle.
        //
        // ⚠ THE TWO SHARE ONE PHYSICAL KEY. Unity has no KeyCode.Tilde - "~" IS BackQuote with
        // shift held, and Input.GetKeyDown(BackQuote) is true for BOTH. So repeat must EXCLUDE
        // shift explicitly; without that, toggling would also fire a repeat on the same frame and
        // the player would hear the old line read over the toggle announcement.
        private static bool ShiftHeld
        {
            get
            {
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            }
        }

        // F9 = read the glass's current brewing stats aloud.
        //
        // A QUERY key rather than an automatic re-read after every ingredient: the stats panel is a
        // toggle for sighted players too (BrewInformationClick swaps it against the brew info
        // panel), so answering on request matches how the game itself treats this information, and
        // it keeps the per-ingredient preview line short enough to scan a whole row with.
        // F9 is unbound by Coffee Talk, like the three above.
        private const KeyCode StatsKey = KeyCode.F9;

        public override void OnInitializeMelon()
        {
            // Route the library's internal logging into MelonLoader's log.
            AccessibilityLog.Logger = new MelonAccessibilityLogger();

            Speech = new UalAnnouncer();
            MelonLogger.Msg("[Init] Speech channel: " + Speech.Name + ", available=" + Speech.IsAvailable);

            if (!Speech.IsAvailable)
            {
                MelonLogger.Error(
                    "[Init] Speech UNAVAILABLE. Check that the 32-BIT UniversalSpeech.dll and " +
                    "nvdaControllerClient.dll are in the game root next to CoffeeTalk.exe.");
            }

            // Fungus writes at a per-glyph cadence and re-speaking mid-line would stutter, so
            // dedup is handled by our own hook (one Speak per Say), not by a time window.
            SpeechManager.DuplicateWindowSeconds = 0.0;

            // WHAT THE REPEAT KEY CAN REPEAT.
            //
            // The library's default stores only Dialogue and Narrator. That is wrong for this game:
            // the opening cutscene's credit line and the "press any button" prompts are sent as
            // TextType.Menu (they must NOT be Dialogue - that type takes a "Speaker: " prefix), so
            // they were never stored. Live evidence, 26-8-9_22-56-25.log:
            //   22:57:39 [Cutscene/Credit] A Game by Toge Productions
            //   22:57:42 [UAL] Nothing to repeat        <- player pressed backquote in between
            // The one screen where a player most needs to re-read - text that appears on a timer and
            // then vanishes - was the one screen repeat did not serve.
            //
            // Menu rows are worth repeating for the same reason: "RESOLUTION, 1920 X 1080, 4 of 10"
            // is long, and a player who missed the middle of it should not have to arrow off the row
            // and back to hear it again. System messages are excluded: they are mod chatter
            // ("Speech test.", patch warnings), not game content, and storing them would let a
            // diagnostic overwrite the story line the player actually wanted back.
            SpeechManager.ShouldStoreForRepeatPredicate =
                type => type != TextType.System;

            // Readable type names in the log instead of bare ints. The log printed "[UAL] [2] ..."
            // and "[0] ...", which reads as a priority level and is not one - it is the text
            // CATEGORY. Naming them removes a standing source of misdiagnosis.
            SpeechManager.TextTypeNames = new System.Collections.Generic.Dictionary<int, string>
            {
                { TextType.Dialogue, "Dialogue" },
                { TextType.Narrator, "Narrator" },
                { TextType.Menu, "Menu" },
                { TextType.MenuChoice, "MenuChoice" },
                { TextType.System, "System" },
            };

            ApplyPatches();

            _focus = new FocusNarrator();
        }

        /// <summary>
        /// Applies the Harmony patches and VERIFIES they took.
        ///
        /// Do NOT rely on MelonLoader auto-patching the mod assembly: on this build it does not
        /// happen, and the failure is SILENT - the game runs, speech works, and dialogue is simply
        /// never spoken with nothing in the log to say why. That cost a live run. We patch
        /// explicitly, then assert each target actually has patches attached, and announce the
        /// result out loud so a broken build is audible rather than mysterious.
        /// </summary>
        private void ApplyPatches()
        {
            try
            {
                HarmonyLib.Harmony harmony = HarmonyInstance ?? new HarmonyLib.Harmony("coffeetalk.access");

                // ⚠ DO NOT call PatchAll here when MelonLoader gave us a HarmonyInstance.
                //
                // MelonLoader v0.7.1 auto-patches the mod assembly before OnInitializeMelon runs
                // (hence its HarmonyDontPatchAllAttribute opt-out, which we do NOT carry). Calling
                // PatchAll again registered every postfix a SECOND time, so every hook fired twice:
                // each line of dialogue spoken twice, every ingredient announced twice.
                //
                // This hid from the existing check because GetPatchedMethods() returns DISTINCT
                // methods - 46 targets, each listed once - so the count looked correct while the
                // patches per target were doubled. Live evidence, 26-8-10_1-16-15.log: paired
                // [Brew]/[Hook] lines 2-3 ms apart, for the whole session, on every hook.
                //
                // We still patch explicitly when there is NO instance from MelonLoader, because the
                // original hazard is real: a build where auto-patching does not happen is silent,
                // and the game runs with nothing in the log to say why.
                if (HarmonyInstance == null)
                    harmony.PatchAll(typeof(AccessMod).Assembly);

                // The retail language picker, attached MANUALLY because its target method exists
                // only in the full game. TG_InitLanguageSettingMenu ships in both builds but
                // RefreshLanguageUI does not, so a [HarmonyPatch] attribute naming it would make
                // PatchAll THROW on the demo and take every other hook down with it. Absence here is
                // an expected build difference, not a failure - hence a plain informational line.
                if (Menus.LanguagePickerPatches.TryAttach(harmony))
                    MelonLogger.Msg("[Patch] Language picker hook attached (full-game build).");
                else
                    MelonLogger.Msg("[Patch] No retail language picker on this build (demo) - skipped.");

                // The mod manager, attached manually for a STRONGER version of the same reason:
                // TG_ModManagerUI is absent from the demo assembly entirely, so naming it in an
                // attribute does not even COMPILE against the demo - and the csproj defaults to the
                // demo install. See the header of ModMenuPatches.
                int modHooks = FullGame.ModMenuPatches.TryAttach(harmony);
                if (modHooks > 0)
                    MelonLogger.Msg("[Patch] Mod manager hooks attached (" + modHooks + ", full-game build).");
                else
                    MelonLogger.Msg("[Patch] No mod manager on this build (demo) - skipped.");

                int patched = 0;
                foreach (System.Reflection.MethodBase m in harmony.GetPatchedMethods())
                {
                    patched++;
                    MelonLogger.Msg("[Patch] Applied to: " + m.DeclaringType.FullName + "." + m.Name);
                }

                if (patched == 0)
                {
                    MelonLogger.Error("[Patch] NO methods were patched - dialogue will be silent.");
                    Speech?.Speak("Warning: dialogue hooks failed to apply. Dialogue will not be spoken.", true);
                }
                else
                {
                    MelonLogger.Msg("[Patch] " + patched + " method(s) patched.");
                }

                VerifyExpectedPatches(harmony);
            }
            catch (Exception e)
            {
                MelonLogger.Error("[Patch] PatchAll threw: " + e);
                Speech?.Speak("Warning: dialogue hooks failed with an error.", true);
            }
        }

        /// <summary>
        /// Names every hook we EXPECT and reports the ones that did not attach.
        ///
        /// The existing count check only proves that SOMETHING was patched. It cannot distinguish
        /// "all fifteen hooks live" from "fourteen live and the cutscene one silently missing" -
        /// and a hook whose target method was renamed fails exactly that quietly, leaving a screen
        /// mute with a clean log. Listing the misses by name turns the next silent screen into a
        /// one-line answer instead of another live run spent guessing.
        ///
        /// Checked by declaring type + method name so this survives signature changes.
        /// </summary>
        private static void VerifyExpectedPatches(HarmonyLib.Harmony harmony)
        {
            string[,] expected =
            {
                // Keyboard navigation. Listed because it is patched by STRING name (the method is
                // private), which is the failure mode this list exists to catch: a rename would
                // leave the arrow keys dead on every screen with an otherwise clean log. It is also
                // the hook whose HOST was wrong once - see KeyboardNav.AfterControllerUpdate.
                // Also the host for MainMenuHotkeys (Tab -> mods, Escape -> exit). Both hooks share
                // this target deliberately; ReportDoublePatches exempts it by name.
                //
                // ⚠ DO NOT ADD A TAB -> SMARTPHONE HOOK HERE. 0.9.1 did, on the theory that the
                // game sits in JOYSTICK mode so HandlerKeyboard never runs and never reaches
                // SmartPhoneToggle. Measured false: log 26-8-11_15-14-29 records
                // `controller mode = KEYBOARD`, the game's own Tab handling opening the phone, and
                // ZERO [Hotkey] lines - the hook's own JOYSTICK check made it a permanent no-op.
                // The game manages this state itself in keyboard mode. See PhoneHotkey's removal.
                { "TG_ControllerInputManager", "ControllerUpdateFunction" },
                { "Fungus.SayDialog", "Say" },
                { "Fungus.SayDialog", "SetCharacterName" },
                { "TG_OpeningCutSceneManager", "SetDialogueText" },
                { "TG_CutsceneManager", "SetDialogueText" },
                { "TG_OpeningCutSceneManager", "GetCreditOpeningText" },
                { "TG_PressAnyBlinkTextUI", "SetTextLocalization" },
                { "TG_PressAnyToSkipBlinkTextUI", "SetTextLocalization" },
                { "TG_NameKeys", "Initialize" },
                { "TG_NameKeys", "OnValueChanged" },
                { "TG_NameKeys", "ConfirmName" },
                { "TG_NameKeys", "BackToMainMenu" },
                { "TG_PopUpUI", "SetPopUpTitleText" },
                { "TG_PopUpUI", "SetTextTerm" },
                { "TG_PopUpLoadUI", "SetInfoText" },
                { "TG_ChatLogManager", "StartOpenChatLog" },
                { "TG_ChatLogManager", "CloseChatLog" },
                { "TG_DrinkManager", "AddIngredient" },
                { "TG_DrinkManager", "GetDrinkNameAndColor" },
                { "TG_DrinkManager", "ServeGlassDrink" },
                { "TG_DrinkManager", "ServeGlassDrinkLatteArt" },
                { "TG_DrinkManager", "ResetIngredients" },
                { "TG_DrinkManager", "BrewInformationClick" },
                { "LatteArtManager", "ActivateLatteArt" },
                { "LatteArtManager", "CloseLatteArt" },
                // Phase 5 - full-game systems. UNTESTABLE ON THE DEMO (its story stops before the
                // day cycle that generates a newspaper, and the phone is not reachable), so these
                // are expected to attach but never fire here. They are listed anyway: a hook that
                // fails to ATTACH is a different problem from one that never fires, and only this
                // list can tell them apart when the retail build arrives.
                { "TG_NewspaperManager", "GenerateNewspaper" },
                { "TG_SmartPhoneManager", "OpenSmartPhone" },
                { "TG_SmartPhoneApps", "Open" },
                { "TG_DrinkRecipesApp", "DisplayDrinks" },
                { "TG_DrinkItemUI", "OnButtonClick" },
                // The music app, hooked on BOTH concrete classes rather than on the shared base
                // TG_MusicAppGeneral - every method on that base is an empty virtual, so a postfix
                // there would attach (and be reported live and green by this very list) while never
                // firing. Listing both is what makes that distinction visible. See MusicAppPatches.
                { "TG_MusicApp", "PlaylistSongButtonClick" },
                { "TG_MusicAppDemo", "PlaylistSongButtonClick" },
                // The social-media detail pane. Unlike the friend-list rows (which are labelled
                // through FocusNarrator), this one SPEAKS: opening it nulls the EventSystem
                // selection and the pane holds no focusable item, so if this hook is missing the
                // screen is silent with no fallback at all. See SocialMediaPatches.
                { "TG_SocialMediaDetailProfileUI", "SetDetailProfile" },
                // The phone's newspaper ARCHIVE (NewspaperAppPatches), which is a different screen
                // and a different data source from TG_NewspaperManager's morning paper above.
                // Doubly unreachable on the demo: the phone is blocked, and this app's Open() shows
                // the subscribe nag on an expo build. Attachment is still worth verifying - it is
                // the only thing about this feature the demo CAN tell us.
                { "TG_NewspaperApp", "Open" },
                { "TG_NewspaperApp", "SetNewsOnApp" },
                { "TG_SmartPhoneApps", "Close" },
                // Achievements are reachable from the MAIN MENU's extras screen, not from the
                // story, so unlike the rest of phase 5 these may well fire on the demo.
                { "TG_AchievementMenuManager", "SetSelectedData" },
                { "TG_AchievementMenuManager", "Init" },
                // Leaving the screen must drop both the armed entry line and any parked icon label,
                // or the label is spoken as the caption of whatever the extras menu focuses next.
                { "TG_AchievementMenuManager", "BackToExtrasMenu" },
                { "TG_CalendarUIManager", "SetSelectedData" },
                { "TG_CalendarUIManager", "SetLastPlayedData" },
                // Both exits from the load calendar, for the same reason as BackToExtrasMenu above:
                // a parked day label outliving the screen becomes the caption of the next control
                // focused. TG_CalendarUIManager.Initialize picks between these two on
                // TG_Static.currentScene, so hooking only one leaves the channel armed on the other.
                { "TG_SaveMenuManager", "BackToMainMenu" },
                { "TG_SaveMenuManager", "BackToPauseInGameMenu" },
                { "TG_GalleryManager", "SetLargeImage" },
                { "TG_GalleryManager", "SetBiggestImage" },
                { "TG_ComicMenuManager", "SetLargeImage" },
                { "TG_ComicMenuManager", "SetBiggestImage" },
                { "TG_EndingCutsceneItem", "GetDialogueEndingCutscene" },
                // The retail profile picker. Listed as REQUIRED rather than optional because both
                // types ship in the DEMO assembly too (verified by reflection over both DLLs) - only
                // the scene data differs - so these hooks must attach on either build, and a miss is
                // a real regression rather than a build difference. See ProfileSelectPatches.
                { "TG_ProfileSlotFlipUI", "SelectInfoButton" },
                { "TG_ProfileUIManager", "BackToMainMenu" },
                { "TG_ProfileUIManager", "CloseProfileSelect" },
            };

            System.Collections.Generic.HashSet<string> live =
                new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

            foreach (System.Reflection.MethodBase m in harmony.GetPatchedMethods())
            {
                if (m?.DeclaringType == null) continue;
                live.Add(m.DeclaringType.FullName + "." + m.Name);
            }

            // Hooks that exist on ONE build only. Absence is a build difference, not a fault, so
            // these are reported as information and never counted as missing. Keeping them in a
            // SEPARATE table (rather than dropping them from verification) means we still learn
            // which build we are on from the log, without a red error either way.
            string[,] optional =
            {
                // Retail's language picker is a spinner with no per-language Selectable; the demo's
                // is a grid of flag buttons. RefreshLanguageUI exists only in the full game.
                { "TG_InitLanguageSettingMenu", "RefreshLanguageUI" },
                // The Steam Workshop mod manager. TG_ModManagerUI does not exist in the demo
                // assembly at all - MOD_MENU is the single value retail ADDED to the state enum -
                // so absence here is a build difference, not a fault. Open() announces the screen
                // and its mod counts; Update() carries the keyboard tab switch.
                { "TG_ModManagerUI", "Open" },
                { "TG_ModManagerUI", "Update" },
                // Close restores the promo button's navigation. Listed because a miss here would
                // leave a main-menu control permanently un-navigable for the rest of the session -
                // the mod having broken a screen it does not own, silently.
                { "TG_ModManagerUI", "Close" },
            };

            int missing = 0;
            for (int i = 0; i < expected.GetLength(0); i++)
            {
                string key = expected[i, 0] + "." + expected[i, 1];
                if (live.Contains(key)) continue;

                missing++;
                MelonLogger.Error("[Patch] MISSING: " + key + " - that screen will be silent.");
            }

            for (int i = 0; i < optional.GetLength(0); i++)
            {
                string key = optional[i, 0] + "." + optional[i, 1];
                MelonLogger.Msg("[Patch] optional hook " + key + ": "
                    + (live.Contains(key) ? "live." : "not present on this build."));
            }

            if (missing > 0)
                MelonLogger.Error("[Patch] " + missing + " expected hook(s) did NOT attach.");
            else
                MelonLogger.Msg("[Patch] All " + expected.GetLength(0) + " expected hooks are live.");

            ReportDoublePatches(harmony);
        }

        /// <summary>
        /// Reports any method carrying more than one of OUR patches - i.e. the same postfix applied
        /// twice, which makes every announcement speak twice.
        ///
        /// This exists because the checks above cannot see it. GetPatchedMethods() returns each
        /// method ONCE however many times it was patched, so a double application reads as a
        /// perfectly healthy "46 method(s) patched / all expected hooks live" while the player hears
        /// everything twice. Counting the patch OWNERS per method is what distinguishes them.
        ///
        /// ⚠ It counts PREFIXES and POSTFIXES SEPARATELY, and exempts the methods we deliberately
        /// hook more than once. Both refinements came from the first three retail runs, where this
        /// check printed two red DOUBLE-PATCHED errors at every startup and NEITHER was real: one
        /// was a prefix beside a postfix, the other a speaking postfix beside a state-clearing one.
        /// A check that reports a bug that is not there costs as much trust as one that misses a
        /// bug that is - the player cannot tell which kind they are looking at.
        /// </summary>
        private static void ReportDoublePatches(HarmonyLib.Harmony harmony)
        {
            int doubled = 0;
            foreach (System.Reflection.MethodBase m in harmony.GetPatchedMethods())
            {
                if (m == null) continue;

                HarmonyLib.Patches info = HarmonyLib.Harmony.GetPatchInfo(m);
                if (info == null) continue;

                // ⚠ COUNT THE TWO KINDS SEPARATELY. Summing them was a false alarm: a PREFIX plus a
                // POSTFIX on the same method is one of each and entirely normal, but it totalled 2
                // and was reported as "it will speak twice". Live proof, the first three retail runs
                // (26-8-10_17-14-43 and two more): TG_MainMenuManager.MouseOverManager was flagged
                // every startup, when MenuPatches deliberately has BeforeMouseOverManager (a prefix
                // that only raises a re-entrancy flag) alongside AfterMouseOverManager. Nothing
                // spoke twice. Double APPLICATION means the same KIND applied twice over.
                int ourPrefixes = 0;
                int ourPostfixes = 0;
                foreach (HarmonyLib.Patch p in info.Prefixes)
                    if (p.owner == harmony.Id) ourPrefixes++;
                foreach (HarmonyLib.Patch p in info.Postfixes)
                    if (p.owner == harmony.Id) ourPostfixes++;

                int ours = System.Math.Max(ourPrefixes, ourPostfixes);
                if (ours <= 1) continue;

                // TG_SmartPhoneApps.Close is a SHARED base method that several app readers hook on
                // purpose - NewspaperAppPatches and DrinkRecipesPatches each postfix it and then
                // filter on `__instance is <their app>`, so only one of them ever acts per call.
                // That is a deliberate fan-out, not the accidental double-application this check
                // hunts for, and flagging it would print a red error on every startup. A diagnostic
                // that cries wolf teaches the player to ignore the real ones.
                if (m.DeclaringType?.Name == "TG_SmartPhoneApps" && m.Name == "Close") continue;

                // TG_DrinkManager.AddIngredient is the same shape: BrewingPatches.AfterAddIngredient
                // is the one that SPEAKS, while StatsPatches.AfterAddIngredientClearPreview only
                // sets PendingStats = null. Two postfixes, one voice - deliberate, and flagged on
                // every one of the first three retail runs until this exemption existed.
                //
                // ⚠ The general rule this check can never see: it counts HOOKS, but the thing that
                // matters is how many of them SPEAK. Where a second hook exists purely to clear
                // state, exempt it HERE and say why, rather than dropping the check or letting it
                // cry wolf - a diagnostic the player learns to ignore is worse than none.
                if (m.DeclaringType?.Name == "TG_DrinkManager" && m.Name == "AddIngredient") continue;

                // TG_ControllerInputManager.ControllerUpdateFunction is the mod's per-frame input
                // host and carries two deliberate postfixes: KeyboardNav.AfterControllerUpdate
                // (pumps the directional routers) and MainMenuHotkeys.AfterControllerUpdate (reads
                // Tab/Escape on the main menu). Both are hosted here for the same load-bearing
                // reason - it runs unconditionally from Update() in BOTH input modes, whereas
                // HandlerKeyboard runs only in KEYBOARD mode, which a connected pad can suppress
                // indefinitely. Neither speaks on the frames the other acts: KeyboardNav narrates
                // nothing itself, and the hotkeys act only on a GetKeyDown edge in MAIN_MENU.
                if (m.DeclaringType?.Name == "TG_ControllerInputManager"
                    && m.Name == "ControllerUpdateFunction") continue;

                doubled++;
                MelonLogger.Error("[Patch] DOUBLE-PATCHED (" + ours + "x): "
                    + m.DeclaringType.FullName + "." + m.Name + " - it will speak twice.");
            }

            if (doubled > 0)
                MelonLogger.Error("[Patch] " + doubled + " method(s) patched more than once.");
        }

        public override void OnLateInitializeMelon()
        {
            // Announced late so it lands after the game's own startup chatter settles.
            Speech?.Speak("Coffee Talk Access loaded. Press F8 to test speech.", true);
        }

        // Controller state is logged automatically rather than on an F10 keypress. Three
        // consecutive live runs went by without the dump because pressing a diagnostic key is one
        // more thing to remember mid-test - and without it every controller theory stays a guess.
        // A periodic report costs nothing and means the evidence is simply THERE in the log.
        private float _nextDeviceReport;
        private string _lastDeviceReport;

        /// <summary>
        /// Logs the InControl device list whenever it CHANGES, plus once at startup. Change-driven
        /// rather than every-N-seconds so plugging or unplugging a pad is visible as an event,
        /// and a steady state does not flood the log.
        /// </summary>
        private void ReportDevicesIfChanged()
        {
            if (Time.realtimeSinceStartup < _nextDeviceReport) return;
            _nextDeviceReport = Time.realtimeSinceStartup + 2f;

            string report = Menus.KeyboardNav.DescribeDevices();
            if (report == _lastDeviceReport) return;

            _lastDeviceReport = report;
            MelonLogger.Msg("[Devices] " + report);
            MelonLogger.Msg("[Devices] controller mode = " + Menus.KeyboardNav.DescribeControllerMode());
        }

        public override void OnUpdate()
        {
            if (Speech == null) return;

            // Division of labour: KeyboardNav (a patch, not polled here) makes the arrow keys
            // move the game's own cursor, MenuPatches announces those moves, and FocusNarrator
            // covers the screens driven through Unity's EventSystem instead - the language
            // picker, where the routers all no-op and the EventSystem is already navigating.
            _focus?.Update();
            // The name-entry screen becomes live inside a tween callback, so there is no method to
            // postfix at the right moment; this polls for the state and fires once. See
            // NameEntryPatches.NameScreenWatcher.
            Menus.NameEntryPatches.NameScreenWatcher.Update();
            // Popups are announced from a watcher for the same reason: the text is set before the
            // dialog is visible, and its activation spans two frames inside a coroutine. See
            // PopUpPatches.PopUpWatcher.
            Menus.PopUpPatches.PopUpWatcher.Update();
            // Several screens (the phone, pop-up dialogs, the load-story list, the friend list) get
            // an EventSystem selection ONLY when a gamepad is active - the game's helpers are all
            // written as `if (JOYSTICK) { Select(); }` with no else - so on a keyboard they open
            // with no cursor and cannot be navigated at all. See Menus.FocusRecovery.
            Menus.FocusRecovery.Update();
            // The chat log has no per-row focus for ANY input device - the game scrolls it as a
            // wall of pixels - so the mod supplies an entry cursor over the log data. Stepped from
            // here rather than from a patch on the game's own update, so a fault can never throw
            // inside the game's frame callback. See ChatLogPatches.
            Dialogue.ChatLogPatches.Update();
            // The phone's newspaper app scrolls its article body as a wall of pixels (a Scrollbar,
            // no per-item focus for any input device), so the mod supplies a PARAGRAPH cursor over
            // the article text - the same shape as the chat log, and stepped from here for the same
            // reason: a fault must never throw inside the game's own frame callback.
            // ⚠ Unreachable on the demo (the phone is blocked); see NewspaperAppPatches.
            FullGame.NewspaperAppPatches.Update();
            // The achievements screen is built (Init) a full second before it is usable: the panel
            // is activated and focused inside a chain of DOTween callbacks with no method of ours to
            // postfix. Same reason as the name screen above - poll for the panel, then speak once.
            FullGame.AchievementPatches.EntryWatcher.Update();
            // The load calendar has the same gap: RefreshSlot fills in the continue-summary from
            // inside OpenSaveMenu, which then waits out a realtime delay and a fade before the grid
            // takes focus. Speaking at fill time meant the summary was cut off by the focused day.
            FullGame.CalendarPatches.EntryWatcher.Update();
            // The smartphone has the SAME gap, and it produced the worst version of it: OpenSmartPhone
            // only starts a 0.6s tween, so announcing from its postfix described a phone that was not
            // open yet while the cafe underneath still owned focus (log 26-8-10_18-17-43: "[Phone]
            // Smartphone..." then "[Focus] Coffee" 82ms later).
            FullGame.SmartPhonePatches.PhoneEntryWatcher.Update();
            ReportDevicesIfChanged();

            if (Input.GetKeyDown(TestKey))
            {
                Speech.Speak("Speech test. Coffee Talk Access is running.", true);
                MelonLogger.Msg("[Test] F8 pressed; test line sent to speech channel.");
            }
            else if (Input.GetKeyDown(RepeatKey))
            {
                // Same key, split by shift: "~" toggles narration, bare "`" repeats.
                if (ShiftHeld) Dialogue.DialogueToggle.Toggle();
                else Speech.RepeatLast();
            }
            else if (Input.GetKeyDown(StatsKey))
            {
                Brewing.StatsPatches.SpeakCurrentStats();
            }
            else if (Input.GetKeyDown(DumpKey))
            {
                DumpUiState();
            }
        }

        /// <summary>
        /// Logs what the UI actually looks like right now: the focused object and every
        /// interactable Selectable on screen. This exists because a screen that stays silent has
        /// two very different causes - nothing is focused (the game never set selection), or the
        /// focused object carries no readable text - and they need opposite fixes. Guessing
        /// between them costs a live run each time, so we ask the game instead.
        /// </summary>
        /// <summary>Names a navigation target, distinguishing "no target" from an unnamed object.</summary>
        private static string NameOf(Selectable s)
        {
            return s == null ? "-" : s.gameObject.name;
        }

        /// <summary>
        /// The object's ancestry, root first, so the log shows which SCREEN owns a control rather
        /// than just what it is called. Capped at four levels - enough to name the owning canvas
        /// without turning every dump line into a paragraph.
        /// </summary>
        private static string PathOf(GameObject go)
        {
            if (go == null) return "?";

            string path = go.name;
            Transform t = go.transform.parent;
            int depth = 0;
            while (t != null && depth < 4)
            {
                path = t.name + "/" + path;
                t = t.parent;
                depth++;
            }
            return path;
        }

        private static void DumpUiState()
        {
            try
            {
                EventSystem es = EventSystem.current;
                GameObject sel = es != null ? es.currentSelectedGameObject : null;
                MelonLogger.Msg("[Dump] EventSystem=" + (es == null ? "NULL" : es.name)
                    + " focused=" + (sel == null ? "NULL" : sel.name));

                // The game's own input state machine decides which screen the arrow keys drive.
                // Reporting it turns "nothing happens" into a specific answer: a state we do not
                // narrate yet, versus a state whose handler we hooked but that never fires.
                MelonLogger.Msg("[Dump] TG state = " + ReadControllerState());

                // What InControl can actually see. Reported here because "the D-pad does nothing"
                // has three unrelated causes that feel identical to the player, and only the
                // device list tells them apart.
                MelonLogger.Msg("[Dump] InControl: " + Menus.KeyboardNav.DescribeDevices());

                Selectable[] all = UnityEngine.Object.FindObjectsOfType<Selectable>();
                int shown = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    Selectable s = all[i];
                    if (s == null || !s.gameObject.activeInHierarchy || !s.interactable) continue;

                    // ⚠ REPORT THE NAVIGATION GRAPH, not just the control's existence.
                    //
                    // "Arrow keys do nothing" and "there is nowhere to go" look identical in a list
                    // of names. Navigation.Mode.None means the EventSystem will never move off this
                    // control no matter what is on screen; Explicit with all-null targets means the
                    // graph exists but is a dead end. Those are different bugs with different fixes,
                    // and without the mode + targets printed here the log cannot tell them apart -
                    // which is what turned a mod-menu report into a second round of guessing.
                    string nav = s.navigation.mode.ToString();
                    if (s.navigation.mode == Navigation.Mode.Explicit)
                    {
                        nav += " up=" + NameOf(s.navigation.selectOnUp)
                             + " down=" + NameOf(s.navigation.selectOnDown)
                             + " left=" + NameOf(s.navigation.selectOnLeft)
                             + " right=" + NameOf(s.navigation.selectOnRight);
                    }

                    // ⚠ PRINT WHERE THE CONTROL LIVES, not just its name.
                    //
                    // A flat list of names cannot distinguish "the screen owns this control" from
                    // "this belongs to the screen underneath and is leaking focus". I asserted the
                    // second about the mod menu from exactly such a list, and the player correctly
                    // objected that only TWO of the supposed leaks were ever reachable. The parent
                    // chain is what settles it, and it costs one line.
                    MelonLogger.Msg("[Dump]   selectable: " + PathOf(s.gameObject)
                        + " (" + s.GetType().Name + ") nav=" + nav);
                    if (++shown >= 40) { MelonLogger.Msg("[Dump]   ...truncated"); break; }
                }
                MelonLogger.Msg("[Dump] " + shown + " interactable selectable(s) active.");

                Speech.Speak(sel == null
                    ? "Nothing focused. " + shown + " controls on screen."
                    : "Focused: " + sel.name + ". " + shown + " controls on screen.", true);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Dump] threw: " + e.Message);
            }
        }

        /// <summary>
        /// Reads TG_ControllerInputManager.currentState (the game's own screen state machine)
        /// through reflection, so a diagnostic never becomes a hard dependency.
        /// </summary>
        internal static string ReadControllerState()
        {
            try
            {
                Type mgrType = HarmonyLib.AccessTools.TypeByName("TG_ControllerInputManager");
                if (mgrType == null) return "TG_ControllerInputManager type not found";

                // The manager is a TG_GenericSingelton<T>; grab its static Instance.
                Type singleton = HarmonyLib.AccessTools.TypeByName("TG_GenericSingelton`1");
                if (singleton == null) return "singleton type not found";

                Type closed = singleton.MakeGenericType(mgrType);
                object instance = HarmonyLib.AccessTools.Property(closed, "Instance")?.GetValue(null)
                                  ?? HarmonyLib.AccessTools.Field(closed, "Instance")?.GetValue(null);
                if (instance == null) return "no Instance (menu manager not alive yet)";

                object state = HarmonyLib.AccessTools.Field(mgrType, "currentState")?.GetValue(instance);
                return state == null ? "currentState unreadable" : state.ToString();
            }
            catch (Exception e)
            {
                return "threw: " + e.Message;
            }
        }

        /// <summary>Bridges UnityAccessibilityLib's logging into MelonLoader's console/log file.</summary>
        private sealed class MelonAccessibilityLogger : IAccessibilityLogger
        {
            public void Msg(string message) => MelonLogger.Msg("[UAL] " + message);
            public void Warning(string message) => MelonLogger.Warning("[UAL] " + message);
            public void Error(string message) => MelonLogger.Error("[UAL] " + message);
        }
    }
}
