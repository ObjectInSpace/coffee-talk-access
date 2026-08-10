using System;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;

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
    /// ServeGlassDrinkLatteArt() regardless of what was (or was not) drawn. We do not call the serve
    /// ourselves and we do not fabricate a drawing; we announce that the button is there and what it
    /// will do. The player presses it.
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
                "choose Serve here and the drink still counts as having latte art. " +
                "Back returns to the serve options without it.");
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
