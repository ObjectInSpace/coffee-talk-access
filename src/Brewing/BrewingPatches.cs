using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using CoffeeTalkAccess.Dialogue;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoffeeTalkAccess.Brewing
{
    /// <summary>
    /// Narrates the brewing loop: what went into the glass, how full it is, and what came out.
    ///
    /// THE MOD DOES NOT NAVIGATE HERE, AND MUST NOT. Unlike the menus, brewing is genuinely
    /// EventSystem-driven: TG_DrinkManager.SetIngredientsButton builds an explicit Navigation graph
    /// that SKIPS non-interactable buttons, and AddIngredient calls Select() itself. The game's
    /// keyboard navigation already works. The gap is purely that nothing is ever SPOKEN - so this
    /// class and FocusNarrator only describe, and never call SetSelectedGameObject.
    ///
    /// ⚠ THE AUTO-JUMP AT THREE INGREDIENTS IS A FEATURE TO ANNOUNCE, NOT A FIGHT TO PICK.
    /// AddIngredient ends with:
    ///     if (glass_value == 3) { brewingButton.OnSelect(null); brewingButton.Select(); }
    ///     else                  { lastSelected.OnSelect(null);  lastSelected.Select(); }
    /// The prior-art mod reported this as auto-focus "fighting you at ingredient 3". It only fights
    /// a mod that is also trying to own focus. We let it happen and SAY so, which turns a
    /// disorienting jump into the most useful announcement in the screen: the glass is full and the
    /// cursor is now on Brew.
    ///
    /// WHY AddIngredient's RETURN CODE MATTERS. It is an int, and the failure cases are silent on
    /// screen - the button simply does not respond:
    ///      1 = added        0 = glass already full (>3)
    ///     -1 = no such ingredient   -2 = not allowed here (base_allow/mix_allow rejected it)
    /// A sighted player sees the greyed-out icon and the unchanged glass. A blind player pressing a
    /// dead button learns nothing, which is the silent-stop failure mode. We speak the refusal AND
    /// its reason.
    /// </summary>
    [HarmonyPatch]
    public static class BrewingPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        private const int GlassCapacity = 3;

        /// <summary>
        /// Supplies the ONE selection the game never makes on a keyboard, and nothing else.
        ///
        /// THE WHOLE BUG, in one line: SelectCocoa's entire body is behind
        /// `CurrentTypeControllerState == JOYSTICK` (TG_DrinkManager:547). It is the only thing that
        /// seeds a cursor when the screen opens with an empty glass - SetIngredientsButton:384-387
        /// calls it on the `glass_value == 0`, `i == 0` branch. On a keyboard nothing is ever
        /// selected, so the explicit Navigation graph built directly below it (:393-524) is correct
        /// and UNREACHABLE: navigation moves relative to a current selection, and there is none.
        /// Arrow keys and Enter are both dead. Reported live 2026-08-11, confirmed with the pad both
        /// connected and unplugged.
        ///
        /// ⚠ WHY A POSTFIX HERE AND NOT A FocusRecovery WHITELIST ENTRY. An earlier fix
        /// added BREWING to FocusRecovery, which watches for ANY null selection. That opts into
        /// every null the screen produces - including the one ServeGlassDrink makes deliberately at
        /// :829 while the state is still BREWING - so it then needed a glass_value guard to avoid
        /// fighting the serve, plus a scoped finder to stop the scene-wide scan wandering onto the
        /// cafe underneath. Three pieces of machinery to rescue one moment.
        ///
        /// Hooking the entry point instead means we are present for exactly that moment and absent
        /// for every other: the serve null is never seen, so no guard is needed, and there is no
        /// scan to scope. The game keeps ownership of the cursor everywhere else - AddIngredient
        /// re-selects (:662-673), the auto-jump to Brew at three ingredients still fires, and
        /// CheckPluggedInState still reseeds on a mode flip.
        ///
        /// And we call the GAME's OWN SelectAnyInteractableIngredient (:569-582) rather than picking
        /// a button ourselves. It already prefers the first interactable ingredient and already
        /// falls back to Brew or Reset when none is - which matters because SetIngredientsButton
        /// disables every ingredient that cannot legally be a base, so index 0 is often unavailable.
        /// Reimplementing that was duplicating rules that can drift; calling it cannot.
        ///
        /// ⚠ HOOK SetIngredientsButton, NOT SelectCocoa. SelectCocoa looks like the natural target -
        /// it is the gated call - but it is reached from ONE branch of SetIngredientsButton's first
        /// loop (:384-387): `glass_value == 0` AND `i == 0` AND index 0 falling through to the
        /// `else`. An ingredient in `ingredientsDisableList` takes the OTHER branch (:367-370), so on
        /// a restricted-ingredient day SelectCocoa is never called at all.
        ///
        /// That is not an edge case - it is the FIRST BREW OF THE GAME. Drew says "I don't have half
        /// of my ingredients today" and the screen offers four (Milk, Coffee, Matcha, Cocoa),
        /// confirmed by an F10 dump inside the live screen: "Nothing focused. 21 controls on screen"
        /// with the four ingredient buttons wired in a left/right ring. A hook on SelectCocoa
        /// attached correctly (log 26-8-11_9-31-49: "Applied to: TG_DrinkManager.SelectCocoa") and
        /// NEVER FIRED. ⚠ "The gated call" is not the same thing as "the call that always runs" -
        /// verify the CALLER's branch conditions, not just the gate.
        ///
        /// SetIngredientsButton runs on every rebuild of the screen, on every path into it, whatever
        /// the day's ingredients are - so it is where "the screen is now ready and nothing is
        /// focused" can actually be observed.
        ///
        /// ⚠ BUT IT RUNS ON THREE PATHS, NOT ONE, AND ONLY ENTRY WANTS US. The glass_value check
        /// below is what distinguishes them - see its comment. A "the selection is null right now"
        /// test is NOT sufficient on its own: AddIngredient nulls it too, transiently, before
        /// re-selecting a line later. Shipping without the glass check made this hook re-pin the
        /// cursor to the first ingredient on every add, which read to the player as arrow keys that
        /// did nothing. ⚠ Check WHICH CALLER you are in, not just what the state looks like.
        ///
        /// Patching the base TG_DrinkManager covers TG_EndlessModeDrinkManager, which inherits it.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_DrinkManager), nameof(TG_DrinkManager.SetIngredientsButton))]
        public static void AfterSetIngredientsButton(TG_DrinkManager __instance)
        {
            try
            {
                // Only in the brewing screen. SetIngredientsButton also runs during setup and on the
                // way out, where taking focus would fight whatever owns the screen next.
                if (AccessMod.ReadControllerState() != "BREWING") return;

                // ⚠ ONLY ON ENTRY - AN EMPTY GLASS. AddIngredient also calls SetIngredientsButton
                // (:663), and it does so AFTER incrementing glass_value but BEFORE re-selecting at
                // :664-673. So at postfix time the selection is momentarily null on that path too,
                // and without this check we re-pin the cursor to the FIRST interactable ingredient
                // on every single add.
                //
                // That is not theoretical: log 26-8-11 20:13:47-53 shows "supplied the keyboard's
                // missing entry selection" firing before all three adds, the player arrowing and
                // hearing nothing move, and three Coffees going into a glass they were steering
                // elsewhere. Reported as "the arrow keys don't move the focus".
                //
                // The game re-selects perfectly well by itself on that path - lastSelected, or the
                // Brew button once the glass is full - so there is nothing for us to do there. An
                // empty glass is the one moment no other path covers.
                if (GlassValue(__instance) != 0) return;

                // The game seeded the cursor itself - JOYSTICK mode, or one of its ungated paths
                // (SelectAnyInteractableIngredient from a mode flip, the resume-from-phone path).
                // Leave it alone; a second Select() here is the two-disagreeing-cursors failure
                // this project has already paid for.
                if (EventSystem.current != null &&
                    EventSystem.current.currentSelectedGameObject != null) return;

                // ⚠ The GAME's own chooser, not ours. It prefers the first INTERACTABLE ingredient
                // and falls back to Brew or Reset when none is (:569-582) - which is exactly what a
                // restricted day needs, and exactly what picking ingredientsButtons[0] ourselves
                // would get wrong.
                __instance.SelectAnyInteractableIngredient();

                MelonLogger.Msg("[Brew] supplied the keyboard's missing entry selection.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Brew] entry selection threw: " + e.Message);
            }
        }

        /// <summary>
        /// Announces the outcome of adding an ingredient - including the refusals, which the game
        /// communicates only by not responding.
        ///
        /// Postfix with __result so we report what actually happened rather than what was attempted.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_DrinkManager), nameof(TG_DrinkManager.AddIngredient))]
        public static void AfterAddIngredient(TG_DrinkManager __instance, TG_Static.Ingredients value, int __result)
        {
            try
            {
                string name = IngredientName(value);

                switch (__result)
                {
                    case 1:
                        AnnounceAdded(__instance, name);
                        return;

                    case 0:
                        // The glass is already full. The game just ignores the press.
                        Announce("Glass is full. Brew it, or reset to start over.");
                        return;

                    case -2:
                        // base_allow / mix_allow rejected it. Which rule applied depends on whether
                        // the glass is empty, and the distinction is genuinely useful: it tells the
                        // player whether to pick a different FIRST ingredient or a different MIXER.
                        Announce(GlassValue(__instance) == 0
                            ? name + " cannot be a base. Choose a base ingredient first."
                            : name + " cannot be mixed into this drink.");
                        return;

                    case -1:
                        Announce(name + " is unavailable.");
                        return;
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Brew] add hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Speaks the added ingredient, the resulting glass, and where the cursor has gone.
        ///
        /// The glass contents are read back in full every time rather than only announcing the
        /// delta. A sighted player has the three slots permanently on screen; without a re-read the
        /// blind player has to hold the recipe in memory across the whole brew, and Coffee Talk's
        /// puzzles turn on exactly that combination.
        /// </summary>
        private static void AnnounceAdded(TG_DrinkManager mgr, string name)
        {
            int count = GlassValue(mgr);
            StringBuilder sb = new StringBuilder();

            sb.Append("Added ").Append(name).Append(". ");
            sb.Append("Glass: ").Append(DescribeGlass(mgr)).Append(". ");
            sb.Append(count).Append(" of ").Append(GlassCapacity).Append('.');

            // Mirror the game's own focus jump - see the class comment. Said explicitly because the
            // cursor has moved somewhere the player did not put it.
            if (count >= GlassCapacity)
                sb.Append(" Glass full, cursor on Brew.");

            Announce(sb.ToString());
        }

        /// <summary>
        /// Lists what is currently in the glass, in the order it went in. Reads brewList (the
        /// scriptable ingredients actually added) rather than arrIngredient, because arrIngredient
        /// is a fixed-size 3-slot array padded with `none`.
        /// </summary>
        private static string DescribeGlass(TG_DrinkManager mgr)
        {
            try
            {
                IList list = AccessTools.Field(typeof(TG_DrinkManager), "brewList")?.GetValue(mgr) as IList;
                if (list == null || list.Count == 0) return "empty";

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < list.Count; i++)
                {
                    string id = IngredientIdOf(list[i]);
                    if (string.IsNullOrEmpty(id)) continue;

                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(LocalizedIngredient(id));
                }
                return sb.Length == 0 ? "empty" : sb.ToString();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Brew] glass read threw: " + e.Message);
                return "unknown";
            }
        }

        /// <summary>
        /// Announces the finished drink once brewing resolves.
        ///
        /// GetDrinkNameAndColor resolves the name into the manager's `drinkName` field, so we
        /// postfix it and read what it just computed rather than re-deriving the name-formula logic
        /// (which is ~80 lines of [BASE]/[PRIMARY]/[SECONDARY] template substitution in the game).
        ///
        /// Reading the FIELD rather than drinkNameText: the field is the resolved value, while the
        /// on-screen Text is written on a separate animated path (AnimateText) that may not have
        /// run yet at postfix time. It is `protected virtual`, hence the patch by string name.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_DrinkManager), "GetDrinkNameAndColor")]
        public static void AfterGetDrinkNameAndColor(TG_DrinkManager __instance)
        {
            try
            {
                string drink = AccessTools.Field(typeof(TG_DrinkManager), "drinkName")
                    ?.GetValue(__instance) as string;

                // Fall back to the visible label if the field is empty - between the two, one of
                // them has the name in every path the game takes.
                if (string.IsNullOrEmpty(drink))
                {
                    Text label = AccessTools.Field(typeof(TG_DrinkManager), "drinkNameText")
                        ?.GetValue(__instance) as Text;
                    drink = label == null ? null : label.text;
                }

                drink = FungusText.ExtractWords(drink ?? string.Empty);

                if (string.IsNullOrEmpty(drink))
                {
                    // The drink resolved to nothing nameable. Say so - a silent brew leaves the
                    // player unsure whether anything happened at all.
                    Announce("Brewed an unnamed drink.");
                    return;
                }

                Announce("Brewed: " + drink + ".");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Brew] drink name hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Confirms what was served, and whether it counted as having latte art.
        ///
        /// The latte-art flag is stated because it is SCORED: TG_SpecificDrinkRule compares
        /// `hasLatteArt` against it, so a request can require latte art and fail without it. The
        /// player needs to know which of the two serves they just performed. See
        /// LatteArtPatches for why serving WITH art is available to a blind player at all.
        ///
        /// Patching the base TG_DrinkManager covers TG_EndlessModeDrinkManager too, since it
        /// overrides these virtuals and calls into the same GiveBrewedDrink contract.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_DrinkManager), nameof(TG_DrinkManager.ServeGlassDrink))]
        public static void AfterServeGlassDrink()
        {
            Announce("Served, without latte art.");
        }

        /// <summary>Serve confirmation for the latte-art path.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_DrinkManager), nameof(TG_DrinkManager.ServeGlassDrinkLatteArt))]
        public static void AfterServeGlassDrinkLatteArt()
        {
            Announce("Served, with latte art.");
        }

        /// <summary>
        /// Announces a reset, which otherwise empties the glass silently.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_DrinkManager), nameof(TG_DrinkManager.ResetIngredients))]
        public static void AfterResetIngredients()
        {
            Announce("Glass emptied.");
        }

        /// <summary>
        /// Resolves a localized ingredient name from the enum value.
        ///
        /// TG_DrinkManager.ingredientsLocList is a PUBLIC STATIC dictionary keyed by ingredientID
        /// and filled from the localizer at Init, so it gives correctly translated names for free.
        /// The enum member name is the fallback, and it happens to match the ID convention
        /// (greentea, coffee, ...), so even the fallback is intelligible rather than a number.
        /// </summary>
        internal static string IngredientName(TG_Static.Ingredients value)
        {
            string id = value.ToString();
            return LocalizedIngredient(id);
        }

        /// <summary>Looks an ingredientID up in the game's localized name table.</summary>
        private static string LocalizedIngredient(string id)
        {
            if (string.IsNullOrEmpty(id)) return "unknown";

            try
            {
                IDictionary loc = AccessTools.Field(typeof(TG_DrinkManager), "ingredientsLocList")
                    ?.GetValue(null) as IDictionary;

                if (loc != null && loc.Contains(id))
                {
                    string name = loc[id] as string;
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch (Exception)
            {
                // Fall through to the raw id.
            }

            return id;
        }

        /// <summary>Reads the ingredientID off a TG_ScriptableIngredient.</summary>
        private static string IngredientIdOf(object scriptableIngredient)
        {
            if (scriptableIngredient == null) return null;
            try
            {
                return AccessTools.Field(scriptableIngredient.GetType(), "ingredientID")
                    ?.GetValue(scriptableIngredient) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Reads the private glass_value (how many ingredients are in the glass).</summary>
        private static int GlassValue(TG_DrinkManager mgr)
        {
            try
            {
                object v = AccessTools.Field(typeof(TG_DrinkManager), "glass_value")?.GetValue(mgr);
                return v is int ? (int)v : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Speaks a brewing line. TextType.Menu because this is interface feedback rather than
        /// story text - it must not take a "Speaker: " prefix - and interrupt:true because each
        /// announcement supersedes the last as the player works.
        /// </summary>
        private static void Announce(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            ISpeechOutput speech = Speech;
            if (speech == null || !speech.IsAvailable) return;

            MelonLogger.Msg("[Brew] " + line);
            speech.SpeakAs(null, line, TextType.Menu, true);
        }
    }
}
