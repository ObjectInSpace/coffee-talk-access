using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace CoffeeTalkAccess.FullGame
{
    /// <summary>
    /// Backspace = step back one level inside the smartphone, the keyboard's missing equivalent of
    /// the gamepad's B / Circle.
    ///
    /// THE GAP. The game declares a keyboard Back action - `Back = CreatePlayerAction("BButton")`
    /// (KeyboardPlayerActions:35) - and then never binds it to a key and never reads it. It is dead
    /// code. So on a gamepad B steps back through 22 states, and on a keyboard nothing does.
    ///
    /// ESCAPE ALREADY COVERS MOST OF IT, which is why this is narrow rather than a general back key.
    /// EscapeHandler handles 17 states and matches B on nearly all of them: closes options, the save
    /// menu, the chat log, backs out of the gallery, answers No on popups, leaves name entry and
    /// profile select. The phone is the ONE place the two genuinely diverge:
    ///
    ///     B      -> smartPhoneManager.BackButtonFunctionDelegate()   (back one level)
    ///     Escape -> optionsUIManager.PauseGame()                     (opens the pause menu)
    ///
    /// So a keyboard player inside a newspaper article cannot step back to the app list: Escape
    /// pauses the game instead, and while Tab closes the phone entirely, that is a different action.
    ///
    /// ⚠ WE DO NOT REBIND ESCAPE TO DO WHAT B DOES. On the phone screens Escape's pause is the only
    /// route to the pause menu, so trading it for a back step would remove a capability to add one.
    /// Both keys should work, doing their own thing - which is what this class arranges.
    ///
    /// WHY BACKSPACE. It is bound NOWHERE in the game: the only "Backspace" in the assembly is
    /// TG_NameKeys.Backspace(), an on-screen button's click handler, not a key binding. It reads
    /// naturally as "back", and its one real collision - the name entry InputField, which consumes
    /// Backspace for editing - is unreachable from here because this acts only on PHONE_* states.
    ///
    /// We call the game's OWN BackButtonFunctionDelegate rather than reimplementing the step. That
    /// delegate is reassigned by each screen as it opens (TG_SmartPhoneManager:364, :374, :386,
    /// :398, :410), so it always holds the correct action for wherever the player currently is -
    /// including "close the phone" on the home screen. Reimplementing the ladder here would be a
    /// second copy of the game's own navigation, free to drift from it.
    /// </summary>
    [HarmonyPatch]
    public static class PhoneBackKey
    {
        private const KeyCode BackKey = KeyCode.Backspace;

        /// <summary>
        /// Hosted on ControllerUpdateFunction for the same reason as MainMenuHotkeys: it runs
        /// unconditionally from Update() in BOTH input modes, whereas HandlerKeyboard runs only in
        /// keyboard mode. Read with Input.GetKeyDown rather than bound onto an action set, so the
        /// key cannot leak into the routers that dispatch on every screen.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ControllerInputManager), "ControllerUpdateFunction")]
        public static void AfterControllerUpdate(TG_ControllerInputManager __instance)
        {
            try
            {
                if (__instance == null) return;
                if (!Input.GetKeyDown(BackKey)) return;

                // PHONE_* only. This is what keeps Backspace away from the name entry field and
                // from every other screen where Escape is already the right key.
                object state = AccessTools.Field(__instance.GetType(), "currentState")
                    ?.GetValue(__instance);
                string s = state != null ? state.ToString() : null;
                if (s == null || !s.StartsWith("PHONE")) return;

                TG_SmartPhoneManager phone = UnityEngine.Object.FindObjectOfType<TG_SmartPhoneManager>();
                if (phone == null) return;

                // ⚠ The delegate can legitimately be null before the phone has opened a screen.
                // Invoking it blind would throw once per press with the phone half-initialised.
                if (phone.BackButtonFunctionDelegate == null)
                {
                    MelonLogger.Msg("[Phone] Backspace: no back action on " + s + ".");
                    return;
                }

                MelonLogger.Msg("[Phone] Backspace -> back (" + s + ")");
                phone.BackButtonFunctionDelegate();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Phone] back key threw: " + e.Message);
            }
        }
    }
}
