using System;
using System.Collections;
using System.Text;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeTalkAccess.FullGame
{
    /// <summary>
    /// Speaks the smartphone's drink recipes app - the in-game recipe book a player consults to
    /// work out what to brew.
    ///
    /// ⚠ UNTESTED (no run has reached the phone), but the DATA is fully present: the demo ships
    /// ~31 drinks in `drinkList/` including the retail-only ones. See PLAN.md phase 5.
    ///
    /// ⚠ THIS APP WAS WRONGLY GROUPED WITH THE BLOCKED ONES, AND THE DISTINCTION IS THE WHOLE
    /// REASON IT IS BUILDABLE. Social media and news are Scrollbar-driven (ScrollDetailProfile /
    /// ScrollNews) - analog pixel positions with no per-item focus, the chat-log shape that needs a
    /// mod-supplied entry cursor. The recipes app is NOT: SetNavigation() builds an explicit
    /// Navigation graph over every row's openDescButton (selectOnUp/selectOnDown, wrapping at both
    /// ends), so the EventSystem moves real focus between rows exactly as it does in the friend
    /// list. No mod cursor is needed or wanted here.
    ///
    /// SO THIS FILE IS A LABELLER, NOT A NARRATOR. The focused object is the BUTTON; the three text
    /// components (name, ingredients, description) live on the TG_DrinkItemUI ROW above it. That is
    /// the same arrangement as TG_FriendListPrefabUI, and it is handled the same way - FocusNarrator
    /// walks up the parent chain and asks us for the row's text. Announcing from here as well would
    /// talk over that.
    ///
    /// ⚠ LOCKED DRINKS ARE THE TRAP IN THIS SCREEN. InstantiateDrinkUI sets name, ingredients AND
    /// description to the SAME single string (lockedThingsText, "appsUI/undiscoveredDrinkRecipes")
    /// for every undiscovered drink. Read naively, a player arrowing down the list hears one
    /// identical sentence repeated ~20 times with nothing to say why, and no way to tell locked
    /// rows apart from a mod fault. We detect that string and say "locked recipe" once instead -
    /// the same principle as saying ", unlabeled" out loud rather than passing a bare object name
    /// off as a caption.
    ///
    /// ⚠ THE JOYSTICK GATE APPLIES HERE TOO. SelectFirstButton() and UpdateFunction's scroller are
    /// both inside `CurrentTypeControllerState == JOYSTICK` - the same keyboard gate that leaves
    /// popups with nothing selected. On keyboard the game may never select a first row, so the tab
    /// announcement below is driven from DisplayDrinks (which always runs) rather than from a focus
    /// watcher that might observe nothing.
    /// </summary>
    [HarmonyPatch]
    public static class DrinkRecipesPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        /// <summary>
        /// The "undiscovered" placeholder string for the app currently open, cached when the list is
        /// built so a row can be recognized as locked without re-resolving localization per row.
        ///
        /// Held as the resolved STRING rather than the localization key because that is what
        /// actually lands in the Text components, and comparing what we see against what the game
        /// wrote is what makes this robust to translation.
        /// </summary>
        private static string _lockedText;

        /// <summary>
        /// The last tab line spoken, so the doubled DisplayDrinks on app open is not heard twice.
        /// See AfterDisplayDrinks for why the duplicate exists and why this is keyed on the rendered
        /// line rather than on the category.
        /// </summary>
        private static string _lastTabLine;

        /// <summary>
        /// Announces the drink category when a tab is opened, with how many recipes are in it.
        ///
        /// DisplayDrinks is the right hook because it is the single point every route into a list
        /// converges on: the tab buttons' onClick, ButtonLeft/ButtonRight (shoulder buttons), and
        /// RefreshList when the app opens all call it. The count matters because the tabs filter by
        /// base ingredient - a sighted player sees a short list or a long one at a glance, and
        /// "coffee, 9 recipes" is that glance.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_DrinkRecipesApp), nameof(TG_DrinkRecipesApp.DisplayDrinks))]
        public static void AfterDisplayDrinks(TG_DrinkRecipesApp __instance, TG_Static.Ingredients ingredient)
        {
            try
            {
                if (__instance == null) return;

                // `lockedThingsText` is a public FIELD (verified against the shipped assembly), but
                // it is only assigned in InitLocalization, which runs from Init(). If a row is read
                // before that - or if the term ever fails to resolve - this stays empty and IsLocked
                // falls back to the English substring test. Never overwrite a good cached value with
                // an empty one: that would silently disable locked-row detection for the rest of the
                // session, and every locked row would then read out the placeholder sentence.
                string resolvedLocked = __instance.lockedThingsText;
                if (!string.IsNullOrEmpty(resolvedLocked)) _lockedText = resolvedLocked;

                int total = __instance.combinedDrinkItemList != null ? __instance.combinedDrinkItemList.Count : 0;
                int locked = __instance.lockedDrinkItem != null ? __instance.lockedDrinkItem.Count : 0;

                // `none` is the "all drinks" tab, not a missing value - GetTabButtonByType maps it
                // to a real tab button. Naming it "all" rather than speaking the enum keeps the
                // announcement in the player's vocabulary.
                //
                // ⚠ THE TAB IS NOT NAMED AFTER THE INGREDIENT IT FILTERS ON. The tabs are the five
                // BASES, and two of them are shown to the player under a different name than the
                // ingredient enum carries: `greentea` is the MATCHA tab (matchaIcon) and `chocolate`
                // is the COCOA tab (cocoaIcon) - the app's own COFFEE/TEA/MATCHA/COCOA/MILK
                // constants. Speaking the localized INGREDIENT name would announce "Green Tea" for a
                // tab whose icon and label say Matcha, sending the player looking for a tab that is
                // not there. Mirror the screen, not the enum.
                string category = ingredient == TG_Static.Ingredients.none
                    ? "All drinks"
                    : TabName(ingredient);

                StringBuilder sb = new StringBuilder(category);
                sb.Append(", ").Append(total).Append(total == 1 ? " recipe" : " recipes");

                // How many are still undiscovered is the progress information this screen carries;
                // without it the player cannot tell a short list from a mostly-locked one.
                if (locked > 0) sb.Append(", ").Append(locked).Append(" still locked");

                string line = sb.ToString();

                // ⚠ OPENING THE APP RUNS DisplayDrinks TWICE FOR THE SAME TAB. RefreshList calls it
                // directly, then invokes the tab button's onClick, whose listener calls it AGAIN
                // with the same category - so without this the player hears the identical sentence
                // twice on every open. Deduped on the rendered LINE rather than on the enum, so a
                // genuine re-entry that CHANGED the counts (a drink unlocked while the app was open,
                // repopulating the same tab) still speaks.
                //
                // This is the same shape as SayDialog's _lastSpoken dedup and is load-bearing for
                // the same reason. Cleared on Close so re-opening the app always announces the tab
                // the player has landed on - a dedup that outlives its screen turns a duplicate into
                // permanent SILENCE, which is the worse failure.
                if (line == _lastTabLine) return;
                _lastTabLine = line;

                Announce(line);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Recipes] tab hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Speaks a row's full description when the player expands it.
        ///
        /// The rows are COLLAPSIBLE: openDescButton toggles drinkDescriptionObject, so the
        /// description is genuinely hidden until opened, for sighted players too. The focus label
        /// below therefore gives the name and ingredients (what is visible in the collapsed row),
        /// and this gives the description (what expanding actually reveals) - mirroring the screen
        /// rather than dumping everything on every arrow press.
        ///
        /// OnButtonClick RETURNS false when it just CLOSED the row, so the return value is what
        /// distinguishes opening from closing. Announcing the description on a close would read out
        /// text the player just dismissed.
        ///
        /// ⚠ THE TEXT IS SAFE TO READ HERE, BUT THE VISIBILITY IS NOT. On the open path
        /// OnButtonClick only kicks off DoRefreshContentSizeFitter, a COROUTINE that waits two
        /// end-of-frames before drinkDescriptionObject is finally left active. So at postfix time
        /// the row is not yet visibly expanded. That does not matter for the description itself -
        /// InstantiateDrinkUI wrote `drinkDescription.text` when the list was BUILT, so the string
        /// is already there - but it does mean nothing here may test `drinkDescriptionObject
        /// .activeSelf` to decide what to say. (DescribeRecipeRow's ", expanded" suffix is read on a
        /// later frame from the focus watcher, so it sees the settled state and is unaffected.)
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_DrinkItemUI), nameof(TG_DrinkItemUI.OnButtonClick))]
        public static void AfterOnButtonClick(TG_DrinkItemUI __instance, bool __result)
        {
            try
            {
                if (__instance == null) return;

                if (!__result)
                {
                    Announce("Collapsed.");
                    return;
                }

                string description = TextOf(__instance.drinkDescription);

                if (IsLocked(description))
                {
                    // Expanding a locked row shows the placeholder. Say why there is nothing to
                    // read, instead of speaking the placeholder or falling silent.
                    Announce("Locked recipe. Brew it once to unlock the description.");
                    return;
                }

                Announce(string.IsNullOrEmpty(description) ? "No description." : description);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Recipes] expand hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Clears the tab dedup when the recipes app is dismissed, so re-opening it always announces
        /// the tab the player lands on.
        ///
        /// ⚠ TG_DrinkRecipesApp DOES NOT OVERRIDE Close() - it inherits it from TG_SmartPhoneApps,
        /// so patching `typeof(TG_DrinkRecipesApp), "Close"` would fail to resolve a method at patch
        /// time. The base is patched and the instance filtered here, the same arrangement as
        /// NewspaperAppPatches.AfterClose (which patches the same base method - Harmony composes
        /// multiple postfixes on one target, so the two coexist).
        ///
        /// Closing the PHONE outright never calls this, so the dedup can survive a session. That is
        /// harmless: the next DisplayDrinks that produces a DIFFERENT line still speaks, and an
        /// identical line for an identical tab is exactly what the dedup is for.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_SmartPhoneApps), nameof(TG_SmartPhoneApps.Close))]
        public static void AfterClose(TG_SmartPhoneApps __instance)
        {
            if (!(__instance is TG_DrinkRecipesApp)) return;
            _lastTabLine = null;
        }

        /// <summary>
        /// Names a recipe row for FocusNarrator: the drink and its three ingredients.
        ///
        /// Exposed the same way DescribeFriendEntry is, and for the same structural reason - the
        /// EventSystem focuses openDescButton while the text sits on the TG_DrinkItemUI row above
        /// it, so the generic component scan finds nothing and would say "unlabeled".
        ///
        /// The ingredients come from the row's own drinkIngredients Text, which the game filled via
        /// GenerateIngredientInDescription (base, primary, secondary, title-cased and localized).
        /// Reading the rendered Text rather than re-deriving it from the scriptable object means the
        /// mod inherits the game's fallback path for free: that method catches a missing
        /// localization key and writes the raw enum names instead, which is still far better than
        /// nothing and is exactly what the sighted player is looking at.
        /// </summary>
        internal static string DescribeRecipeRow(MonoBehaviour row)
        {
            try
            {
                if (row == null) return string.Empty;

                Type t = row.GetType();
                Text nameText = AccessTools.Field(t, "drinkName")?.GetValue(row) as Text;
                Text ingredientsText = AccessTools.Field(t, "drinkIngredients")?.GetValue(row) as Text;

                string drink = TextOf(nameText);

                // Locked rows put the placeholder in EVERY field, so this check has to come before
                // anything is spoken - otherwise the player hears the same sentence for each one.
                if (IsLocked(drink)) return "Locked recipe";

                if (string.IsNullOrEmpty(drink)) return string.Empty;

                StringBuilder sb = new StringBuilder(drink);

                string ingredients = TextOf(ingredientsText);
                if (!string.IsNullOrEmpty(ingredients) && !IsLocked(ingredients))
                    sb.Append(", ").Append(ingredients);

                // Whether the row is currently expanded is real state a sighted player can see (the
                // triangle rotates), and it tells the player whether Enter will open or close.
                GameObject descObj = AccessTools.Field(t, "drinkDescriptionObject")?.GetValue(row) as GameObject;
                if (descObj != null && descObj.activeSelf) sb.Append(", expanded");

                return sb.ToString();
            }
            catch (Exception)
            {
                // Degrade to "unlabeled" rather than throwing inside the focus watcher.
                return string.Empty;
            }
        }

        /// <summary>
        /// True when a string is the "undiscovered recipe" placeholder.
        ///
        /// Compared against the string the app itself resolved (cached in AfterDisplayDrinks) so
        /// this works in every language. The English fallback exists only for the case where a row
        /// is read before any DisplayDrinks call has been observed - and it is a fallback, not the
        /// primary test, because hardcoding a localized string would silently stop working the
        /// moment the player switches language.
        /// </summary>
        private static bool IsLocked(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            if (!string.IsNullOrEmpty(_lockedText) && text == _lockedText) return true;

            return text.IndexOf("undiscovered", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Reads and cleans a legacy UI.Text. All three row texts are UI.Text (not TMP)
        /// - confirmed by reflecting over the shipped assembly.</summary>
        private static string TextOf(Text text)
        {
            if (text == null || string.IsNullOrEmpty(text.text)) return string.Empty;
            return Dialogue.FungusText.ExtractWords(text.text);
        }

        /// <summary>
        /// Names a recipe TAB. The tabs are the five base ingredients, but two are presented under a
        /// different name than the enum: greentea is shown as Matcha, chocolate as Cocoa (the app's
        /// MATCHA_CONST / COCOA_CONST, and its matchaIcon / cocoaIcon).
        ///
        /// Uses the game's own localized ingredient name where the two agree, so the announcement
        /// stays in the player's language; the two renamed tabs are the deliberate exception, and an
        /// unrecognized value falls through to the ingredient name rather than to silence.
        /// </summary>
        private static string TabName(TG_Static.Ingredients ingredient)
        {
            switch (ingredient)
            {
                case TG_Static.Ingredients.greentea: return "Matcha";
                case TG_Static.Ingredients.chocolate: return "Cocoa";
                default: return Capitalize(Brewing.BrewingPatches.IngredientName(ingredient));
            }
        }

        /// <summary>Uppercases the first letter of a localized ingredient name for use at the
        /// start of an announcement.</summary>
        private static string Capitalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (char.IsUpper(text[0])) return text;
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private static void Announce(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            ISpeechOutput speech = Speech;
            if (speech == null || !speech.IsAvailable) return;

            MelonLogger.Msg("[Recipes] " + line);
            speech.SpeakAs(null, line, TextType.Menu, true);
        }
    }
}
