using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine.EventSystems;

namespace CoffeeTalkAccess.Menus
{
    /// <summary>
    /// Makes the arrow keys behave exactly like the D-pad.
    ///
    /// The controller moves between menu entries by feeding
    /// TG_ControllerInputManager.HandlerControllerPress(), which reads playerActions.Up/Down/
    /// Left/Right and dispatches to UpButtonPressed/DownButtonPressed/... Those routers already
    /// cover every screen (12/12/6/6 states for the directions, 10 for A, 22 for B). That path is
    /// correct and developer-tested. The arrow keys should simply reach it.
    ///
    /// So this class does exactly two things:
    ///  1. Binds the arrow keys and WASD onto the JOYSTICK action set's DIRECTIONAL actions.
    ///  2. Calls the game's own HandlerControllerPress() once per frame while in keyboard mode.
    ///
    /// DIRECTIONS ONLY. Enter/Space and Escape already have handling - HandlerKeyboard reads them
    /// and InControlInputModule submits them to the EventSystem - so the mod stays out of it.
    /// Movement is idempotent (two Ups just move twice, audibly); activation is not, so an extra
    /// consumer on Enter could activate a menu item and then something on the next screen.
    ///
    /// Running HandlerControllerPress does mean it also reads A/B/X/Y, the bumpers, triggers,
    /// Pause and Select from the joystick set. On a keyboard those are all unbound and read as
    /// zero, with ONE exception: the game itself binds Key.E and Key.Q onto RBButton/LBButton
    /// (JoystickPlayerActions.cs:127-128), so E/Q reach BOTH handlers. That is harmless here
    /// because their states are disjoint - the keyboard path acts only in LATTE_ART, and the
    /// joystick bumpers handle 13 states which do not include it.
    ///
    /// Everything else - press/repeat edge detection, per-state dispatch, hold-to-repeat timing -
    /// is the GAME's, unchanged. An earlier version re-implemented the edge detection here with
    /// its own _vertHeld/_horiHeld flags and IsPressed instead of the game's RawValue thresholds.
    /// That was a second copy of behaviour the game already had, free to drift from it. Calling
    /// HandlerControllerPress directly means the arrow keys and the D-pad run the SAME code, so
    /// they cannot diverge.
    ///
    /// WHY BINDING IS SAFE HERE (it was not, in an earlier attempt):
    /// InControl's BindingSource carries a single `BoundTo` back-pointer, and Coffee Talk has a
    /// THIRD action set - InputModuleActionAdapter.InputModuleActions - which binds the arrow keys
    /// to drive Unity's EventSystem. We construct FRESH KeyBindingSource objects, so AddBinding
    /// accepts them and the EventSystem's own bindings are untouched; both sets see the key.
    /// The danger is not stolen input but DOUBLE handling, which is addressed below.
    ///
    /// ⚠ DOUBLE-STEP on EventSystem screens: on the language picker (INIT_SCREEN)
    /// InControlInputModule already moves the selection from the arrow keys. Running the routers
    /// there too is harmless ONLY because every router no-ops on INIT_SCREEN - it appears in ZERO
    /// of UpButtonPressed/DownButtonPressed/LeftButtonPressed/RightButtonPressed. If a future
    /// screen is driven by BOTH the EventSystem and a router, this is where it would double-step,
    /// and the fix would be to gate on currentState here.
    ///
    /// This runs in BOTH input modes, hosted on ControllerUpdateFunction. An earlier version ran
    /// only in KEYBOARD mode (postfixing HandlerKeyboard) and relied on the game's mode switch to
    /// reach it - which a connected pad prevents indefinitely, killing the arrow keys everywhere.
    /// See AfterControllerUpdate for the race that caused it. Because we now run in joystick mode
    /// too, the once-per-frame guard there is what prevents double-pumping the routers.
    /// </summary>
    [HarmonyPatch]
    public static class KeyboardNav
    {
        private static bool _bound;
        private static bool _bindFailed;

        /// <summary>
        /// Runs right after the game's own per-frame input pump, in BOTH input modes.
        ///
        /// ⚠ HOST CHOICE IS LOAD-BEARING - do not move this back onto HandlerKeyboard.
        /// This was originally a postfix on TG_KeyboardHotkeyManager.HandlerKeyboard, which the
        /// game calls only in KEYBOARD mode (ControllerUpdateFunction:267-274). That made the whole
        /// feature hostage to a race the keyboard cannot win while a pad is attached:
        ///
        ///   - CheckActiveController() runs from OnGUI, which fires SEVERAL times per frame.
        ///   - An arrow key sets KEYBOARD (ListenKeyboard is true for any Event.current.isKey).
        ///   - But ListenControllers() reads .Value - a LEVEL, not a press edge - across every
        ///     stick, trigger and the D-pad. A pad that is merely resting near non-zero, or still
        ///     being held, returns true and flips the mode straight back to JOYSTICK.
        ///
        /// So the arrow key never got a frame in KEYBOARD mode, HandlerKeyboard never ran, and this
        /// postfix never fired. Confirmed live 2026-08-10: a whole session logged `mode = JOYSTICK`
        /// at every sample with exactly ONE `[Nav] bound` line, from a single early frame before
        /// the pad went active. Arrow keys were dead on every screen, menus included.
        ///
        /// ControllerUpdateFunction runs unconditionally from Update(), so hosting here removes the
        /// mode dependency entirely: arrows work whether or not a pad is connected, and the two
        /// input methods stay live simultaneously.
        ///
        /// Binding happens lazily on the first call rather than at mod init, because
        /// TG_ControllerInputManager is a scene singleton whose playerActions are created in
        /// InitController() - neither exists when the mod loads.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ControllerInputManager), "ControllerUpdateFunction")]
        public static void AfterControllerUpdate(TG_ControllerInputManager __instance)
        {
            try
            {
                object mgr = __instance;
                if (mgr == null) return;

                object padActions = AccessTools.Field(mgr.GetType(), "playerActions")?.GetValue(mgr);
                if (padActions == null) return;

                if (!_bound && !_bindFailed) BindKeyboardToPad(padActions);
                if (!_bound) return;

                // ONCE PER FRAME, from any source. In JOYSTICK mode the game has ALREADY called
                // HandlerControllerPress this frame, and calling it again would not double-step
                // (vertButtonPressed/horiButtonPressed are manager fields, so the second call falls
                // into the WasRepeated branch) but it WOULD perturb the hold-repeat timing. In
                // KEYBOARD mode the game called HandlerKeyboard instead and nothing has pumped the
                // directional routers, so we do it here.
                if (!GameAlreadyPumpedThisFrame(mgr))
                {
                    // The game's own input router, with the game's own edge detection and repeat
                    // timing. Arrow keys now travel the exact path the D-pad travels.
                    AccessTools.Method(mgr.GetType(), "HandlerControllerPress")?.Invoke(mgr, null);
                }

                ForwardSubmit(mgr);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Nav] keyboard nav threw: " + e.Message);
            }
        }

        /// <summary>
        /// True when the game itself already pumped HandlerControllerPress this frame, i.e. it is
        /// in JOYSTICK mode. Read from the game's own mode field rather than tracked with our own
        /// frame counter, so there is no second copy of state that can drift from the game's.
        /// </summary>
        private static bool GameAlreadyPumpedThisFrame(object mgr)
        {
            object mode = AccessTools.Field(mgr.GetType(), "currentTypeController")?.GetValue(mgr);
            return mode != null && mode.ToString() == "JOYSTICK";
        }

        /// <summary>
        /// Routes Enter to the game's activate handler on screens where nothing else will.
        ///
        /// THE PROBLEM, confirmed live 2026-08-09: arrows moved the main menu and announced
        /// correctly, but Enter did nothing. Coffee Talk drives the main menu with its OWN private
        /// `cursorIdx` and NEVER calls EventSystem.SetSelectedGameObject or Selectable.Select()
        /// there - grep TG_MainMenuManager/TG_MainMenuButton for either: zero hits. So:
        ///  - Unity's EventSystem has NO selection, so InControlInputModule has nothing to submit.
        ///  - HandlerKeyboard's EnterButtonHandler/ConfirmButtonHandler cover MAIN_MENU zero times
        ///    (they handle only READING_NEWSPAPER, and INPUT_NAME/POP_UP_LOAD/POP_UP_CONFIRMATION).
        /// Enter therefore had no route at all. A controller works because AButtonPressed ->
        /// InvokeButton() reads cursorIdx directly, which is exactly the path we now borrow.
        ///
        /// We do NOT bind Enter onto the AButton action, because HandlerControllerPress would then
        /// fire it on EVERY screen including ones the EventSystem drives, giving two activations
        /// for one press. Instead we forward it ourselves, only when the EventSystem holds no
        /// selection - i.e. only when nobody else can act on it.
        ///
        /// Checking live selection beats listing states: screens that DO use selection (the
        /// language picker via TG_InitLanguageSettingMenu, pop-ups via TG_PopUpManager, brewing
        /// via TG_DrinkManager - 12 files call .Select()) are excluded automatically, and any
        /// future screen gets the right behaviour without us enumerating it correctly.
        ///
        /// ⚠ We read keyboardPlayerActions.Submit, which is bound to Space/Return ONLY
        /// (KeyboardPlayerActions:51-52) - never the pad's A button - so a controller press cannot
        /// arrive through here. That matters now that this runs in joystick mode too: the guard
        /// below is what stops ONE Enter press becoming TWO activations, because in joystick mode
        /// the game's own HandlerControllerPress is already dispatching AButtonPressed each frame.
        /// Movement is idempotent and survives being pumped twice; activation is not.
        /// </summary>
        private static void ForwardSubmit(object mgr)
        {
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null) return;

            // In JOYSTICK mode the game already routes A itself. Forwarding as well would give two
            // activations for one press - the exact hazard the class comment warns about.
            if (GameAlreadyPumpedThisFrame(mgr)) return;

            // Read the game's own Submit action rather than Input.GetKeyDown, so this honours
            // whatever keys the game has bound (Return, Space, PadEnter) instead of hardcoding.
            object kb = AccessTools.Field(mgr.GetType(), "keyboardHotkeyManager")?.GetValue(mgr);
            object actions = kb == null
                ? null
                : AccessTools.Field(kb.GetType(), "keyboardPlayerActions")?.GetValue(kb);
            if (actions == null) return;

            object submit = AccessTools.Field(actions.GetType(), "Submit")?.GetValue(actions);
            if (submit == null) return;

            object pressed = AccessTools.Property(submit.GetType(), "WasPressed")?.GetValue(submit, null);
            if (!(pressed is bool) || !(bool)pressed) return;

            AccessTools.Method(mgr.GetType(), "AButtonPressed")?.Invoke(mgr, null);
        }

        /// <summary>
        /// Adds keyboard bindings to the joystick action set, so HandlerControllerPress sees the
        /// arrow keys as directional input.
        ///
        /// AddBinding (not AddDefaultBinding): it returns false on conflict instead of THROWING
        /// InControlException, and an exception thrown into the game's frame is exactly what a mod
        /// must not risk. The InControl 1.7.3 docs describe regular bindings as "added by users,
        /// most likely at runtime", so this is the supported use, not a trick.
        ///
        /// ⚠ THE TWO BUILDS SHIP DIFFERENT InControl VERSIONS - demo 1.7.3 (build 9338), retail
        /// 1.8.5 (build 9368), read from InControl.VersionInfo.InControlVersion() since the assembly
        /// metadata is all zeros. Retail also renames every device profile
        /// (InControl.Xbox360WinProfile -> InControl.UnityDeviceProfiles.Xbox360WindowsUnityProfile)
        /// and replaces LastResortRegex with LastResortMatchers/InputDeviceMatcher. (The vendor
        /// changelog does not mention that refactor in 1.8.0-1.8.5, so it is NOT datable to this
        /// bump - it may predate 1.7.3 in the paid edition. Don't infer a cause from the version.)
        ///
        /// None of it is touched here: verified against BOTH DLLs that InControl.Key (132 values,
        /// all ten names below present), InControl.KeyBindingSource, its (Key[]) constructor, and
        /// AddBinding returning BOOL are identical. The three APIs 1.8.0 actually REMOVED
        /// (CustomInputDeviceProfile, InputManager.Setup/Reset) are used by neither the mod nor the
        /// game. The bind path is version-independent; do not re-investigate the bump.
        ///
        /// ⚠ To re-check InControl, use the VENDOR's site, not the source: the GitHub repo is the
        /// open-source edition, frozen at 1.4.4, and covers neither version. The changelog and API
        /// reference at gallantgames.com are current.
        ///
        /// Note the game itself already mixes key and device bindings on this very set -
        /// JoystickPlayerActions.cs:127-128 binds Key.E and Key.Q onto RBButton/LBButton - so a
        /// Key binding living beside an InputControlType binding is proven in this build.
        /// </summary>
        private static void BindKeyboardToPad(object padActions)
        {
            // DIRECTIONS ONLY.
            //
            // Enter/Space and Escape are deliberately NOT bound. HandlerKeyboard already reads
            // them (Submit -> EnterButtonHandler, Confirm -> ConfirmButtonHandler, Escape ->
            // EscapeHandler) and InControlInputModule submits them to the EventSystem as well.
            // Binding them onto AButton/BButton too would put a THIRD consumer on one keypress -
            // and unlike the directions, activation is not idempotent: two Enters can activate a
            // menu item and then whatever the next screen puts under the cursor. Directions are
            // the actual gap, so directions are all we fill.
            int added = 0;
            added += Bind(padActions, "Up", "UpArrow", "W");
            added += Bind(padActions, "Down", "DownArrow", "S");
            added += Bind(padActions, "Left", "LeftArrow", "A");
            added += Bind(padActions, "Right", "RightArrow", "D");

            if (added == 0)
            {
                _bindFailed = true;
                MelonLogger.Error("[Nav] no keyboard bindings were added; arrow keys will not work.");
                return;
            }

            _bound = true;
            MelonLogger.Msg("[Nav] arrow keys bound to the game's D-pad actions (" + added + " bindings).");
        }

        private static int Bind(object actions, string actionName, params string[] keyNames)
        {
            object action = AccessTools.Field(actions.GetType(), actionName)?.GetValue(actions);
            if (action == null)
            {
                MelonLogger.Warning("[Nav] action '" + actionName + "' not found.");
                return 0;
            }

            Type keyEnum = AccessTools.TypeByName("InControl.Key");
            Type keySource = AccessTools.TypeByName("InControl.KeyBindingSource");
            if (keyEnum == null || keySource == null) return 0;

            // KeyBindingSource(params Key[]) - a one-element array is a plain key, not a combo.
            ConstructorInfo ctor = keySource.GetConstructor(new[] { keyEnum.MakeArrayType() });
            MethodInfo addBinding = AccessTools.Method(action.GetType(), "AddBinding");
            if (ctor == null || addBinding == null) return 0;

            int added = 0;
            foreach (string keyName in keyNames)
            {
                try
                {
                    if (!Enum.IsDefined(keyEnum, keyName)) continue;

                    Array arr = Array.CreateInstance(keyEnum, 1);
                    arr.SetValue(Enum.Parse(keyEnum, keyName), 0);

                    object result = addBinding.Invoke(action, new[] { ctor.Invoke(new object[] { arr }) });
                    if (result is bool && (bool)result) added++;
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[Nav] binding " + keyName + " -> " + actionName + ": " + e.Message);
                }
            }
            return added;
        }
    }
}
