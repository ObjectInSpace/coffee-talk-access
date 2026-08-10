using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine.EventSystems;

namespace CoffeeTalkAccess.Menus
{
    /// <summary>
    /// Repairs a GAME bug that leaves the DEMO's language picker unnavigable in keyboard mode.
    ///
    /// ⚠ THIS FILE IS DEMO-ONLY, AND IT FAILS SILENTLY ON RETAIL BY DESIGN. Retail redesigned the
    /// picker from a grid of flag buttons into a single label with prev/next arrows, and
    /// `SelectAnyFlag` DOES NOT EXIST there (verified by reflection over both assemblies). The
    /// lookup below returns null and this postfix does nothing - correct, because retail's screen
    /// has no per-language Selectable to restore in the first place.
    ///
    /// The consequence is that retail's picker is narrated by a DIFFERENT file,
    /// Menus.LanguagePickerPatches, which hooks `RefreshLanguageUI` (retail-only) and speaks the
    /// spinner's current value. If you are here because the language picker is silent, check WHICH
    /// BUILD you are on before touching this file - the retail path is not this one.
    ///
    /// THE BUG (in Coffee Talk, not in this mod - verified 2026-08-09 from a live log):
    /// CheckActiveController() runs every OnGUI and switches to KEYBOARD mode as soon as
    /// ListenKeyboard() sees any key or mouse event. That calls HandleActiveMouseKeyboard(),
    /// which does:
    ///
    ///     EventSystem.current.SetSelectedGameObject(null);
    ///     CheckRemovedState();
    ///
    /// CheckRemovedState() re-selects something for 12 states (MAIN_MENU, BREWING, OPTION_*,
    /// PHONE_*, INPUT_NAME, ...) but has NO branch for State.INIT_SCREEN - the language picker.
    /// So on that screen the selection is cleared and never restored. InControlInputModule
    /// navigates by moving the CURRENT selection, so with selection null the arrow keys have
    /// nothing to move and the screen goes dead.
    ///
    /// The joystick side of the same manager gets this right: CheckPluggedInState() DOES have an
    /// INIT_SCREEN branch calling languageSettingGO.SelectAnyFlag(). Only the keyboard side
    /// forgets. We simply do for keyboard mode what the game already does for joystick mode,
    /// using the game's OWN method - no reimplementation, no second cursor.
    ///
    /// EVIDENCE this is pre-existing and not ours: the live log shows exactly ONE "[Focus] English"
    /// (the initial button.Select() from TG_InitLanguageSettingMenu) and then 21 seconds of
    /// silence. Focus never moved because there was no focus to move. An earlier build masked
    /// this by forcing JOYSTICK mode permanently, which stopped HandleActiveMouseKeyboard() from
    /// ever running - the bug was hidden, not absent.
    /// </summary>
    [HarmonyPatch]
    public static class InitScreenFix
    {
        /// <summary>
        /// Runs after the game's own CheckRemovedState and fills in the missing INIT_SCREEN case.
        ///
        /// A postfix (not a prefix) so the game's own logic runs first and wins wherever it has an
        /// opinion; we only act on the state it does not handle. And we only act when selection is
        /// ACTUALLY null, so if a future path does restore selection we quietly do nothing rather
        /// than fighting it.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ControllerInputManager), "CheckRemovedState")]
        public static void AfterCheckRemovedState(TG_ControllerInputManager __instance)
        {
            try
            {
                object state = AccessTools.Field(typeof(TG_ControllerInputManager), "currentState")
                    ?.GetValue(__instance);
                if (state == null || state.ToString() != "INIT_SCREEN") return;

                EventSystem es = EventSystem.current;
                if (es == null || es.currentSelectedGameObject != null) return;

                RestoreLanguageSelection();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[InitFix] threw: " + e.Message);
            }
        }

        /// <summary>
        /// Calls the game's own SelectAnyFlag(), reached exactly as CheckPluggedInState reaches it:
        /// TG_InitScreen.Instance.languageSettingGO.SelectAnyFlag().
        ///
        /// SelectAnyFlag re-selects languageButtonList[currentIndexSelected], so the restored
        /// selection is the flag the player was ALREADY on - not a reset to the first entry. That
        /// matters: silently jumping the cursor back to the top on every keypress would be a
        /// different, more confusing bug than the one being fixed.
        /// </summary>
        private static void RestoreLanguageSelection()
        {
            Type initScreen = AccessTools.TypeByName("TG_InitScreen");
            if (initScreen == null) return;

            Type singleton = AccessTools.TypeByName("TG_GenericSingelton`1");
            if (singleton == null) return;

            object instance = AccessTools.Property(singleton.MakeGenericType(initScreen), "Instance")
                ?.GetValue(null);
            if (instance == null) return;

            object langMenu = AccessTools.Field(initScreen, "languageSettingGO")?.GetValue(instance);
            if (langMenu == null) return;

            AccessTools.Method(langMenu.GetType(), "SelectAnyFlag")?.Invoke(langMenu, null);
        }
    }
}
