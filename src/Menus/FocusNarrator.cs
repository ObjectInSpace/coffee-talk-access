using System;
using System.Text;
using CoffeeTalkAccess.Dialogue;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using TMPro;
using UnityAccessibilityLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoffeeTalkAccess.Menus
{
    /// <summary>
    /// Speaks whatever UI control currently has focus - main menu, options, save/load, brewing
    /// ingredients, dialogue choices.
    ///
    /// WHY A WATCHER AND NOT PER-SCREEN PATCHES: Coffee Talk's UI is built from ordinary Unity
    /// Selectables driven by the EventSystem (TG_MainMenuButton implements ISelectHandler;
    /// TG_DrinkManager wires explicit Navigation and calls Select() itself). The game therefore
    /// ALREADY moves focus correctly with the keyboard - it just never says anything. Watching
    /// EventSystem.currentSelectedGameObject catches every one of those screens at once, instead
    /// of a patch per menu class.
    ///
    /// This is a READ-ONLY observer: it never calls SetSelectedGameObject. The prior-art mod found
    /// that driving selection fights the game during brewing (TG_DrinkManager calls
    /// brewingButton.Select() itself once the glass has 3 ingredients). We let the game own focus
    /// and simply report it - so that auto-jump becomes a useful announcement rather than a fight.
    /// </summary>
    public sealed class FocusNarrator
    {
        private GameObject _lastFocused;
        private string _lastSpoken;

        /// <summary>
        /// The game state the dedup above was recorded under. When the game's own state machine
        /// moves, the dedup is void - see the comment in Update().
        /// </summary>
        private string _lastState;
        private float _lastSliderValue = float.NaN;

        /// <summary>
        /// Optional "3 of 7" to attach to the next focus change, when whoever moved focus knew the
        /// list position. Consumed (and cleared) by the next Update, because it is only meaningful
        /// for the focus change it accompanies - focus the game moves itself has no position.
        ///
        /// Currently always null: the cursor classes that set it have been retired. The screens
        /// FocusNarrator still covers (the EventSystem-driven language picker) are ones where the
        /// game moves focus itself, so there is no position to report. Kept because the consuming
        /// logic below is correct and costs nothing.
        /// </summary>
        internal static string PendingPosition;

        public void Update()
        {
            try
            {
                EventSystem es = EventSystem.current;
                if (es == null) return;

                // ⚠ CLEAR THE DEDUP ON A SCREEN CHANGE - and ONLY on a screen change.
                //
                // `_lastSpoken` is what keeps a control from being re-announced when focus is
                // briefly lost and restored to the SAME control, which the game does constantly:
                // every switch to keyboard mode runs CheckRemovedState, whose default branch nulls
                // the EventSystem selection. That suppression is the point, and the null branch
                // below deliberately does NOT clear `_lastSpoken` for exactly that reason.
                //
                // The risk is the mirror image: a label left over from a PREVIOUS screen could
                // suppress a genuine first announcement on a new one, leaving it silent - the worst
                // failure mode there is. So the dedup is invalidated when the game's own state
                // machine moves, which is the one event that means "different screen, all bets off".
                //
                // ⚠ Polarity matters: CLEAR here, never set. Getting this backwards produces either
                // a stutter (clearing too often) or a silent screen (clearing too rarely).
                string state = AccessMod.ReadControllerState();
                if (state != _lastState)
                {
                    _lastState = state;
                    _lastSpoken = null;
                    _lastFocused = null;
                }

                GameObject current = es.currentSelectedGameObject;
                if (current == null)
                {
                    // ⚠ `_lastSpoken` is deliberately NOT cleared here. A null selection is usually
                    // the game mid-transition (or a mode flip), and the control that comes back is
                    // usually the same one. Clearing would turn every such blip into a repeat
                    // announcement - the "it re-announces itself" symptom reported live.
                    _lastFocused = null;
                    return;
                }

                // A slider's VALUE changes while focus stays put, so re-describe it when it moves.
                // Without this the player adjusts volume and hears nothing back.
                bool sameObject = ReferenceEquals(current, _lastFocused);
                if (sameObject && !SliderValueChanged(current)) return;
                _lastFocused = current;

                // Stand down where a dedicated narrator owns the screen.
                //
                // The name-entry screen announced itself correctly and was then TALKED OVER 48ms
                // later by this watcher's generic fallback ("Input Field, unlabeled"), which
                // interrupts. The player heard only the useless half. A screen with a real
                // narrator does not want a second voice describing the same control - this is the
                // same "never let two cursors disagree" rule that retired MenuCursor, applied to
                // announcements rather than to focus.
                if (HasDedicatedNarrator(current)) return;

                string label = Describe(current);

                string position = PendingPosition;
                PendingPosition = null;
                if (!string.IsNullOrEmpty(position)) label = label + ", " + position;

                // Brewing ingredients carry a stat PREVIEW, parked here by StatsPatches when the
                // game computed it during OnSelect earlier this frame. It is appended to the label
                // rather than spoken on its own because this announcement interrupts: a separate
                // preview line would be cut off mid-word by the ingredient's own name. One control,
                // one utterance - "Honey, adds 2 sweetness".
                //
                // Consumed unconditionally (read-and-clear even when it is not appended) so a
                // preview can never outlive the focus move it describes and reattach to the next
                // control the player lands on.
                string stats = Brewing.StatsPatches.PendingStats;
                Brewing.StatsPatches.PendingStats = null;
                if (!string.IsNullOrEmpty(stats)) label = label + ", " + stats;

                if (string.IsNullOrEmpty(label)) return;
                // Dedup guards against repeat announcements of the SAME state; a slider that
                // moved is genuinely new information even when focus did not change, and its
                // label already differs by the percentage.
                if (label == _lastSpoken) return;
                _lastSpoken = label;

                ISpeechOutput speech = AccessMod.Speech;
                if (speech == null || !speech.IsAvailable) return;

                // interrupt:true - focus moves are the player's own input; the newest one always
                // supersedes whatever was still being read.
                speech.SpeakAs(null, label, TextType.Menu, true);
                MelonLogger.Msg("[Focus] " + label);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Focus] threw: " + e.Message);
            }
        }

        /// <summary>
        /// True when another part of the mod narrates this control in full, so the generic focus
        /// announcement would only talk over it.
        ///
        /// Detected by COMPONENT, not by object name: `InputField` is the actual thing
        /// NameEntryPatches drives, whereas the name "Input Field" is incidental scene naming that
        /// a localization pass or the retail build could change. Any InputField anywhere is
        /// text-entry, and text-entry is inherently better served by an edit-aware narrator than
        /// by a focus watcher that cannot see typing at all.
        /// </summary>
        private static bool HasDedicatedNarrator(GameObject go)
        {
            return go.GetComponent<InputField>() != null;
        }

        /// <summary>
        /// True if the focused object is a slider whose value moved since the last announcement.
        /// </summary>
        private bool SliderValueChanged(GameObject go)
        {
            Slider s = go.GetComponent<Slider>();
            if (s == null) return false;

            if (Mathf.Approximately(s.value, _lastSliderValue)) return false;
            _lastSliderValue = s.value;
            return true;
        }

        /// <summary>
        /// Builds the spoken label for a focused control: its text, plus state worth knowing
        /// (disabled, toggle on/off, slider value).
        /// </summary>
        private static string Describe(GameObject go)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(GetLabel(go));

            Selectable sel = go.GetComponent<Selectable>();
            if (sel != null && !sel.interactable) sb.Append(", unavailable");

            Toggle toggle = go.GetComponent<Toggle>();
            if (toggle != null) sb.Append(toggle.isOn ? ", checked" : ", unchecked");

            Slider slider = go.GetComponent<Slider>();
            if (slider != null)
            {
                // Report sliders as a PERCENTAGE of their own range and say they are adjustable.
                // The raw value alone was useless live: the audio sliders announced a bare "1" or
                // "0" with no indication of scale, of which direction changes them, or even that
                // they were sliders rather than buttons.
                float span = slider.maxValue - slider.minValue;
                int pct = span > 0f ? Mathf.RoundToInt((slider.value - slider.minValue) / span * 100f) : 0;
                sb.Append(", slider, " + pct + " percent, left and right to adjust");
            }

            // Flag controls that quit or discard progress. In the 18:37 live run the cursor landed
            // on ButtonExit and then on a "NAO" confirm prompt - one Enter away from quitting the
            // game, with nothing spoken to say so. A sighted player sees a scary red button; this
            // is the equivalent warning.
            if (IsDestructive(go.name)) sb.Append(", warning, this quits or erases");

            string result = sb.ToString().Trim();
            // A control with no readable text at all is worse than useless to announce, but the
            // object name is a last resort that at least tells the player something moved.
            //
            // Say "unlabeled" out loud rather than passing the bare object name off as a caption.
            // The name-entry screen announced itself as the single word "Input" - which sounds like
            // a real (if terse) label, so it read as the game being obscure rather than as the mod
            // having nothing to say. Naming the gap makes a missing hook diagnosable by ear.
            if (result.Length == 0 || result.StartsWith(","))
                result = Prettify(go.name) + ", unlabeled" + result;

            return result.Trim(' ', ',');
        }

        /// <summary>
        /// Finds the human-readable text for a control. Coffee Talk mixes legacy UI.Text and
        /// TextMeshProUGUI (TG_MainMenuButton uses UI.Text; dialogue uses TMP), so check both -
        /// on the object itself first, then in children (the usual Button/Label arrangement).
        /// </summary>
        private static string GetLabel(GameObject go)
        {
            // FIRST: the game's own label reference. TG_MainMenuButton keeps its caption in
            // `textComponentToMove`, which is NOT necessarily a child of the Button object - the
            // live run announced "Button Exit" (a Prettify of the object name), proving the
            // component scan below missed it. The menu captions are also I2.Loc-localized onto
            // that same Text, so reading it gets the correctly translated string for free.
            string viaGame = GetGameLabel(go);
            if (!string.IsNullOrEmpty(viaGame)) return viaGame;

            Text t = go.GetComponent<Text>();
            if (t != null && !string.IsNullOrEmpty(t.text)) return Clean(t.text);

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            if (tmp != null && !string.IsNullOrEmpty(tmp.text)) return Clean(tmp.text);

            Text childText = go.GetComponentInChildren<Text>();
            if (childText != null && !string.IsNullOrEmpty(childText.text)) return Clean(childText.text);

            TextMeshProUGUI childTmp = go.GetComponentInChildren<TextMeshProUGUI>();
            if (childTmp != null && !string.IsNullOrEmpty(childTmp.text)) return Clean(childTmp.text);

            return string.Empty;
        }

        /// <summary>
        /// Resolves a language flag button to its language name via its `langIdx` into the static
        /// `TG_Static.languageList`. Reflection keeps the mod from hard-binding to the game's
        /// static layout - if either member moves, we degrade to the generic scan rather than throw.
        /// </summary>
        private static string GetLanguageName(MonoBehaviour languageButton)
        {
            try
            {
                System.Reflection.FieldInfo idxField = languageButton.GetType().GetField("langIdx");
                if (idxField == null) return string.Empty;
                int idx = (int)idxField.GetValue(languageButton);

                Type tgStatic = AccessTools.TypeByName("TG_Static");
                if (tgStatic == null) return string.Empty;

                System.Reflection.FieldInfo listField = tgStatic.GetField("languageList",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (listField == null) return string.Empty;

                System.Collections.IList list = listField.GetValue(null) as System.Collections.IList;
                if (list == null || idx < 0 || idx >= list.Count) return string.Empty;

                return Convert.ToString(list[idx]);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Names a smartphone friend-list row, searching the focused object and its parents.
        ///
        /// Looked up by TYPE NAME through the parent chain rather than by a direct component
        /// reference, so this file keeps no compile-time dependency on the phone types and a rename
        /// in the retail build degrades to "unlabeled" (an audible gap) instead of throwing inside
        /// the focus watcher.
        /// </summary>
        private static string GetFriendListLabel(GameObject go)
        {
            try
            {
                Type t = AccessTools.TypeByName("TG_FriendListPrefabUI");
                if (t == null) return string.Empty;

                Component row = go.GetComponentInParent(t);
                return FullGame.SmartPhonePatches.DescribeFriendEntry(row as MonoBehaviour);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Names a focused achievement icon, searching the focused object and its parents.
        ///
        /// The phrase itself was composed by AchievementPatches when the game filled the detail
        /// panels during OnSelect earlier this frame; this only decides that the focused object IS
        /// an achievement icon, and takes the parked label so the two never speak over each other.
        ///
        /// ⚠ The take is deliberately gated on FINDING the component: consuming it unconditionally
        /// (as the stat preview is) would throw the label away on unrelated focus moves, and unlike
        /// a stat preview there is nothing else for the icon to fall back on.
        /// </summary>
        private static string GetAchievementLabel(GameObject go)
        {
            try
            {
                Type t = AccessTools.TypeByName("TG_AchievementIconUI");
                if (t == null) return string.Empty;

                if (go.GetComponentInParent(t) == null) return string.Empty;

                return FullGame.AchievementPatches.TakePendingLabel();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Names a focused calendar day cell, searching the focused object and its parents.
        ///
        /// Identical in shape to GetAchievementLabel, and here for the identical reason:
        /// TG_CalendarLoadUI extends TG_Button and inherits its `button` field, so the EventSystem
        /// may focus a child while the component sits on the row. CalendarPatches parked the day's
        /// description during OnSelect; this only decides that the focused object IS a day cell.
        /// </summary>
        private static string GetCalendarDayLabel(GameObject go)
        {
            try
            {
                Type t = AccessTools.TypeByName("TG_CalendarLoadUI");
                if (t == null) return string.Empty;

                if (go.GetComponentInParent(t) == null) return string.Empty;

                return FullGame.CalendarPatches.TakePendingLabel();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Names a drink-recipe row, searching the focused object and its parents.
        ///
        /// Looked up by TYPE NAME through the parent chain, the same way friend rows are, so this
        /// file keeps no compile-time dependency on the phone types and a rename in the retail
        /// build degrades to "unlabeled" - an audible gap - rather than throwing inside the focus
        /// watcher.
        /// </summary>
        private static string GetRecipeRowLabel(GameObject go)
        {
            try
            {
                Type t = AccessTools.TypeByName("TG_DrinkItemUI");
                if (t == null) return string.Empty;

                Component row = go.GetComponentInParent(t);
                return FullGame.DrinkRecipesPatches.DescribeRecipeRow(row as MonoBehaviour);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Names a focused profile slot, searching the focused object and its parents.
        ///
        /// Same arrangement as every other row screen: TG_ProfileSlotFlipUI extends TG_Button and
        /// the navigation graph targets its child `button` (TG_ProfileUIManager.SetNavigation
        /// assigns selectOnLeft/selectOnRight to `profileSlotUIList[i].button`), while the name and
        /// information Texts are private fields on the row above it. The object-local component scan
        /// would find neither, so the slot would announce "unlabeled" - on the FIRST screen of the
        /// retail game.
        /// </summary>
        private static string GetProfileSlotLabel(GameObject go)
        {
            try
            {
                Type t = AccessTools.TypeByName("TG_ProfileSlotFlipUI");
                if (t == null) return string.Empty;

                Component row = go.GetComponentInParent(t);
                return FullGame.ProfileSelectPatches.DescribeSlot(row as MonoBehaviour);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Appends what a phone app icon actually DOES to its brand name.
        ///
        /// The phone's four icons carry in-fiction product names - "Tomodachill", "Shuffld",
        /// "Brewpad", "The Evening Whisperss" - and nothing else. A sighted player sees an icon and
        /// learns the brand in seconds; by ear those names are undiscoverable, and the player asked
        /// outright "how do I get to social media?" after having opened it twice without knowing.
        ///
        /// So the brand is KEPT and the function is appended: "Tomodachill, social media". Keeping
        /// the brand matters - it is what the game calls it, it is what a guide or a friend will
        /// say, and replacing it would leave the player unable to match what they hear against any
        /// outside reference. Same reasoning as the recipes tabs, which speak the app's own
        /// "Matcha" rather than the enum's "Green Tea".
        ///
        /// Identified by comparing against TG_SmartPhoneManager's OWN serialized button references
        /// rather than by object name or by icon, so a localized or renamed prefab still resolves.
        /// </summary>
        private static string GetPhoneAppLabel(GameObject go)
        {
            try
            {
                Type mgrType = AccessTools.TypeByName("TG_SmartPhoneManager");
                if (mgrType == null) return string.Empty;

                UnityEngine.Object mgr = UnityEngine.Object.FindObjectOfType(mgrType);
                if (mgr == null) return string.Empty;

                string function = null;
                if (IsAppButton(mgrType, mgr, "socialMediaAppButton", go)) function = "social media";
                else if (IsAppButton(mgrType, mgr, "musicAppButton", go)) function = "music";
                else if (IsAppButton(mgrType, mgr, "recipesDrinkAppButton", go)) function = "drink recipes";
                else if (IsAppButton(mgrType, mgr, "newsPaperAppButton", go)) function = "newspaper";

                if (function == null) return string.Empty;

                // The brand name lives on a child Text (mixed types on this screen, as elsewhere in
                // this game). If it cannot be read we still say what the app IS, which is the
                // useful half - never fall back to the raw object name here.
                string brand = null;
                Text child = go.GetComponentInChildren<Text>();
                if (child != null && !string.IsNullOrEmpty(child.text)) brand = Clean(child.text);

                if (string.IsNullOrEmpty(brand))
                {
                    TextMeshProUGUI childTmp = go.GetComponentInChildren<TextMeshProUGUI>();
                    if (childTmp != null && !string.IsNullOrEmpty(childTmp.text)) brand = Clean(childTmp.text);
                }

                return string.IsNullOrEmpty(brand) ? Capitalize(function) : brand + ", " + function;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>True when the focused object IS the named app button on the phone manager.</summary>
        private static bool IsAppButton(Type mgrType, UnityEngine.Object mgr, string field, GameObject go)
        {
            Button b = AccessTools.Field(mgrType, field)?.GetValue(mgr) as Button;
            return b != null && ReferenceEquals(b.gameObject, go);
        }

        /// <summary>Upper-cases the first letter, so a bare function reads as a caption.</summary>
        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        /// <summary>
        /// Names a music playlist row, searching the focused object and its parents.
        ///
        /// Same arrangement as the friend list and the recipe rows: the EventSystem focuses the
        /// row's child `PlaylistSongButton` while the song and artist Texts hang off the
        /// TG_PlaylistSongUI above it, so the lookup walks the parent chain.
        /// </summary>
        private static string GetSongRowLabel(GameObject go)
        {
            try
            {
                Type t = AccessTools.TypeByName("TG_PlaylistSongUI");
                if (t == null) return string.Empty;

                Component row = go.GetComponentInParent(t);
                return FullGame.MusicAppPatches.DescribeSongRow(row as MonoBehaviour);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Names a brewing ingredient button from its `value` (a TG_Static.Ingredients enum),
        /// resolved through the game's own localized name table.
        ///
        /// Reflection rather than a direct cast so that a rename in the retail build degrades to
        /// the generic scan (which will then say "unlabeled" - an audible gap) instead of throwing
        /// inside the focus watcher.
        /// </summary>
        private static string GetIngredientName(MonoBehaviour ingredientButton)
        {
            try
            {
                System.Reflection.FieldInfo valueField = ingredientButton.GetType().GetField("value");
                if (valueField == null) return string.Empty;

                object raw = valueField.GetValue(ingredientButton);
                if (!(raw is TG_Static.Ingredients)) return string.Empty;

                return Brewing.BrewingPatches.IngredientName((TG_Static.Ingredients)raw);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Recognizes controls whose activation quits the game or destroys progress, by object
        /// name. Deliberately matches on the ENGLISH object names (which are stable identifiers in
        /// the scene, unlike the I2.Loc-translated captions the player hears).
        /// </summary>
        internal static bool IsDestructive(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("exit") || n.Contains("quit")
                || n.Contains("delete") || n.Contains("erase")
                || n.Contains("overwrite") || n.Contains("newgame");
        }

        /// <summary>
        /// Reads the caption from the game's own button components, which hold an explicit Text
        /// reference rather than relying on hierarchy. Covers TG_MainMenuButton (and its Expo
        /// subclass) plus the generic TG_Button. Uses reflection so the mod does not hard-bind to
        /// field layout that differs between the two, and so a missing field degrades to the
        /// generic scan instead of throwing.
        /// </summary>
        private static string GetGameLabel(GameObject go)
        {
            try
            {
                // Friend-list rows are checked first and through the PARENT chain, because
                // TG_SocialMediaApp selects FriendListButton.gameObject while the text-bearing
                // TG_FriendListPrefabUI usually sits on the row above it. GetComponentInParent
                // includes the object itself, so this covers both arrangements.
                string friendLabel = GetFriendListLabel(go);
                if (!string.IsNullOrEmpty(friendLabel)) return friendLabel;

                // Recipe rows are the same arrangement as friend rows: the EventSystem focuses
                // openDescButton while the drink's name and ingredients live on the TG_DrinkItemUI
                // above it. Checked through the parent chain for that reason.
                string recipeLabel = GetRecipeRowLabel(go);
                if (!string.IsNullOrEmpty(recipeLabel)) return recipeLabel;

                // Achievement icons are checked through the PARENT chain too, and for the same
                // reason the two above are: TG_AchievementIconUI holds its Button in a separate
                // `button` field, so the object the EventSystem focuses is not necessarily the one
                // carrying the component. The component scan below is object-local, so wherever the
                // prefab puts the Button on a child, that scan finds nothing, the icon announces as
                // "unlabeled", and the composed phrase stays parked - then leaks onto the NEXT
                // control focused. An icon-only grid has no fallback text to degrade to.
                string achievementLabel = GetAchievementLabel(go);
                if (!string.IsNullOrEmpty(achievementLabel)) return achievementLabel;

                // Calendar day cells are the SAME arrangement and carry the same risk: like the
                // achievement icons, TG_CalendarLoadUI extends TG_Button and inherits its `button`
                // field, so the focused object need not be the one holding the component.
                string dayLabel = GetCalendarDayLabel(go);
                if (!string.IsNullOrEmpty(dayLabel)) return dayLabel;

                // Music playlist rows, same parent-chain arrangement again. Composed explicitly
                // rather than left to the component scan below because the row's song and artist
                // Texts are found in hierarchy order, which puts the artist first as often as not.
                string songLabel = GetSongRowLabel(go);
                if (!string.IsNullOrEmpty(songLabel)) return songLabel;

                // Phone app icons carry only a brand name ("Tomodachill"), which says nothing about
                // what the app does. Checked before the generic scan below, because that scan WOULD
                // find the brand Text and return it alone - which is exactly the unhelpful half.
                string phoneAppLabel = GetPhoneAppLabel(go);
                if (!string.IsNullOrEmpty(phoneAppLabel)) return phoneAppLabel;

                // Profile slots, the same parent-chain arrangement once more. This one matters most
                // of all: on retail the picker is the FIRST interactive screen (PRESS_ANY_KEY goes
                // straight to OpenSelectProfile), so without this the player's first contact with the
                // game is three cards that all announce "unlabeled".
                string profileLabel = GetProfileSlotLabel(go);
                if (!string.IsNullOrEmpty(profileLabel)) return profileLabel;

                MonoBehaviour[] comps = go.GetComponents<MonoBehaviour>();
                for (int i = 0; i < comps.Length; i++)
                {
                    MonoBehaviour c = comps[i];
                    if (c == null) continue;

                    string type = c.GetType().Name;

                    // The startup language picker builds 11 identical flag buttons from a prefab.
                    // They carry NO text at all - the language name is shown in a SEPARATE label -
                    // so the generic scan announced "Init Scene Flag Button( Clone)" eleven times.
                    // The button knows its own index into TG_Static.languageList; read that.
                    if (type == "TG_LanguageButton")
                    {
                        string lang = GetLanguageName(c);
                        if (!string.IsNullOrEmpty(lang)) return lang;
                        continue;
                    }

                    // Brewing ingredients are ICON buttons - they carry no Text anywhere, and
                    // TG_Button (their base) has no textComponentToMove either, so every path
                    // below fails and they announced as "Button Milk, unlabeled" or worse. The
                    // button does know what it is: an Ingredients enum value, which
                    // TG_DrinkManager.ingredientsLocList maps to a localized name.
                    if (type == "TG_IngredientButton")
                    {
                        string ingredient = GetIngredientName(c);
                        if (!string.IsNullOrEmpty(ingredient)) return ingredient;
                        continue;
                    }

                    if (type != "TG_MainMenuButton" && type != "TG_MainMenuButtonExpo" && type != "TG_Button")
                        continue;

                    System.Reflection.FieldInfo f = c.GetType().GetField("textComponentToMove");
                    if (f == null) continue;

                    Text label = f.GetValue(c) as Text;
                    if (label != null && !string.IsNullOrEmpty(label.text)) return Clean(label.text);
                }
            }
            catch
            {
                // Fall through to the generic component scan.
            }
            return string.Empty;
        }

        /// <summary>Strips Fungus/TMP markup the same way dialogue lines are cleaned.</summary>
        private static string Clean(string raw)
        {
            return FungusText.ExtractWords(raw);
        }

        /// <summary>
        /// Turns an object name like "ButtonNewGame" into "Button New Game" as a fallback when a
        /// control carries no text component.
        /// </summary>
        private static string Prettify(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            StringBuilder sb = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '_' || c == '-') { sb.Append(' '); continue; }
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
