using System;
using System.Reflection;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;
using UnityEngine.UI;

namespace CoffeeTalkAccess.Menus
{
    /// <summary>
    /// Speaks the RETAIL language picker, which is a completely different screen from the demo's.
    ///
    /// ⚠ THE TWO BUILDS SHARE ALMOST NOTHING HERE - verified by reflecting over both assemblies:
    ///
    ///                              demo                      retail
    ///   shape                      grid of flag buttons      one label + prev/next arrows
    ///   per-language Selectable    TG_LanguageButton list    NONE
    ///   SelectAnyFlag()            EXISTS                    *** MISSING ***
    ///   RefreshLanguageUI()        *** MISSING ***           EXISTS
    ///   languageList / Count       *** MISSING ***           EXISTS
    ///
    /// So on retail the demo-era fix (InitScreenFix, which calls SelectAnyFlag) resolves to null and
    /// silently does nothing, and FocusNarrator has nothing to narrate because THERE IS NO FOCUSED
    /// LANGUAGE CONTROL - the screen is a spinner, not a list. Live proof, log 26-8-10_17-23-26:
    /// state reaches TWEENING, no [Focus] line ever appears, and the picker sat silent while the
    /// player pressed keys. **Reported as "it isn't reading the language picker", and it was neither
    /// a labelling bug nor a missing hook: the whole screen design changed.**
    ///
    /// NAVIGATION ALREADY WORKS and must not be re-implemented. Retail's
    /// TG_ControllerInputManager.LeftButtonPressed/RightButtonPressed invoke
    /// languageSettingGO.previousLanguageButton/nextLanguageButton for INIT_SCREEN, and
    /// AButtonPressed invokes selectLanguageButton. All three are dispatched from
    /// HandlerControllerPress(), which KeyboardNav already pumps every frame. So Left/Right/Enter
    /// reach this screen unaided; the ONLY thing missing was speech.
    ///
    /// ⚠ BOUND ENTIRELY BY STRING, and registered as an OPTIONAL hook. TG_InitLanguageSettingMenu
    /// exists in both builds but RefreshLanguageUI does not, so naming it in a [HarmonyPatch]
    /// attribute would make PatchAll throw on the demo. Main.ApplyPatches attaches this manually and
    /// tolerates absence; Main's expected-hook list carries it in the OPTIONAL table for the same
    /// reason - a hook that is legitimately missing on one build must not be reported as a failure
    /// on either.
    /// </summary>
    internal static class LanguagePickerPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        /// <summary>The last line spoken, so a repeated refresh does not repeat the announcement.</summary>
        private static string _lastSpoken;

        /// <summary>
        /// Attaches the postfix if this build has the method. Returns true when it attached.
        ///
        /// Done here rather than with an attribute because the target is retail-only: PatchAll would
        /// throw on the demo, taking every OTHER hook down with it.
        /// </summary>
        internal static bool TryAttach(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type t = AccessTools.TypeByName("TG_InitLanguageSettingMenu");
                if (t == null) return false;

                MethodInfo target = AccessTools.Method(t, "RefreshLanguageUI");
                if (target == null) return false;   // demo build - expected, not an error.

                MethodInfo postfix = typeof(LanguagePickerPatches)
                    .GetMethod(nameof(AfterRefreshLanguageUI), BindingFlags.NonPublic | BindingFlags.Static);
                if (postfix == null) return false;

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                return true;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Language] could not attach: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Announces the language now showing, with its position in the list.
        ///
        /// ⚠ Reads the INDEX and the LIST, not the on-screen label. RefreshLanguageUI only starts
        /// RefreshLanguageUICorutine, which sets the flag sprite and toggles the panel across a
        /// WaitForFixedUpdate - so at postfix time the visible text may still be the PREVIOUS
        /// language. `currentIndexSelected` and `languageList`, by contrast, are both updated by
        /// ChangeLanguage BEFORE it calls this, so they are correct the moment we run.
        ///
        /// The language NAME is the list entry itself ("English", "Brazil"), which is an English
        /// identifier rather than a localized string - deliberately so: the player is choosing a
        /// language they may not yet be able to read, and the endonym is what the flag represents.
        /// This is also the one screen where speaking the game's localized text would be actively
        /// unhelpful, since ChangeLanguage has just switched the whole UI into the candidate
        /// language - a player scanning for "English" would hear it announced in Portuguese.
        /// </summary>
        private static void AfterRefreshLanguageUI(object __instance)
        {
            try
            {
                if (__instance == null) return;

                Type t = __instance.GetType();

                object rawIdx = AccessTools.Field(t, "currentIndexSelected")?.GetValue(__instance);
                if (!(rawIdx is int)) return;
                int idx = (int)rawIdx;

                System.Collections.IList list =
                    AccessTools.Field(t, "languageList")?.GetValue(__instance) as System.Collections.IList;

                // Fall back to the game's global list: InitLanguageSelectUI copies TG_Static
                // .languageList into the private field, but a refresh could in principle run before
                // that assignment. Announcing a bare position with no name would be useless.
                if (list == null || list.Count == 0)
                {
                    list = AccessTools.Field(AccessTools.TypeByName("TG_Static"), "languageList")
                        ?.GetValue(null) as System.Collections.IList;
                }
                if (list == null || idx < 0 || idx >= list.Count) return;

                string name = Convert.ToString(list[idx]);
                if (string.IsNullOrEmpty(name)) return;

                string line = name + ", " + (idx + 1) + " of " + list.Count
                            + ". Left and right to change, Enter to choose.";

                // The screen refreshes more than once per change (the coroutine toggles the panel
                // twice), and InitLanguageSelectUI calls it again on entry.
                if (line == _lastSpoken) return;
                _lastSpoken = line;

                // interrupt:true - each move REPLACES the previous language, so the stale name must
                // not keep talking over the new one while the player arrows along the list.
                Speech?.SpeakAs(null, line, TextType.Menu, true);
                MelonLogger.Msg("[Language] " + line);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Language] hook threw: " + e.Message);
            }
        }

        /// <summary>Clears the dedup so re-entering the picker announces the current language again.</summary>
        internal static void Reset()
        {
            _lastSpoken = null;
        }
    }
}
