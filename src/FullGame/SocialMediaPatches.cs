using System;
using System.Text;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using TMPro;
using UnityAccessibilityLib;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeTalkAccess.FullGame
{
    /// <summary>
    /// Speaks the smartphone's social-media app: the character profile a player opens from the
    /// friend list, with its three trivia slots and which of them are still locked.
    ///
    /// ⚠ UNREACHABLE ON THE DEMO. Two independent gates, as with the rest of the phone:
    /// `canOpenSmartPhone` is false, and this app's content is PLAYER PROGRESS
    /// (`profileData.UnlockedCharacterDataList`) rather than shipped data, so it is near-empty on
    /// the demo's single day even if the panel opened. Built blind, to be verified on retail.
    ///
    /// ⚠ PLAN.md filed this screen as "blocked twice over - Scrollbar-driven, and zero
    /// localization keys". Both halves need qualifying, and the conclusion changes:
    ///
    ///   1. **The scrollbar is not in the way of the CONTENT.** `ScrollDetailProfile` moves a
    ///      Scrollbar with no per-item focus, which is why no cursor is built here - but the pane
    ///      holds exactly THREE trivia slots (`informationText[0..2]`, the loop in SetDetailProfile
    ///      is a literal `i < 3`), bounded by `GetTotalAffectionLevel()` which caps at 3. Three
    ///      bounded strings do not need a cursor; they need one announcement. That is the same
    ///      reason the newspaper is read as one utterance, applied to a much smaller body.
    ///   2. **The empty key prefixes were `socialMedia/`, `news/` and `profile/`.** This screen
    ///      does not read any of them: names come from `characterNameLocaliztionDictionary` and
    ///      trivia from `localizer.GetSocialMediaProfileLocalization(...)` keyed off the character
    ///      ScriptableObject's own `description[]`. Whether THOSE resolve on the demo is a separate
    ///      question from the three prefixes that were measured. Same trap as `news/` versus
    ///      `newspaperApp/`: adjacent names, different key spaces.
    ///
    /// So the friend LIST was already labelled (SmartPhonePatches.DescribeFriendEntry, reached
    /// through FocusNarrator's parent chain) and the DETAIL pane is what this file adds.
    /// </summary>
    [HarmonyPatch]
    public static class SocialMediaPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        /// <summary>Suppresses a repeat of the profile already announced.</summary>
        private static string _lastSpoken;

        /// <summary>
        /// Announces a character's profile when the detail pane is filled in.
        ///
        /// Hooked on `SetDetailProfile` rather than on `FriendListButtonClik` because this is where
        /// the text actually lands, and because `RefreshDetailProfile` also routes here when the
        /// app re-opens onto the profile last viewed. One hook, every route.
        ///
        /// ⚠ Spoken from the postfix rather than parked for the focus watcher, which is the
        /// opposite of the friend-list rows and deliberately so: opening the pane sets
        /// `EventSystem.current.SetSelectedGameObject(null)` (TG_SocialMediaApp:255) and the pane
        /// itself is a scrolling wall of text with NO focusable item. There is no focus event to
        /// attach a label to - if this file does not speak, nothing does.
        ///
        /// ⚠ It does not interrupt. `DoRefreshDetailContent` toggles the pane active/inactive three
        /// times across three frames before settling, and the friend row the player just activated
        /// may still be being read. This is content they asked for by pressing a button, so it can
        /// afford to queue behind that rather than cut it off.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_SocialMediaDetailProfileUI),
            nameof(TG_SocialMediaDetailProfileUI.SetDetailProfile))]
        public static void AfterSetDetailProfile(TG_SocialMediaDetailProfileUI __instance)
        {
            try
            {
                if (__instance == null) return;

                StringBuilder sb = new StringBuilder();

                Text nameText = Read<Text>(__instance, "ProfileNameText", "profileNameText");
                string name = nameText != null ? Clean(nameText.text) : null;

                // A profile with no name is a data gap, not a reason to fall silent - the player
                // would otherwise be unable to tell an unnamed contact from a dead hook.
                sb.Append(string.IsNullOrEmpty(name) ? "Profile, name unavailable" : name);

                // The three trivia slots. Read back off the TMP components rather than re-derived
                // from `description[]` + `UnlockedDescriptionIndex`, because by postfix time the
                // game has already resolved the localization AND decided which slots are locked;
                // redoing that would mean a second copy of the affection-level rules to drift.
                TextMeshProUGUI[] slots = Read<TextMeshProUGUI[]>(__instance, "TriviaText", "informationText");

                int spoken = 0;
                int locked = 0;
                if (slots != null)
                {
                    for (int i = 0; i < slots.Length; i++)
                    {
                        TextMeshProUGUI slot = slots[i];
                        if (slot == null || string.IsNullOrEmpty(slot.text)) continue;

                        string line = Clean(slot.text);
                        if (line.Length == 0) continue;

                        // A LOCKED slot holds one of TG_Static.socialMediaLockedStatusLocalization
                        // and is rendered in italics; an unlocked one is set back to
                        // FontStyles.Normal on the same line that writes its real text
                        // (SetDetailProfile:106-107). The font style is therefore the game's OWN
                        // locked/unlocked signal, and it works in every language - unlike matching
                        // the placeholder string, which would need the English text.
                        bool isLocked = (slot.fontStyle & FontStyles.Italic) != 0;
                        if (isLocked)
                        {
                            locked++;
                            continue;
                        }

                        sb.Append(". ").Append(line);
                        spoken++;
                    }
                }

                // Count the locked slots rather than reading each placeholder aloud: they are all
                // the same sentence, and hearing it three times says less than being told there
                // are three things left to learn. Silence here would be worse - a player who
                // cannot see the greyed-out rows has no other way to know the profile is partial.
                if (locked > 0)
                {
                    sb.Append(". ").Append(locked)
                      .Append(locked == 1 ? " more thing to learn" : " more things to learn");
                }
                else if (spoken == 0)
                {
                    sb.Append(". No details yet");
                }

                string line2 = sb.ToString();
                if (line2 == _lastSpoken) return;
                _lastSpoken = line2;

                Announce(line2, false);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[SocMed] profile hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Clears the dedup when the social app closes.
        ///
        /// ⚠ An uncleared dedup turns a duplicate into permanent SILENCE, which for a blind player
        /// is strictly worse than hearing something twice: re-opening the app onto the same
        /// character would say nothing at all. Same rule as the recipes app's tab dedup.
        ///
        /// Filtered on the instance type because TG_SmartPhoneApps.Close is a shared base method
        /// several app readers postfix on purpose; Main.ReportDoublePatches exempts it for that.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_SmartPhoneApps), nameof(TG_SmartPhoneApps.Close))]
        public static void AfterClose(TG_SmartPhoneApps __instance)
        {
            if (__instance is TG_SocialMediaApp) _lastSpoken = null;
        }

        /// <summary>
        /// Reads a member that may be a PROPERTY or a field, in that order.
        ///
        /// TG_SocialMediaDetailProfileUI exposes `ProfileNameText` and `TriviaText` as properties
        /// over private backing fields, and AccessTools.Field returns null on a property SILENTLY -
        /// the failure mode recorded in memory `coffee-talk-decompile-field-vs-property`. Both
        /// names are tried so a retail build that flattens either one still resolves.
        /// </summary>
        private static T Read<T>(object instance, string propertyName, string fieldName)
            where T : class
        {
            if (instance == null) return null;

            Type t = instance.GetType();
            object value = AccessTools.Property(t, propertyName)?.GetValue(instance, null)
                           ?? AccessTools.Field(t, fieldName)?.GetValue(instance);
            return value as T;
        }

        /// <summary>Strips markup the same way every other spoken line is cleaned.</summary>
        private static string Clean(string raw)
        {
            return string.IsNullOrEmpty(raw) ? string.Empty : Dialogue.FungusText.ExtractWords(raw);
        }

        private static void Announce(string line, bool interrupt)
        {
            if (string.IsNullOrEmpty(line)) return;

            ISpeechOutput speech = Speech;
            if (speech == null || !speech.IsAvailable) return;

            MelonLogger.Msg("[SocMed] " + line);
            speech.SpeakAs(null, line, TextType.Menu, interrupt);
        }
    }
}
