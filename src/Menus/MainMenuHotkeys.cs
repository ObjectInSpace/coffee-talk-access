using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace CoffeeTalkAccess.Menus
{
    /// <summary>
    /// Reaches the main menu's two SATELLITE buttons from the keyboard: Exit and Mods.
    ///
    /// THE GAP (retail, confirmed by decompile 2026-08-10): TG_MainMenuManager has three buttons
    /// that are NOT in `buttonList` - closeGameButton (Exit, field :34), openModMenuButton (Mods,
    /// :81) and promoButton (a store link). Every cursor path in the game bottoms out at
    /// `buttonList[cursorIdx].MouseHover()`, and ButtonUp/ButtonDown clamp cursorIdx to
    /// buttonList.Count in both directions (:265, :302), so the cursor CANNOT address them. They
    /// are reachable only by mouse click, or by an undiscoverable face-button shortcut:
    ///
    ///   Mods -> XButtonPressed      (TG_ControllerInputManager:1482, MAIN_MENU only)
    ///   Exit -> PauseButtonPressed  (TG_ControllerInputManager:1574, MAIN_MENU only)
    ///
    /// Those shortcuts are advertised by on-screen button prompts, i.e. visually only, and
    /// HandlerKeyboard reads just RB/LB/Escape/Submit/Confirm/SmartPhoneToggle - it never calls
    /// either handler. So on a keyboard the two buttons had NO route at all, discoverable or
    /// otherwise. Ten live retail logs contain zero mentions of Exit or Mods: the cursor never
    /// visited them, so there was never anything to narrate. This is a gap in the GAME, of the same
    /// shape as [[coffee-talk-menus-gamepad-only]].
    ///
    /// WHY HOTKEYS RATHER THAN MENU ENTRIES. Appending them to the arrow-key rotation was the first
    /// design and was rejected twice over:
    ///  - `buttonList` is List&lt;TG_MainMenuButton&gt; but openModMenuButton is a TG_Button, a
    ///    different type with no MouseHover/arrowImage/textComponentToMove. Appending it would throw
    ///    in EnableMainMenu's loop (:1120).
    ///  - Holding our own index past buttonList.Count means a SECOND cursor disagreeing with the
    ///    game's cursorIdx - the exact configuration that once announced "PLAY GAME" while Enter hit
    ///    Exit and quit the game. See [[coffee-talk-own-cursor]].
    /// A hotkey has no index, so there is no cursor to disagree with and no focus to manage.
    ///
    /// We call the GAME's handlers rather than the buttons' onClick directly, so the state checks,
    /// the platform gating and - for Exit - the confirmation popup all remain the game's own.
    /// </summary>
    [HarmonyPatch]
    public static class MainMenuHotkeys
    {
        /// <summary>
        /// Tab = Mods. Chosen to match Tab = phone in gameplay: both open an auxiliary panel, so the
        /// gesture is consistent rather than arbitrary.
        ///
        /// ⚠ Tab IS bound elsewhere - three places, all verified unreachable from the main menu:
        ///  - keyboardPlayerActions.SmartPhoneToggle (KeyboardPlayerActions:53). Its handler acts
        ///    only in BREWING and IN_DIALOGUE, each additionally requiring an in-game scene.
        ///  - Input.GetKeyDown(KeyCode.Tab) in TG_SmartPhoneManager:146 and TG_SmartPhoneApps:52.
        ///    Both sit inside UpdateFunction, the phone's own pump, which runs only while the phone
        ///    is open.
        /// Since we act only in MAIN_MENU, the two uses cannot overlap. Do not re-derive this from
        /// "Tab is the phone key" alone - the raw Input reads bypass the action set and would not
        /// show up in a bindings audit.
        /// </summary>
        private const KeyCode ModsKey = KeyCode.Tab;

        /// <summary>
        /// Escape = Exit, and it is free HERE specifically: EscapeHandler has zero MAIN_MENU
        /// branches (grepped - EnterButtonHandler and ConfirmButtonHandler have none either), so
        /// Escape currently does nothing on this screen.
        ///
        /// Escape is the universal back/close key everywhere else in the game, which makes the main
        /// menu the one screen where it means something different. That is a real wrinkle, accepted
        /// deliberately: the main menu is the top of the navigation tree, so there is nothing to
        /// back out TO, and quitting is what "back" means from there. The game's own confirmation
        /// popup catches a mistaken press and defaults to NO.
        /// </summary>
        private const KeyCode ExitKey = KeyCode.Escape;

        /// <summary>
        /// Hosted on ControllerUpdateFunction for the same reason KeyboardNav is: it runs
        /// unconditionally from Update() in BOTH input modes, whereas HandlerKeyboard runs only in
        /// KEYBOARD mode - which a connected pad can suppress indefinitely, because ListenControllers
        /// reads axis LEVELS rather than press edges and keeps flipping the mode back to JOYSTICK.
        /// Hosting here means these keys work whether or not a controller is attached.
        ///
        /// Read with Input.GetKeyDown rather than by binding onto the joystick action set. Binding
        /// would route the key through HandlerControllerPress, which dispatches on EVERY screen -
        /// and unlike the directional keys KeyboardNav binds, these actions are not idempotent.
        /// Reading the key ourselves lets us gate strictly on MAIN_MENU.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ControllerInputManager), "ControllerUpdateFunction")]
        public static void AfterControllerUpdate(TG_ControllerInputManager __instance)
        {
            try
            {
                if (__instance == null) return;

                bool wantsMods = Input.GetKeyDown(ModsKey);
                bool wantsExit = Input.GetKeyDown(ExitKey);
                if (!wantsMods && !wantsExit) return;

                // MAIN_MENU only. Both game handlers already check this themselves, so this is
                // belt-and-braces - but it also keeps our LOG line honest: without it we would
                // report "Mods" on screens where the call did nothing.
                object state = AccessTools.Field(__instance.GetType(), "currentState")?.GetValue(__instance);
                if (state == null || state.ToString() != "MAIN_MENU") return;

                if (wantsMods) OpenMods(__instance);
                else if (wantsExit) RequestExit(__instance);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Hotkey] main menu hotkey threw: " + e.Message);
            }
        }

        /// <summary>
        /// Opens the mod manager through the game's XButtonPressed, which invokes
        /// openModMenuButton.button.onClick.
        ///
        /// ⚠ The button can legitimately be ABSENT or hidden: InitOpenModButton deactivates it when
        /// TG_ModsManager.Instance.active is false (:1048), and SetActiveAnotherButton hides it
        /// whenever a submenu is open (:1075). Invoking onClick on a hidden button would open a
        /// screen the game considers closed, so we check activeSelf first and SAY when it is
        /// unavailable - silence here is indistinguishable from a dead key, which is the failure
        /// mode this project keeps having to design out.
        /// </summary>
        private static void OpenMods(object mgr)
        {
            object mainMenu = ResolveMainMenu();
            if (mainMenu == null) return;

            var modButton = AccessTools.Field(mainMenu.GetType(), "openModMenuButton")?.GetValue(mainMenu) as MonoBehaviour;
            if (modButton == null || !modButton.gameObject.activeSelf)
            {
                Speak("Mods are not available here.");
                return;
            }

            MelonLogger.Msg("[Hotkey] Tab -> mod menu");
            AccessTools.Method(mgr.GetType(), "XButtonPressed")?.Invoke(mgr, null);
        }

        /// <summary>
        /// Requests a quit through the game's PauseButtonPressed, which calls InitQuitGamePopUp.
        ///
        /// We deliberately do NOT implement our own confirmation. InitQuitGamePopUp raises the
        /// game's own TG_PopUpManager confirmation, defaulting to NO (:476-487), and that popup is
        /// already narrated by TG_PopUpUI.SetPopUpTitleText / SetTextTerm, which the mod hooks. So
        /// the two-press confirm this project requires for destructive actions is satisfied by the
        /// game, on the game's own wording, with no second implementation to drift.
        ///
        /// ⚠ InitQuitGamePopUp early-returns unless closeGameButton is active - it is hidden on
        /// console platforms and while submenus are open - so the same "say it is unavailable"
        /// treatment applies as for Mods.
        /// </summary>
        private static void RequestExit(object mgr)
        {
            object mainMenu = ResolveMainMenu();
            if (mainMenu == null) return;

            var closeButton = AccessTools.Field(mainMenu.GetType(), "closeGameButton")?.GetValue(mainMenu) as UnityEngine.UI.Button;
            if (closeButton == null || !closeButton.gameObject.activeSelf)
            {
                Speak("Exit is not available here.");
                return;
            }

            MelonLogger.Msg("[Hotkey] Escape -> quit confirmation");
            AccessTools.Method(mgr.GetType(), "PauseButtonPressed")?.Invoke(mgr, null);
        }

        /// <summary>Resolves the TG_MainMenuManager singleton, or null if it is not alive.</summary>
        private static object ResolveMainMenu()
        {
            Type mgrType = AccessTools.TypeByName("TG_MainMenuManager");
            Type singleton = AccessTools.TypeByName("TG_GenericSingelton`1");
            if (mgrType == null || singleton == null) return null;

            Type closed = singleton.MakeGenericType(mgrType);
            return AccessTools.Property(closed, "Instance")?.GetValue(null)
                   ?? AccessTools.Field(closed, "Instance")?.GetValue(null);
        }

        private static void Speak(string text)
        {
            MelonLogger.Msg("[Hotkey] " + text);
            Speech.ISpeechOutput speech = AccessMod.Speech;
            if (speech == null || !speech.IsAvailable) return;
            speech.SpeakAs(null, text, UnityAccessibilityLib.TextType.Menu, true);
        }
    }
}
