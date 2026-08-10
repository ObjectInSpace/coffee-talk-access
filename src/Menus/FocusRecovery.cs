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

        private static float _nullSince = -1f;
        private static string _lastRecoveredState;

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

                // ⚠ BREWING IS DELIBERATELY EXCLUDED, despite TG_DrinkManager:547 having the same
                // gated-Select bug. Two reasons, and both are why a whitelist beats "any state":
                //  1. It already recovers on its own - brewing has many UNgated Select() calls
                //     (:312, :577, :587, :667-672), and it navigated correctly in the 01:16 live
                //     run, so there is nothing to fix.
                //  2. Stepping in would be ACTIVELY HARMFUL. ServeGlassDrink nulls the selection
                //     (:829) while the state is still BREWING and hands off to dialogue. Recovering
                //     there would drag focus back onto an ingredient button mid-serve - the mod
                //     fighting the game for the cursor, which is the failure this class must not
                //     reintroduce.
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

                if (es.currentSelectedGameObject != null)
                {
                    // Focus is healthy. Reset the timer AND the dedup, so the next genuine loss of
                    // focus on this same screen is treated as new. Without clearing this, a screen
                    // the player leaves and returns to would be recovered once and then never
                    // again - the "uncleared dedup turns a duplicate into permanent silence" trap.
                    _nullSince = -1f;
                    _lastRecoveredState = null;
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

                // Start (or continue) timing this null.
                if (_nullSince < 0f) _nullSince = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - _nullSince < SettleSeconds) return;

                // One recovery per visit to a screen. If our selection does not stick, something
                // else is actively clearing it and retrying every frame would be a fight.
                if (_lastRecoveredState == state) return;

                Selectable target = FindEntryControl(state);
                if (target == null) return;

                _lastRecoveredState = state;
                target.Select();

                // OnSelect is what makes the game's own TG_Button react (hover SFX, and the
                // ISelectHandler path that computes brewing stat previews). The game calls both
                // wherever it does this itself, so we match it rather than half-doing it.
                (target as Button)?.OnSelect(null);

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
                Selectable s = all[i];
                if (s == null) continue;
                if (!s.gameObject.activeInHierarchy) continue;
                if (!s.interactable) continue;

                // A Selectable with navigation explicitly turned off is a display element, not an
                // entry point - the game uses Navigation.Mode.None for exactly that.
                if (s.navigation.mode == Navigation.Mode.None) continue;

                if (scope != null && !s.transform.IsChildOf(scope))
                {
                    if (fallback == null) fallback = s;
                    continue;
                }

                return s;
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
