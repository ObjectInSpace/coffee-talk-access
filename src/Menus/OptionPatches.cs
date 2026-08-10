using System;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeTalkAccess.Menus
{
    /// <summary>
    /// Speaks the options and save/load menus.
    ///
    /// WHY A SECOND PATCH CLASS: the main menu and the options menu use DIFFERENT button types
    /// with DIFFERENT highlight methods, so MenuPatches (which hooks TG_MainMenuButton.MouseHover
    /// and TG_MainMenuManager.MouseOverManager) never sees the options screen at all. That is why
    /// options was completely silent while the main menu read fine.
    ///
    ///   main menu     -> List&lt;TG_MainMenuButton&gt; buttonList,       cursorIdx,     MouseHover()
    ///   options       -> List&lt;TG_OptionButtonContent&gt; optionButtonList, cursorMenuIdx, EventHoverMouse()
    ///
    /// We hook the BASE class TG_UIMenuContent.EventHoverMouse rather than the derived
    /// TG_OptionButtonContent. Harmony patches the base implementation, and since the overrides
    /// call base.EventHoverMouse() first, one patch covers every subclass:
    /// TG_OptionButtonContent (options, both main-menu and in-game) and TG_SaveLoadButtonContent
    /// (the save/load menu). Hooking the derived type would have needed a patch per subclass and
    /// would silently miss any we did not enumerate.
    ///
    /// This is the same "narrate the game's own highlight event" approach as MenuPatches - the
    /// game moves its own cursor (ButtonUp/ButtonDown drive cursorMenuIdx and call
    /// EventHoverMouse), and we only describe where it landed. No second cursor.
    /// </summary>
    [HarmonyPatch]
    public static class OptionPatches
    {
        private static string _lastSpoken;

        /// <summary>
        /// Re-announces the focused row after Left/Right adjusts it.
        ///
        /// OptionPlusMinusButtonHandler invokes the row's plus/minus button directly; it does NOT
        /// re-fire EventHoverMouse. So without this the player presses Right, the volume really
        /// changes, and NOTHING is spoken - leaving them to guess whether the key did anything.
        /// The dedup in the hover path is bypassed by clearing _lastSpoken, because the label is
        /// unchanged and only the value moved.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ControllerInputManager), "OptionPlusMinusButtonHandler")]
        public static void AfterOptionPlusMinus()
        {
            try
            {
                TG_UIMenuContent row = FindFocusedOptionRow();
                if (row == null) return;

                // The button's onClick has already run, but Unity's Slider may not have applied
                // the value until the end of frame; re-reading here is still correct because
                // OptionPlusMinusButtonHandler invokes onClick synchronously.
                _lastSpoken = null;
                AfterEventHoverMouse(row);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Option] plus/minus hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Resolves the option row the game's cursor is on, following the same three-way lookup
        /// OptionPlusMinusButtonHandler itself uses (main menu, in-game, endless mode), so we
        /// always describe the row the game just acted on.
        /// </summary>
        private static TG_UIMenuContent FindFocusedOptionRow()
        {
            object mainMenu = GetSingleton("TG_MainMenuManager");
            if (mainMenu != null)
            {
                object panel = AccessTools.Field(mainMenu.GetType(), "optionButtonPanel")?.GetValue(mainMenu);
                object idx = AccessTools.Field(mainMenu.GetType(), "cursorMenuIdx")?.GetValue(mainMenu);
                return RowFrom(panel, "buttonList", idx);
            }

            object game = GetSingleton("TG_GameManager");
            if (game != null)
            {
                object ui = AccessTools.Field(game.GetType(), "optionsUIManager")?.GetValue(game);
                if (ui == null) return null;
                object idx = AccessTools.Field(ui.GetType(), "cursorMenuIdx")?.GetValue(ui);
                return RowFrom(ui, "optionButtonList", idx);
            }
            return null;
        }

        private static TG_UIMenuContent RowFrom(object owner, string listField, object idxObj)
        {
            if (owner == null || idxObj == null) return null;

            System.Collections.IList list =
                AccessTools.Field(owner.GetType(), listField)?.GetValue(owner) as System.Collections.IList;
            if (list == null) return null;

            int idx = Convert.ToInt32(idxObj);
            if (idx < 0 || idx >= list.Count) return null;

            return list[idx] as TG_UIMenuContent;
        }

        private static object GetSingleton(string typeName)
        {
            Type t = AccessTools.TypeByName(typeName);
            if (t == null) return null;

            Type singleton = AccessTools.TypeByName("TG_GenericSingelton`1");
            if (singleton == null) return null;

            return AccessTools.Property(singleton.MakeGenericType(t), "Instance")?.GetValue(null);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_UIMenuContent), nameof(TG_UIMenuContent.EventHoverMouse))]
        public static void AfterEventHoverMouse(TG_UIMenuContent __instance)
        {
            try
            {
                if (__instance == null) return;

                ISpeechOutput speech = AccessMod.Speech;
                if (speech == null || !speech.IsAvailable) return;

                string label = ReadLabel(__instance);
                if (string.IsNullOrEmpty(label)) label = Prettify(__instance.gameObject.name);

                string spoken = label + DescribeValue(__instance);

                // arrayIdx is the entry's own position in its panel, maintained by the game's
                // SetMenuIndex, so it matches the list the player is actually moving through.
                int count = CountSiblings(__instance);
                if (count > 0 && __instance.arrayIdx >= 0 && __instance.arrayIdx < count)
                {
                    spoken += ", " + (__instance.arrayIdx + 1) + " of " + count;
                }

                if (spoken == _lastSpoken) return;
                _lastSpoken = spoken;

                speech.SpeakAs(null, spoken, TextType.Menu, true);
                MelonLogger.Msg("[Option] " + spoken);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Option] hover hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Describes the CONTROL, not just its name - a slider the player cannot see is useless
        /// without its value, and "BGM" alone does not say it is adjustable.
        ///
        /// The value is read live from the Slider each time rather than cached, so re-hovering
        /// after an adjustment reports the new figure. Percentage is computed against the
        /// slider's own min/max because Coffee Talk's audio sliders are not all 0-1.
        /// </summary>
        private static string DescribeValue(TG_UIMenuContent entry)
        {
            try
            {
                Slider slider = AccessTools.Field(entry.GetType(), "sliderValue")?.GetValue(entry) as Slider;
                if (slider != null && slider.gameObject.activeInHierarchy)
                {
                    float span = slider.maxValue - slider.minValue;
                    int pct = span > 0.0001f
                        ? Mathf.RoundToInt((slider.value - slider.minValue) / span * 100f)
                        : 0;
                    return ", slider, " + pct + " percent, left and right to adjust";
                }

                // Non-slider options (Fullscreen, Resolution, Language, ...) keep their current
                // setting in a Text field. Speaking the label without it would tell the player
                // which row they are on but not what it is set to.
                Text value = AccessTools.Field(entry.GetType(), "textValue")?.GetValue(entry) as Text;
                if (value != null && value.gameObject.activeInHierarchy && !string.IsNullOrEmpty(value.text))
                {
                    return ", " + value.text.Trim();
                }
            }
            catch (Exception)
            {
                // A subclass without these fields is expected (TG_SaveLoadButtonContent has
                // neither), not exceptional - fall through to the bare label.
            }
            return string.Empty;
        }

        /// <summary>
        /// Finds the row's caption. TG_UIMenuContent has no label field of its own, so we take the
        /// first Text in the hierarchy that is NOT the value readout - the value is spoken
        /// separately by DescribeValue and must not be mistaken for the name.
        /// </summary>
        private static string ReadLabel(TG_UIMenuContent entry)
        {
            Text value = null;
            try
            {
                value = AccessTools.Field(entry.GetType(), "textValue")?.GetValue(entry) as Text;
            }
            catch (Exception)
            {
                // No textValue on this subclass; every Text found is then a candidate label.
            }

            Text[] texts = entry.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                Text t = texts[i];
                if (t == null || ReferenceEquals(t, value)) continue;
                if (!t.gameObject.activeInHierarchy) continue;
                if (string.IsNullOrEmpty(t.text)) continue;
                return t.text.Trim();
            }
            return string.Empty;
        }

        /// <summary>Counts sibling menu rows, so "3 of 6" matches what the player moves through.</summary>
        private static int CountSiblings(TG_UIMenuContent entry)
        {
            try
            {
                Transform parent = entry.transform.parent;
                if (parent == null) return 0;

                int n = 0;
                for (int i = 0; i < parent.childCount; i++)
                {
                    GameObject child = parent.GetChild(i).gameObject;
                    if (!child.activeInHierarchy) continue;
                    if (child.GetComponent<TG_UIMenuContent>() != null) n++;
                }
                return n;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Turns an object name into something speakable when no caption exists - "BGMSlider"
        /// reads as "B G M Slider" rather than a single mangled word.
        /// </summary>
        private static string Prettify(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            System.Text.StringBuilder sb = new System.Text.StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
