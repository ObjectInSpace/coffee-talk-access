using System;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeTalkAccess.Brewing
{
    /// <summary>
    /// Makes latte art usable without drawing it.
    ///
    /// THE INVESTIGATION THAT CHANGED THE PLAN. Latte art was recorded as "the one real blocker" and
    /// scheduled as "announce + skip". Reading the scoring path shows a skip would have been the
    /// WRONG answer, and an unnecessary one:
    ///
    ///   TG_DrinkManager.ServeGlassDrink()         -> GiveBrewedDrink(arrIngredient, latteart: false)
    ///   TG_DrinkManager.ServeGlassDrinkLatteArt() -> GiveBrewedDrink(arrIngredient, latteart: true)
    ///
    /// Those two methods are otherwise IDENTICAL. The flag flows to SaveBrewedDrink ->
    /// TG_BrewSaveData.LatteArtMade, and every rule that consults it asks only
    /// `TG_SpecificDrinkRule.hasLatteArt != latteArt` (TG_DialogueManager:264) or
    /// `brewDataByBrewId.LatteArtMade != latteArt` (:275). BOTH ARE BOOLEANS. Nothing anywhere reads
    /// the fluid simulation, the drawn shape, or any quality score.
    ///
    /// So the artwork is cosmetic to the game's logic, while the ACT of making it is scored - drink
    /// requests can require latte art and will fail without it. That distinction is what makes this
    /// an honest substitute rather than a fake: the player performs the real, scored action and
    /// receives the real outcome. Only the picture, which the game never grades, is missing.
    ///
    /// THE PATH IS ENTIRELY NATIVE - THE MOD FORGES NOTHING. The latte art screen has its own
    /// `serveLatteArtButton`, and pressing it runs DoCloseLatteArtNServe, which calls
    /// ServeGlassDrinkLatteArt() regardless of what was (or was not) drawn. We do not fabricate a
    /// drawing and we do not call the serve ourselves: the player presses the screen's own button,
    /// through the same onClick the gamepad's A button invokes. All the mod supplies is the KEY,
    /// because the keyboard was never given one - see AfterControllerUpdate.
    ///
    /// ⚠ The latte art button only appears for drinks CONTAINING MILK
    /// (SetUpLatteArtButton: `ingredients.IndexOf(milk) > 0`), or for predefined drinks with
    /// `enableLatteArt`. Its absence is therefore normal, not a fault, and must not be reported as
    /// one.
    /// </summary>
    [HarmonyPatch]
    public static class LatteArtPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        /// <summary>
        /// Explains the latte art screen on entry, including how to leave it in either direction.
        ///
        /// The instruction is the whole point of this hook. A blind player arriving here has no way
        /// to discover that serving without drawing still counts - the screen presents itself as a
        /// drawing canvas, and the natural assumption is that leaving it means forfeiting the latte
        /// art. Saying so converts a screen that looks like a dead end into a working one.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(LatteArtManager), nameof(LatteArtManager.ActivateLatteArt))]
        public static void AfterActivateLatteArt()
        {
            Announce(
                "Latte art. Drawing needs a mouse and is not accessible, but it is not required: " +
                "press Enter to serve and the drink still counts as having latte art. " +
                "Backspace returns to the serve options without it.");
        }

        /// <summary>The live latte art UI manager, or null when the screen is not up.</summary>
        private static LatteArtUIManager ResolveUI()
        {
            LatteArtManager mgr = UnityEngine.Object.FindObjectOfType<LatteArtManager>();
            return mgr != null ? mgr.latteArtUIManager : null;
        }

        /// <summary>
        /// Gives the keyboard one key per action, mirroring the gamepad exactly.
        ///
        /// ⚠ THIS SCREEN HAS NO CURSOR, AND THAT IS THE WHOLE DESIGN. Latte art is the one screen in
        /// the game with NO EventSystem navigation whatsoever: grep LatteArtManager,
        /// LatteArtUIManager and ToogleLatteArtToolsManager for Select / SetSelectedGameObject /
        /// Selectable / EventSystem and the answer is ZERO hits - not even the JOYSTICK-gated kind
        /// this codebase fixes everywhere else. The gamepad does not MOVE a focus between these
        /// buttons; it gives each one its own button (ControllerInput:341-364):
        ///
        ///     A -> serve      B -> back        Y -> reset
        ///     D-Up -> pour milk   D-Left -> smudge   D-Down -> invert flow   (D-Right unused)
        ///
        /// The only thing the screen calls a cursor is `cursorLatteArt`, and that is the DRAWING
        /// crosshair - an Image that SetPositionCursor slides around inside the cup to aim the milk,
        /// explicitly hidden on a keyboard by SetActiveUIController(false). It is not a menu
        /// selector, and ToogleLatteArtToolsManager.SetActiveButton only swaps animator triggers, so
        /// the tool "highlight" is a picture rather than a focus.
        ///
        /// ⚠ SO THERE IS NO GAME CURSOR TO BORROW HERE, which is what separates this screen from
        /// every other one in this codebase. Elsewhere the game HAS a cursor and merely fails to
        /// seed it on a keyboard, so the fix is to supply the game's own missing trigger
        /// (BrewingPatches.AfterServeOptions, FocusRecovery). Here there is nothing gated and
        /// nothing missing: a selection would be a mod invention, and the arrow ORDER between six
        /// buttons would come from Unity's geometric Automatic mode rather than any graph the game
        /// authored. The player's rule stands - "we shouldn't ever add a cursor" - and the honest
        /// keyboard equivalent of a screen built from dedicated buttons is dedicated KEYS.
        ///
        /// KEY CHOICES. Enter and Backspace are this mod's established confirm/back pair (see
        /// PhoneBackKey). Up/Left/Down sit on the same DIRECTIONS the pad uses for the same three
        /// tools, so the two input methods describe the screen the same way. R is reset, under the
        /// same hand as the arrows.
        ///
        /// ⚠ R IS FREE HERE ONLY BECAUSE THE REQUEST HOTKEY MOVED OFF IT. R used to be
        /// RequestPatches' speak-the-request key, which would have put two meanings on one key on
        /// adjacent screens; the request query now answers to backquote instead (see Main's
        /// RequestKey). If anything ever wants R globally again, this is the second claim on it.
        ///
        /// ⚠ NOT ESCAPE FOR BACK, AND THIS WAS GOT WRONG ONCE. An earlier version bound Escape on
        /// the reasoning that EscapeHandler has no LATTE_ART branch, so the key was "free". That is
        /// the mistake PhoneBackKey's comment warns against one screen over: Escape is the game's
        /// PAUSE key, and a state having no explicit branch does not make pause the wrong meaning
        /// there - it makes it the meaning the player expects. The player's correction,
        /// 2026-08-17: "Escape is the wrong key for this, escape should pause. Backspace would be
        /// back."
        ///
        /// Hosted on ControllerUpdateFunction, which Update() calls unconditionally in BOTH input
        /// modes - not on HandlerKeyboard, which runs only in keyboard mode and is therefore hostage
        /// to the mode race documented in KeyboardNav.AfterControllerUpdate.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ControllerInputManager), "ControllerUpdateFunction")]
        public static void AfterControllerUpdate(TG_ControllerInputManager __instance)
        {
            try
            {
                if (__instance == null) return;

                // ⚠ LATTE_ART ONLY, read from the game's own live state rather than tracked here.
                // Enter and the arrows are the busiest keys in the game; acting on them outside this
                // one state would collide with every screen that already handles them. Checked
                // BEFORE reading any key so the common case costs one field read.
                object state = AccessTools.Field(__instance.GetType(), "currentState")
                    ?.GetValue(__instance);
                if (state == null || state.ToString() != "LATTE_ART") return;

                LatteArtUIManager ui = ResolveUI();
                if (ui == null) return;

                if (Pressed(KeyCode.Return) || Pressed(KeyCode.KeypadEnter))
                    Press(ui.serveLatteArtButton, "Enter -> serve");
                else if (Pressed(KeyCode.Backspace))
                    Press(ui.backButton, "Backspace -> back");
                else if (Pressed(KeyCode.UpArrow))
                    Press(ui.pourMilkButton, "Up -> pour milk");
                else if (Pressed(KeyCode.LeftArrow))
                    Press(ui.smudgeButton, "Left -> etch");
                else if (Pressed(KeyCode.DownArrow))
                    Press(ui.invertFlowButton, "Down -> invert flow");
                else if (Pressed(KeyCode.R))
                    Press(ui.resetButton, "R -> reset");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[LatteArt] key handler threw: " + e.Message);
            }
        }

        private static bool Pressed(KeyCode key)
        {
            return Input.GetKeyDown(key);
        }

        /// <summary>
        /// Invokes a latte art button the way the gamepad does, refusing politely when it is not
        /// available yet.
        ///
        /// ⚠ serveLatteArtButton and backButton are [HideInInspector] and assigned by
        /// SetButtonPositionForJoystick from the serveLatteArtButtons/backButtons ARRAYS, so both are
        /// legitimately null until the screen has initialised its UI - and the panel tweens in over
        /// 0.6 s, so a keypress during the animation is entirely reachable. Invoking blind would
        /// throw once per press on a half-open screen.
        /// </summary>
        private static void Press(Button button, string what)
        {
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
            {
                MelonLogger.Msg("[LatteArt] " + what + ": that button is not available yet.");
                return;
            }

            MelonLogger.Msg("[LatteArt] " + what);
            button.onClick.Invoke();
        }

        /// <summary>
        /// Confirms leaving latte art WITHOUT serving, so the player knows the drink is back on the
        /// serve-options screen rather than gone.
        ///
        /// Note the asymmetry with CloseLatteArtNServe: that path ends in ServeGlassDrinkLatteArt,
        /// which BrewingPatches already announces ("Served, with latte art."). Announcing here too
        /// would talk over it, so only the non-serving exit speaks.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(LatteArtManager), nameof(LatteArtManager.CloseLatteArt))]
        public static void AfterCloseLatteArt()
        {
            Announce("Left latte art without serving.");
        }

        private static void Announce(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            ISpeechOutput speech = Speech;
            if (speech == null || !speech.IsAvailable) return;

            MelonLogger.Msg("[LatteArt] " + line);
            speech.SpeakAs(null, line, TextType.Menu, true);
        }
    }
}
