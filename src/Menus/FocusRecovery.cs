using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoffeeTalkAccess.Menus
{
    /// <summary>
    /// Supplies an EventSystem selection on screens where Coffee Talk only ever sets one for a
    /// GAMEPAD, leaving the keyboard with no cursor to move.
    ///
    /// THE BUG CLASS. The game's "give this screen its initial focus" helpers are written as
    ///     if (CurrentTypeControllerState == JOYSTICK) { button.OnSelect(null); button.Select(); }
    /// with NO else branch. On a gamepad the screen gets a cursor; on a keyboard it gets nothing,
    /// the arrow keys have no selection to move, and the screen is completely unnavigable. Since
    /// FocusNarrator can only narrate a focus that EXISTS, the screen is also silent, so the two
    /// symptoms the player reports - "it says nothing" and "I can't move" - have one cause.
    ///
    /// This was found three times the expensive way (language picker, main menu, smartphone) before
    /// being swept for deliberately. An audit of the decompiled source for Select()/
    /// SetSelectedGameObject inside a JOYSTICK gate found these unguarded entry points:
    ///     TG_SmartPhoneManager:250,:361   home app grid          (confirmed live 2026-08-10)
    ///     TG_PopUpManager:66,:98          confirm + load dialogs (NO keyboard path at all)
    ///     TG_CalendarUIManager:65         load-story slot list
    ///     TG_SocialMediaApp:316,:326      friend list
    ///     TG_DrinkRecipesApp:402          recipe list
    ///     TG_EndlessModeUIManager:228     back-to-menu button
    ///     TG_DrinkManager:547             brewing ingredients    (recovers on its own, see below)
    ///
    /// WHY A WATCHER AND NOT A PATCH PER SITE. Seven patches would each need the right moment to
    /// fire, and several of these run inside tween callbacks where the panel is not yet
    /// interactable. The failure is identical in all of them and states the condition exactly:
    /// a menu-ish state is live, and NOTHING is selected. Watching for that covers the sites above
    /// and any screen with the same bug that this audit missed - including in the retail build,
    /// which has screens the demo cannot reach.
    ///
    /// ⚠ THIS MUST NEVER FIGHT THE GAME FOR FOCUS. Two disagreeing cursors is a failure this
    /// codebase has already paid for (it once quit the game). Three rules keep that from recurring:
    ///  1. Act ONLY when the selection is null. Never move a live selection.
    ///  2. Require the null to PERSIST for a short settle time. Focus is briefly null during normal
    ///     transitions - SetSelectedGameObject(null) appears in the game's own teardown paths
    ///     (TG_SaveMenuManager:350,:362, TG_DrinkManager:829) - and selecting during a handoff would
    ///     yank focus back to a screen that is closing.
    ///  3. Only ever select something the game itself would have selected, found by asking the
    ///     screen's own manager, and never on a screen with no candidates.
    /// </summary>
    internal static class FocusRecovery
    {
        /// <summary>
        /// How long the selection must stay null before we step in. Long enough that a normal
        /// screen handoff (which nulls focus for a frame or two) completes untouched, short enough
        /// that a player pressing arrows on a dead screen does not notice the wait.
        /// </summary>
        private const float SettleSeconds = 0.35f;

        /// <summary>
        /// How long a recovered selection must SURVIVE before the one-per-screen guard is released.
        /// Comfortably longer than the frame or two a fight lasts, and far shorter than any real
        /// visit to a screen, so a player who genuinely leaves and returns is still recovered.
        /// </summary>
        private const float HoldSeconds = 1.0f;

        private static float _nullSince = -1f;
        private static string _lastRecoveredState;

        /// <summary>When the current recovered selection first became healthy. -1 = not timing.</summary>
        private static float _recoveredAt = -1f;

        /// <summary>
        /// The control the player was last genuinely on, so a recovery can put them BACK rather than
        /// somewhere arbitrary.
        ///
        /// Why this exists: FindEntryControl returns the first interactable Selectable in
        /// FindObjectsOfType order, which is the right answer when a screen opens with no cursor and
        /// the WRONG answer when a cursor already existed and was destroyed. The game destroys it
        /// routinely - every switch to keyboard mode runs CheckRemovedState, whose default branch
        /// nulls the EventSystem selection for every state it does not name (SELECT_PROFILE,
        /// POP_UP_*, CALENDAR_LOAD_GAME, MOD_MENU among them). Live proof, log 26-8-10_17-39-3:
        /// mode went to KEYBOARD at 17:41:10.714, recovery fired 346 ms later and announced
        /// "Slot 3 of 3" while the player was on Slot 1.
        ///
        /// Recorded in the healthy-focus branch of Update(), which already runs every frame the
        /// selection is non-null, so this costs nothing extra.
        /// </summary>
        private static GameObject _lastGoodFocus;
        private static string _lastGoodState;
        private static float _lastGoodTime;

        /// <summary>
        /// How long a remembered control stays valid. Far longer than any mode flip (which resolves
        /// in well under a second) and far shorter than a screen visit.
        ///
        /// This is the backstop for the case the other three checks cannot catch: a screen re-entered
        /// in the SAME state after a long absence, where a pooled prefab happens to still exist. The
        /// profile slots are literally `SelectProfileFlipUI(Clone)`, so that hazard is real here.
        /// </summary>
        private const float MemorySeconds = 5f;

        /// <summary>
        /// States where the game drives the screen through the EventSystem and therefore NEEDS a
        /// selection. Deliberately a whitelist rather than "any state": screens driven by the game's
        /// own private cursor (MAIN_MENU) legitimately run with focus null, and selecting something
        /// there would introduce the second cursor rule 1 exists to prevent.
        /// </summary>
        private static bool NeedsSelection(string state)
        {
            switch (state)
            {
                // ⚠ ONLY the four phone states the game actually NAVIGATES. Verified against
                // TG_ControllerInputManager.UpButtonPressed/DownButtonPressed (and their Hold
                // twins): those route to musicScreenPanel / recipesDrinkScreenpanel /
                // socialMediaScreenPanel.ButtonInput for PHONE_HOME, PHONE_DRINK, PHONE_MUSIC and
                // PHONE_SOCMED, and for NOTHING else.
                //
                // PHONE_SOCMED_ACCOUNT, PHONE_NEWSPAPER and PHONE_NEWSPAPER_DETAIL used to be here
                // and were REMOVED 2026-08-10. They are the Scrollbar panes: reading them moves an
                // analog scroll position, and they contain no Selectable to move BETWEEN. Recovery
                // there cannot succeed, and cannot fail quietly either - FindEntryControl scans the
                // WHOLE SCENE, so with nothing navigable on the pane it returns the first
                // interactable control anywhere else and announces it under a phone label.
                //
                // That is not hypothetical: it is precisely the failure logged in 26-8-10_2-21-28
                // ("supplied missing keyboard selection on PHONE_HOME: ButtonBrew", three times).
                // The canOpenSmartPhone guard below fixed that for a BLOCKED phone; on retail these
                // three states reach the identical bug with the phone genuinely open, because the
                // cause was never the blocking - it was recovering on a screen with no candidates
                // (rule 3 of this class, violated by its own whitelist).
                case "PHONE_HOME":
                case "PHONE_DRINK":
                case "PHONE_SOCMED":
                case "PHONE_MUSIC":
                case "POP_UP_CONFIRMATION":
                case "POP_UP_LOAD":
                // ⚠ The load-story list is CALENDAR_LOAD_GAME, not "LOAD_GAME" - there is no such
                // state. Verified against the State enum rather than inferred from the manager name
                // (TG_CalendarUIManager), which is what suggested the wrong one.
                case "CALENDAR_LOAD_GAME":
                case "CONFIRMATION_LOAD_GAME":
                case "CONFIRMATION_EXIT_GAME":
                // ⚠ RETAIL-ONLY SCREENS, AND SELECT_PROFILE IS THE MOST IMPORTANT ENTRY IN THIS
                // LIST. Both were measured on the retail assembly (2026-08-10) and both carry this
                // class's exact bug:
                //   TG_ProfileUIManager.SelectFirstButton:252  - whole body inside a JOYSTICK gate
                //   TG_ModManagerUI:121                        - the same, in a DOFade callback
                // Neither state exists in any demo scene, so both are inert there rather than
                // wrong: NeedsSelection is only consulted for the state the game is actually in.
                //
                // SELECT_PROFILE is the retail game's FIRST interactive screen - TG_MainMenuManager
                // :230 sends PRESS_ANY_KEY straight into OpenSelectProfile(0f) - so without recovery
                // a keyboard player is stopped dead before reaching the main menu, with no cursor to
                // move and nothing spoken. It is EventSystem-driven (SetNavigation builds an explicit
                // left/right graph over profileSlotUIList[i].button), so recovery has real candidates
                // and the game's own MouseHoverEvent keeps `currentSelected` in step with our
                // selection - which is what makes Enter, Escape and gamepad X act on the right card.
                case "SELECT_PROFILE":
                case "MOD_MENU":
                    return true;

                // ⚠ BREWING IS EXCLUDED, AND THE REASON IS NOT THE ONE THAT USED TO BE WRITTEN HERE.
                //
                // The old text claimed brewing "already recovers on its own" because of the ungated
                // Select() calls at :312, :577, :587, :667-672. That is FALSE, and it shipped a dead
                // screen: every one of those needs an interaction to have ALREADY happened (:577
                // fires from CheckPluggedInState, i.e. a MODE CHANGE; :667-672 is the
                // resume-from-phone path; AddIngredient re-selects only after an ingredient went
                // in). None runs on plain entry with an empty glass. The "01:16 live run" cited as
                // proof reached brewing via a mode flip, which seeds through CheckPluggedInState -
                // which is exactly why the bug passed a live test. ⚠ A screen working in a live run
                // does NOT prove its ENTRY path works; check which path the run took.
                //
                // Brewing IS broken on a keyboard (TG_DrinkManager:547, SelectCocoa, JOYSTICK-gated)
                // and it is fixed in BrewingPatches.AfterBrewingEntry instead of here. That hook sits
                // on the entry point itself, so it is present for the one moment a selection is
                // missing and absent for every other. This class watches for ANY null selection,
                // which on this screen would also catch the one ServeGlassDrink makes deliberately
                // at :829 while the state is still BREWING - requiring a glass_value guard and a
                // scoped finder to undo the over-reach. Recovering the entry point directly needs
                // neither. See that method's comment for the full reasoning.
                default:
                    return false;
            }
        }

        internal static void Update()
        {
            try
            {
                EventSystem es = EventSystem.current;
                if (es == null) return;

                // ⚠ A POPUP OWNS THE SCREEN EVEN IF SOMETHING BEHIND IT STILL HOLDS THE SELECTION.
                //
                // Every other screen in this class is rescued only when the selection is NULL,
                // because a live selection means the game has a cursor and the mod must not fight
                // it (rule 1). Popups are the one exception, and the reason is structural:
                // TG_PopUpManager adds NO raycast blocker and disables nothing behind it, so the
                // screen underneath keeps its selection and keeps responding to the arrow keys -
                // while the game's own state says POP_UP_*, which is where Enter and Escape are
                // routed.
                //
                // Live proof, log 26-8-10_18-50-34: the load calendar's "Do you want to restart the
                // day?" confirmation announced correctly, and then the player's arrows walked the
                // CALENDAR GRID behind it ("Day 2 ... Day 3 ... Day 10") because the grid never
                // gave up focus. Recovery never even ran - the selection was never null. Reported
                // as "it didn't take focus when I selected the save and it asked for confirmation".
                //
                // The save popup (POP_UP_LOAD) worked in the same session only by luck: it opens
                // from a screen with nothing else interactable, so the selection WAS null there.
                //
                // So on a popup state we take focus even from a live selection, and we do it ONLY
                // when the selection is not already inside the popup - which keeps rule 1 intact
                // for the popup's own two buttons and stops this from re-firing every frame.
                if (IsPopUpState(state: AccessMod.ReadControllerState()) &&
                    !SelectionIsInsidePopUp(es.currentSelectedGameObject))
                {
                    TakeFocusForPopUp();
                    return;
                }

                // ⚠ THE PHONE IS A TAKEOVER SCREEN AND NEEDS THE SAME RULE, for the same reason.
                //
                // Measured, log 26-8-11_15-23-45: five phone opens. The first got
                // "supplied missing keyboard selection on PHONE_HOME" - because the selection
                // happened to be null. The last two announced "Smartphone..." and then focused
                // GREEN TEA and COFFEE: the phone was open, on top, owning input, while the
                // EventSystem still pointed at a brewing ingredient underneath. Arrow keys moved the
                // BREW PAD while the player believed they were in the phone. Reported as Tab not
                // moving focus to the smartphone - which is a FOCUS bug, not an opening bug; the
                // phone opened correctly every time.
                //
                // The generic rule below ("a non-null selection means focus is healthy") cannot see
                // this: the selection is alive, interactable and on screen - it is just on the
                // screen we LEFT. Same shape as the popup case above, so it takes the same fix
                // rather than a new mechanism.
                if (IsPhoneState(AccessMod.ReadControllerState()) &&
                    !SelectionIsInsidePhone(es.currentSelectedGameObject))
                {
                    TakeFocusForPhone();
                    return;
                }

                if (es.currentSelectedGameObject != null)
                {
                    // Focus is healthy. Remember WHERE, so that if the game destroys this selection
                    // (which it does on every switch to keyboard mode) we can put the player back
                    // instead of guessing. Free: this branch already runs every frame.
                    _lastGoodFocus = es.currentSelectedGameObject;
                    _lastGoodState = AccessMod.ReadControllerState();
                    _lastGoodTime = Time.realtimeSinceStartup;

                    // Reset the timer AND the dedup, so the next genuine loss of
                    // focus on this same screen is treated as new. Without clearing this, a screen
                    // the player leaves and returns to would be recovered once and then never
                    // again - the "uncleared dedup turns a duplicate into permanent silence" trap.
                    //
                    // ⚠ BUT ONLY ONCE THE SELECTION HAS HELD. Clearing the guard the instant focus
                    // is non-null makes "one recovery per screen" mean "one recovery per FRAME"
                    // whenever our own selection does not stick: recover -> valid for a frame ->
                    // guard cleared -> lost again -> recover. That is not hypothetical - it ran
                    // seven times in a row on the mod menu's dead-end AddAllModButton and the player
                    // reported the screen as "focusing nothing".
                    //
                    // Requiring the selection to survive HoldSeconds distinguishes a real recovery
                    // (focus lands and stays) from a fight (focus lands and is torn away). The root
                    // cause there was unwired scene data, fixed in ModMenuPatches - this is the
                    // backstop that keeps ANY such screen from becoming a loop.
                    _nullSince = -1f;
                    if (_lastRecoveredState != null)
                    {
                        if (_recoveredAt < 0f) _recoveredAt = Time.realtimeSinceStartup;
                        if (Time.realtimeSinceStartup - _recoveredAt >= HoldSeconds)
                        {
                            _lastRecoveredState = null;
                            _recoveredAt = -1f;
                        }
                    }
                    return;
                }

                string state = AccessMod.ReadControllerState();
                if (!NeedsSelection(state))
                {
                    _nullSince = -1f;
                    return;
                }

                // ⚠ A PHONE_* state does NOT mean the phone is usable. When canOpenSmartPhone is
                // false the game still enters GameState.SMART_PHONE and sets PHONE_HOME, but it
                // raises screenBlockPanel and calls CloseUnaccesablePhone() 0.3s later
                // (TG_SmartPhoneManager.OpenSmartPhone, else branch). The phone's own buttons are
                // therefore never available, and FindEntryControl - which scans the whole scene -
                // fell through to the BREWING buttons still underneath. Live proof, log
                // 26-8-10_2-21-28: "[Focus] supplied missing keyboard selection on PHONE_HOME:
                // ButtonBrew", three times. That announces brewing controls under a phone label,
                // which is the two-disagreeing-cursors failure this class exists to prevent.
                if (IsBlockedPhone(state))
                {
                    _nullSince = -1f;
                    return;
                }

                // ⚠ THE PROFILE PICKER NULLS ITS OWN SELECTION ON PURPOSE, AND RECOVERING THERE
                // MOVES THE PLAYER. Pressing Enter flips a card open, and DoFilpToOptionAnimation
                // sets `button.interactable = false` for the whole 0.6 s flip. The selected card is
                // therefore not a valid Selectable for that moment, so FindEntryControl skipped it
                // and selected a DIFFERENT card - reported as "when I press enter on the first one
                // it moves focus to the third one".
                //
                // Live proof, log 26-8-10_17-54-25: focus was on "Slot 1 of 3, Drew"; Enter produced
                // a recovery line, then "Slot 3 of 3, Barista" 80 ms later, and only THEN
                // "[Profile] Drew, open". The game opened the RIGHT profile all along - the mod
                // yanked the cursor away while it was doing so.
                //
                // A null selection on this screen is the game mid-transition, not a screen that
                // needs rescuing, so we stand down while any card is animating or open. The open
                // card is driven by Enter/Escape/X off `currentSelected`, not by a focused
                // Selectable, so there is genuinely nothing to recover to until it closes again.
                if (state == "SELECT_PROFILE" && IsProfileCardBusy())
                {
                    _nullSince = -1f;
                    return;
                }

                // Start (or continue) timing this null.
                if (_nullSince < 0f) _nullSince = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - _nullSince < SettleSeconds) return;

                // One recovery per visit to a screen. If our selection does not stick, something
                // else is actively clearing it and retrying every frame would be a fight.
                if (_lastRecoveredState == state) return;

                // PREFER THE CONTROL THE PLAYER WAS ACTUALLY ON. Only when there is no valid memory
                // do we fall back to the scene scan, which cannot know where the cursor used to be.
                Selectable remembered = RememberedTarget(state);
                if (remembered != null)
                {
                    _lastRecoveredState = state;
                    SelectAndNotify(remembered);
                    MelonLogger.Msg("[Focus] restored the control you were on: "
                        + remembered.gameObject.name);
                    return;
                }

                Selectable target = FindEntryControl(state);
                if (target == null) return;

                _lastRecoveredState = state;
                SelectAndNotify(target);

                MelonLogger.Msg("[Focus] supplied missing keyboard selection on " + state
                    + ": " + target.gameObject.name);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Focus] recovery threw: " + e.Message);
            }
        }

        /// <summary>
        /// True when the state is a phone screen but the phone is not actually usable.
        ///
        /// Read live from the manager rather than assumed, because canOpenSmartPhone is set by the
        /// story (TG_SmartPhoneManager:122) and flips during a playthrough - a build-time constant
        /// would be wrong on whichever side it guessed. If the manager cannot be found we return
        /// false, i.e. recover as before: the pre-existing behaviour is the safer default for the
        /// retail build, where the phone genuinely does open.
        /// </summary>
        private static bool IsBlockedPhone(string state)
        {
            if (state == null || !state.StartsWith("PHONE")) return false;

            TG_SmartPhoneManager phone = UnityEngine.Object.FindObjectOfType<TG_SmartPhoneManager>();
            if (phone == null) return false;

            return !phone.canOpenSmartPhone;
        }

        /// <summary>True for the two states where a modal popup owns input.</summary>
        private static bool IsPopUpState(string state)
        {
            return state == "POP_UP_CONFIRMATION" || state == "POP_UP_LOAD";
        }

        /// <summary>
        /// Every phone screen. Matched on the PHONE_ prefix rather than listing the seven states
        /// (PHONE_HOME, PHONE_SOCMED, PHONE_SOCMED_ACCOUNT, PHONE_DRINK, PHONE_NEWSPAPER,
        /// PHONE_MUSIC, PHONE_MUSIC_NOW_PLAYING) so a state added later is covered by default -
        /// the failure direction that matters here is MISSING one, which reads to the player as a
        /// screen that does not take focus.
        /// </summary>
        private static bool IsPhoneState(string state)
        {
            return state != null && state.StartsWith("PHONE");
        }

        /// <summary>
        /// The phone's root panel, or null when it is not up. `smartPhonePanel` is private, and it
        /// is deliberately the target rather than `homeScreenPanel`: it is the parent of the home
        /// screen AND of all four app panes, so one IsChildOf test covers every phone screen.
        /// </summary>
        private static Component PhoneRoot()
        {
            try
            {
                Type mgrType = AccessTools.TypeByName("TG_SmartPhoneManager");
                if (mgrType == null) return null;

                UnityEngine.Object mgr = UnityEngine.Object.FindObjectOfType(mgrType);
                if (mgr == null) return null;

                Component panel = AccessTools.Field(mgrType, "smartPhonePanel")?.GetValue(mgr) as Component;
                return panel != null && panel.gameObject.activeInHierarchy ? panel : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool SelectionIsInsidePhone(GameObject selected)
        {
            if (selected == null) return false;

            Component phone = PhoneRoot();
            if (phone == null) return false;

            return selected.transform.IsChildOf(phone.transform);
        }

        /// <summary>
        /// Moves focus onto the phone when it has opened over a screen that still holds the
        /// selection. Mirrors TakeFocusForPopUp deliberately - same problem, same shape.
        ///
        /// ⚠ Picks the first usable control INSIDE the phone rather than a named button, because
        /// which screen is showing depends on the app: the home screen has its four app buttons,
        /// but an app pane has its own controls and no app buttons at all.
        /// </summary>
        private static void TakeFocusForPhone()
        {
            try
            {
                Component phone = PhoneRoot();
                if (phone == null) return;

                Selectable target = null;
                foreach (Selectable s in phone.GetComponentsInChildren<Selectable>(false))
                {
                    if (!IsUsable(s)) continue;
                    target = s;
                    break;
                }

                if (target == null) return;

                if (EventSystem.current != null &&
                    ReferenceEquals(EventSystem.current.currentSelectedGameObject, target.gameObject)) return;

                SelectAndNotify(target);
                MelonLogger.Msg("[Focus] phone took focus from the screen behind it: "
                    + target.gameObject.name);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Focus] phone focus threw: " + e.Message);
            }
        }

        /// <summary>
        /// The popup UI that is currently up, or null. Reads TG_PopUpManager's OWN serialized
        /// references rather than searching the scene, so we act on exactly the object the game
        /// considers current. Bound by string: both fields are private.
        /// </summary>
        private static Component ActivePopUp()
        {
            try
            {
                Type mgrType = AccessTools.TypeByName("TG_PopUpManager");
                if (mgrType == null) return null;

                UnityEngine.Object mgr = UnityEngine.Object.FindObjectOfType(mgrType);
                if (mgr == null) return null;

                foreach (string field in new[] { "popUpConfirmationUI", "popUpLoadUI" })
                {
                    Component ui = AccessTools.Field(mgrType, field)?.GetValue(mgr) as Component;
                    if (ui != null && ui.gameObject.activeInHierarchy) return ui;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// True when the current selection already sits inside the live popup, in which case the
        /// game (or an earlier pass of ours) has done the job and we must not touch it.
        /// </summary>
        private static bool SelectionIsInsidePopUp(GameObject selected)
        {
            if (selected == null) return false;

            Component popUp = ActivePopUp();
            if (popUp == null) return false;

            return selected.transform.IsChildOf(popUp.transform);
        }

        /// <summary>
        /// Moves focus onto the live popup's default button.
        ///
        /// Prefers the popup's OWN `yesButton` - the same control the game selects on a gamepad
        /// (TG_PopUpManager.SelectButtonPopUpConfirmation/Load, both entirely JOYSTICK-gated, which
        /// is why the keyboard needs this at all). Falls back to the first usable Selectable inside
        /// the popup so a prefab change cannot leave the dialog unreachable.
        ///
        /// ⚠ Scoped HARD to the popup, with no cross-scope fallback: the whole point is that the
        /// screen behind stays interactable, so "best candidate anywhere" is exactly the wrong
        /// answer here.
        /// </summary>
        private static void TakeFocusForPopUp()
        {
            try
            {
                Component popUp = ActivePopUp();
                if (popUp == null) return;

                Selectable target = AccessTools.Field(popUp.GetType(), "yesButton")?.GetValue(popUp) as Selectable;

                if (!IsUsable(target))
                {
                    target = null;
                    foreach (Selectable s in popUp.GetComponentsInChildren<Selectable>(false))
                    {
                        if (!IsUsable(s)) continue;
                        target = s;
                        break;
                    }
                }

                if (target == null) return;

                // Do not re-announce or re-select what is already focused.
                if (EventSystem.current != null &&
                    ReferenceEquals(EventSystem.current.currentSelectedGameObject, target.gameObject)) return;

                SelectAndNotify(target);
                MelonLogger.Msg("[Focus] popup took focus from the screen behind it: "
                    + target.gameObject.name);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Focus] popup focus threw: " + e.Message);
            }
        }

        /// <summary>
        /// Selects a control AND tells the game's own handler about it.
        ///
        /// ⚠ BOTH HALVES ARE REQUIRED, and they live in one helper so the memory path and the scan
        /// path cannot drift apart. `Select()` alone moves only Unity's EventSystem; it does NOT
        /// reach TG_Button, which is a SEPARATE MonoBehaviour implementing ISelectHandler in its own
        /// right and merely HOLDING a `button` reference. (An older form here called
        /// `(target as Button)?.OnSelect(null)`, i.e. UnityEngine.UI.Button.OnSelect, which is a
        /// dead end for the same reason.)
        ///
        /// On most screens that omission was invisible. On the profile picker it desynchronised two
        /// cursors: TG_ProfileSlotFlipUI.MouseHoverEvent is what calls
        /// TG_ProfileUIManager.SetCurrentSelected(indexButton), and `currentSelected` is what
        /// Enter/Escape/X actually act on. So the mod moved the EventSystem while the game's own
        /// pointer stayed put, and Enter opened a DIFFERENT profile from the one just announced.
        /// Live proof, log 26-8-10_17-47-22: "[Focus] Slot 3 of 3, Barista" at :53.953 followed
        /// 140 ms later by "[Profile] Drew, open" - reported as "it selects a different one".
        ///
        /// ExecuteEvents.Execute finds the handler wherever it lives, so this drives the GAME's
        /// single cursor rather than introducing a second one (rule 4).
        /// </summary>
        private static void SelectAndNotify(Selectable target)
        {
            target.Select();
            ExecuteEvents.Execute(target.gameObject, new BaseEventData(EventSystem.current),
                ExecuteEvents.selectHandler);
        }

        /// <summary>
        /// The remembered control, if it is still a safe thing to select; otherwise null.
        ///
        /// Four independent checks, all applied at USE time rather than at record time, because a
        /// control can be perfectly valid when recorded and destroyed a frame later:
        ///  1. SAME SCREEN. The state string is the game's own screen machine, and it is what
        ///     decided which branch of CheckRemovedState ran in the first place. If the screen
        ///     changed, the memory is void - this is the primary guard against resurrecting a
        ///     control from a screen that has since closed.
        ///  2. STILL ALIVE. Unity's overloaded == reports a destroyed object as null, so this
        ///     catches a torn-down control even when the state string coincidentally matches.
        ///  3. STILL USABLE. Shares IsUsable with FindEntryControl so the two paths cannot disagree
        ///     about what "selectable" means.
        ///  4. STILL FRESH. See MemorySeconds - the backstop for pooled prefabs.
        /// </summary>
        private static Selectable RememberedTarget(string state)
        {
            if (_lastGoodFocus == null) return null;
            if (_lastGoodState != state) return null;
            if (Time.realtimeSinceStartup - _lastGoodTime > MemorySeconds) return null;

            Selectable s = _lastGoodFocus.GetComponent<Selectable>();
            return IsUsable(s) ? s : null;
        }

        /// <summary>
        /// Whether a Selectable is a legitimate thing to hand the player: on screen, interactable,
        /// and not explicitly excluded from navigation.
        ///
        /// A Selectable with Navigation.Mode.None is a display element, not an entry point - the
        /// game uses exactly that for decorative controls.
        /// </summary>
        private static bool IsUsable(Selectable s)
        {
            if (s == null) return false;
            if (!s.gameObject.activeInHierarchy) return false;
            if (!s.interactable) return false;
            if (s.navigation.mode == Navigation.Mode.None) return false;
            return true;
        }

        /// <summary>
        /// True while any profile card is flipping or sitting open - i.e. while a null EventSystem
        /// selection is the game's own doing rather than the gated-Select bug this class fixes.
        ///
        /// Reads `isAnimating` and `isOpen` off each TG_ProfileSlotFlipUI (both real fields,
        /// verified by reflection over the shipped assembly; `isOpen` is public and `isAnimating`
        /// private, hence AccessTools rather than a cast). Bound by STRING throughout so this file
        /// keeps no compile-time dependency on a retail-only screen.
        ///
        /// Fails OPEN - if anything cannot be read we return false and recover as before, because
        /// the pre-existing behaviour is the safer default on every other screen.
        /// </summary>
        private static bool IsProfileCardBusy()
        {
            try
            {
                Type mgrType = AccessTools.TypeByName("TG_ProfileUIManager");
                if (mgrType == null) return false;

                UnityEngine.Object mgr = UnityEngine.Object.FindObjectOfType(mgrType);
                if (mgr == null) return false;

                System.Collections.IList slots =
                    AccessTools.Field(mgrType, "profileSlotUIList")?.GetValue(mgr) as System.Collections.IList;
                if (slots == null) return false;

                for (int i = 0; i < slots.Count; i++)
                {
                    object slot = slots[i];
                    if (slot == null) continue;

                    Type t = slot.GetType();
                    object animating = AccessTools.Field(t, "isAnimating")?.GetValue(slot);
                    object open = AccessTools.Field(t, "isOpen")?.GetValue(slot);

                    if (animating is bool && (bool)animating) return true;
                    if (open is bool && (bool)open) return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Finds a screen's own panel transform, to scope the entry-control search to it.
        ///
        /// Bound entirely by STRING - type name and field name both looked up at runtime - because
        /// TG_ModManagerUI exists ONLY in the retail assembly. Naming it in code would not compile
        /// against the demo, which this project still builds and tests against. TG_ProfileUIManager
        /// does exist in both, but goes through the same path so there is one mechanism rather than
        /// two.
        ///
        /// Returns null when anything is missing, which the caller treats as "no scope" - i.e. the
        /// pre-existing scene-wide behaviour, logged as a fallback rather than silently applied.
        /// </summary>
        private static Transform FindPanelScope(string typeName, string panelField)
        {
            try
            {
                Type t = AccessTools.TypeByName(typeName);
                if (t == null) return null;

                UnityEngine.Object owner = UnityEngine.Object.FindObjectOfType(t);
                if (owner == null) return null;

                GameObject panel = AccessTools.Field(t, panelField)?.GetValue(owner) as GameObject;
                return panel == null ? null : panel.transform;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Picks the control the game would have selected: the first interactable Selectable that
        /// is actually on screen, in the scene's own order.
        ///
        /// Chosen over asking each manager for its specific entry button (socialMediaAppButton,
        /// yesButton, buttonList[0]...) because that would hardcode seven reflection paths that
        /// each need verifying against the real assembly, and would cover only the screens this
        /// audit happened to find. The game's gated helpers all select the FIRST entry of the
        /// panel they just opened, which is what this returns.
        ///
        /// ⚠ Ordering is FindObjectsOfType's, which is not guaranteed. That is acceptable here
        /// because the goal is to give the player A cursor on a screen that has none - any valid
        /// entry point restores navigation, and from there the game's own navigation graph takes
        /// over. It is NOT acceptable to select something invisible or disabled, hence the checks.
        ///
        /// ⚠ SCOPED TO THE PHONE ON PHONE STATES (added 2026-08-10). A scene-wide scan is only safe
        /// when the screen that needs a cursor is the only thing on screen. The phone is drawn OVER
        /// a live café - brewing buttons stay active and interactable underneath it - so an
        /// unscoped scan can return a control from the screen BEHIND the one the player is on. That
        /// is the ButtonBrew-under-a-phone-label bug from 26-8-10_2-21-28. Restricting the search to
        /// the phone's own hierarchy makes the failure honest: if the phone has nothing selectable,
        /// this returns null and recovery does nothing, instead of confidently selecting the wrong
        /// screen.
        /// </summary>
        private static Selectable FindEntryControl(string state)
        {
            Selectable[] all = UnityEngine.Object.FindObjectsOfType<Selectable>();

            // On a phone screen, prefer controls inside the phone itself.
            //
            // ⚠ The scope is a PREFERENCE, not a hard filter, and that is deliberate. The panels
            // are serialized inspector references (TG_SmartPhoneManager:28-32), which says nothing
            // about where they sit in the TRANSFORM hierarchy - a prefab may well parent them to a
            // sibling canvas rather than under the manager. Treating the scope as a hard filter
            // would then match nothing and silently disable phone recovery altogether, trading a
            // wrong-cursor bug for a no-cursor bug on a screen the player cannot navigate at all.
            // Neither is acceptable, so: take an in-scope candidate if one exists, otherwise fall
            // back to the first valid candidate anywhere and LOG that it came from outside the
            // phone. If that log line ever appears on retail, the hierarchy assumption is wrong and
            // the fallback is visible instead of silent.
            Transform scope = null;
            if (state != null && state.StartsWith("PHONE"))
            {
                TG_SmartPhoneManager phone = UnityEngine.Object.FindObjectOfType<TG_SmartPhoneManager>();
                if (phone != null) scope = phone.transform;
            }
            else if (state == "SELECT_PROFILE")
            {
                // The picker is drawn OVER the main menu, which keeps its own controls alive
                // underneath - the identical hazard to the phone-over-café case above, and it would
                // fail the identical way: a main-menu button selected and announced under a profile
                // label. Scoped to the panel the manager itself activates rather than to the manager
                // (a TG_GenericSingelton whose transform need not be the panel's parent), and kept a
                // PREFERENCE with the logged fallback for the reason spelled out above.
                scope = FindPanelScope("TG_ProfileUIManager", "profileSelectCanvasPanel");
            }
            else if (state == "MOD_MENU")
            {
                // Retail-only, and bound entirely by STRING: TG_ModManagerUI does not exist in the
                // demo assembly, so naming the type in code would not compile against it.
                // ⚠ The field is `canvas`, not any of the *CanvasPanel names the rest of this
                // codebase uses - read off TG_ModManagerUI:14, not guessed from the sibling screens.
                scope = FindPanelScope("TG_ModManagerUI", "canvas");
            }

            Selectable fallback = null;

            for (int i = 0; i < all.Length; i++)
            {
                // Shared with the remembered-control path (RememberedTarget), so the two can never
                // disagree about what counts as a selectable entry point.
                Selectable s = all[i];
                if (!IsUsable(s)) continue;

                if (scope != null && !s.transform.IsChildOf(scope))
                {
                    if (fallback == null) fallback = s;
                    continue;
                }

                return s;
            }

            // ⚠ NO CROSS-SCREEN FALLBACK ON THE PHONE. Everywhere else, handing back the best
            // candidate from outside the scope beats leaving the player with no cursor at all. The
            // phone is the exception, because it is drawn OVER a live café whose brewing buttons
            // stay active and interactable - so "the first valid control anywhere" is reliably a
            // BREWING control, and selecting it silently moves the player out of the screen they
            // just opened.
            //
            // Live proof, log 26-8-10_18-17-43: the phone opened and focus landed on "Coffee" (an
            // ingredient) and then "Brewpad" (the brew/phone switch cursor the game shows when the
            // phone is opened FROM brewing). Reported as "phone navigation doesn't seem to be
            // working" and "the brewpad didn't get initial focus" - the same event from two sides.
            //
            // Returning null here means recovery does nothing this frame and tries again on the
            // next, which is correct: the phone's own buttons become interactable when its 0.6 s
            // tween completes, and the watcher-based announcement now waits for the same moment.
            if (fallback != null && state != null && state.StartsWith("PHONE"))
            {
                MelonLogger.Msg("[Focus] nothing selectable inside the phone yet on " + state
                    + " (best outside candidate was " + fallback.gameObject.name
                    + ") - standing down rather than selecting the cafe behind it.");
                return null;
            }

            if (fallback != null)
            {
                MelonLogger.Warning("[Focus] no selectable inside the scoped panel on " + state
                    + "; falling back to " + fallback.gameObject.name
                    + " - the panel-hierarchy assumption may be wrong for this screen.");
            }

            return fallback;
        }
    }
}
