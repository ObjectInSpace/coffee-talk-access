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
