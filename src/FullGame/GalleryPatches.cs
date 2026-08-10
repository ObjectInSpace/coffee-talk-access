using System;
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
    /// Speaks the extras-menu GALLERY and COMICS browsers.
    ///
    /// ⚠ BE HONEST ABOUT WHAT THIS DOES AND DOES NOT DELIVER. These two screens are picture
    /// viewers. The artwork itself is not describable by any means available to this mod, and
    /// nothing here changes that - there is no alt text in the game data, and inventing
    /// descriptions would be fabrication. What IS deliverable, and what this file delivers, is
    /// everything AROUND the picture: which item is selected, how far through the set it is,
    /// whether it is still locked, and - for the gallery - the game's OWN description text where
    /// one exists. That makes the screens navigable and their state legible; it does not make the
    /// images accessible, and it should not be described as if it did.
    ///
    /// ⚠ THE GALLERY HAS MORE THAN CHROME, WHICH THE FIRST SURVEY MISSED. PLAN.md recorded these
    /// as visual-only on the basis that their classes reference almost no localization terms. That
    /// is true of the COMICS, but the gallery carries a per-picture `description` string on
    /// TG_GalleryDisplay, shown in `biggestGalleryDescriptionText` when a picture is opened full
    /// screen. It is real authored prose about the artwork and it is spoken here.
    ///
    /// ⚠ TWO DECOY CLASSES, BOTH ONE STEP FROM A SILENT HOOK. `TG_BigPictureGallery` sounds like
    /// the gallery screen and is a single `Image` field - not a screen at all. `TG_GalleryItem`
    /// looks like the item model and carries exactly the members you would want (`isUnlocked`,
    /// `key`) - but the manager's list is `TG_GalleryDisplay`, a DIFFERENT type, and reflection
    /// over the shipped assembly confirms `TG_GalleryDisplay.key` and `TG_ComicDisplayUI.isUnlocked`
    /// DO NOT EXIST. Only members verified present on the real types are read below. This is the
    /// same trap as TG_CalendarContent vs TG_CalendarLoadUI, three times over now: in this codebase
    /// a plausible class name is not evidence.
    ///
    /// WHY SetLargeImage / SetBiggestImage AND NOT THE PROPERTY SETTER. Both managers funnel every
    /// movement through a `CurrentIdx` property whose setter wraps the index, recomputes the page,
    /// and then calls SetLargeImage (always) or SetBiggestImage (when zoomed). Patching the two
    /// methods rather than `set_CurrentIdx` gets the same coverage AND receives the display object
    /// as an argument - which is where the description lives. It also means the announcement
    /// reflects the CLAMPED index the setter settled on, not the raw value it was handed.
    ///
    /// ⚠ UNTESTED, and likely unreachable on the demo (extras menu). Built for the retail build.
    /// </summary>
    [HarmonyPatch]
    public static class GalleryPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        /// <summary>
        /// Guards against re-announcing the same item. Both managers call SetLargeImage from
        /// several paths (the CurrentIdx setter, DisplayItems, page changes), so the same picture
        /// can be re-set several times without the player having moved - which would otherwise
        /// stutter the same line repeatedly.
        /// </summary>
        private static string _lastSpoken;

        /// <summary>
        /// Announces the gallery picture that just became current.
        ///
        /// Speaks directly rather than parking a phrase for FocusNarrator: unlike the achievement
        /// grid, movement here is driven by the manager's own CurrentIdx (left/right buttons and
        /// the shoulder buttons), NOT by EventSystem focus moving between per-item Selectables. So
        /// there is no focus change for FocusNarrator to observe and nothing to be talked over by.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_GalleryManager), nameof(TG_GalleryManager.SetLargeImage))]
        public static void AfterGallerySetLarge(TG_GalleryManager __instance, TG_GalleryDisplay galleryDisplay)
        {
            try
            {
                if (__instance == null || galleryDisplay == null) return;

                // While the full-screen view is open, SetBiggestImage handles the announcement and
                // says strictly more (it adds the description). Announcing here as well would speak
                // the shorter line first and then interrupt it with the longer one.
                if (IsZoomed(__instance, "biggestImageCanvasPanel")) return;

                bool unlocked = GetBool(galleryDisplay, "isUnlocked");

                StringBuilder sb = new StringBuilder();
                sb.Append(Position(__instance, "currentIdx", "galleryCount", "Picture"));

                // Locked state is carried ONLY by the sprite and by how the image is scaled
                // (SetBiggestImage anchors it differently) - there is no text on screen saying so.
                // Without this the player cannot tell an unseen picture from a placeholder.
                sb.Append(unlocked ? ", unlocked" : ", locked");

                Announce(sb.ToString());
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Gallery] large-image hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Announces a gallery picture opened full screen, including the game's own description.
        ///
        /// The description is the one piece of genuine CONTENT on this screen, so the full-screen
        /// view - the deliberate act of looking closely at one picture - is where it belongs.
        /// Read from the resolved `biggestGalleryDescriptionText` component rather than from the
        /// display's `description` field, because SetBiggestImage decides whether to show it at all
        /// (it branches on the field being empty); reading what the game DISPLAYED keeps the
        /// announcement matched to the screen.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_GalleryManager), nameof(TG_GalleryManager.SetBiggestImage))]
        public static void AfterGallerySetBiggest(TG_GalleryManager __instance, TG_GalleryDisplay galleryDisplay)
        {
            try
            {
                if (__instance == null || galleryDisplay == null) return;

                StringBuilder sb = new StringBuilder();
                sb.Append(Position(__instance, "currentIdx", "galleryCount", "Picture"));

                bool unlocked = GetBool(galleryDisplay, "isUnlocked");
                sb.Append(unlocked ? ", unlocked" : ", locked");

                string description = ReadText(__instance, typeof(TG_GalleryManager), "biggestGalleryDescriptionText");
                if (!string.IsNullOrEmpty(description)) sb.Append(". ").Append(description);

                // The picture itself cannot be described, and saying nothing about that gap would
                // leave a player waiting for a description that is never coming. Naming it is the
                // same rule as saying ", unlabeled" out loud.
                else if (unlocked) sb.Append(", no description. Artwork not described.");

                Announce(sb.ToString());
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Gallery] biggest-image hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Announces the comic that just became current.
        ///
        /// Comics carry no description field - only a title, and only once opened full screen (see
        /// below) - so the browsing line is position alone.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ComicMenuManager), nameof(TG_ComicMenuManager.SetLargeImage))]
        public static void AfterComicSetLarge(TG_ComicMenuManager __instance)
        {
            try
            {
                if (__instance == null) return;
                if (IsZoomed(__instance, "biggestImageCanvasPanel")) return;

                Announce(Position(__instance, "currentIdx", "comicDataLength", "Comic"));
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Comic] large-image hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Announces a comic opened full screen: its title, and how many panels it has.
        ///
        /// The panel count is worth saying because opening a comic switches the left/right buttons
        /// from "next comic" to "next panel" - the same keys now do something different, and a
        /// sighted player sees that from the page indicator. `GetcomicPictures` is asked for the
        /// CURRENT LANGUAGE's panel array, which is the same call the game makes to build the view.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ComicMenuManager), nameof(TG_ComicMenuManager.SetBiggestImage))]
        public static void AfterComicSetBiggest(TG_ComicMenuManager __instance, TG_ComicDisplayUI comicDisplay)
        {
            try
            {
                if (__instance == null || comicDisplay == null) return;

                StringBuilder sb = new StringBuilder();
                sb.Append(Position(__instance, "currentIdx", "comicDataLength", "Comic"));

                // The title comes from the game's own accessor, which the manager calls to fill
                // comicTileText. Read via the same route rather than the label, because the label
                // may not be written yet at postfix time on the first open.
                string title = ReadTitle(comicDisplay);
                if (!string.IsNullOrEmpty(title)) sb.Append(", ").Append(title);

                int panels = CountPanels(comicDisplay);
                if (panels > 0) sb.Append(", ").Append(panels).Append(panels == 1 ? " panel" : " panels");

                sb.Append(". Comic art not described.");

                Announce(sb.ToString());
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Comic] biggest-image hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Builds an "Picture 4 of 30" clause from the manager's clamped index and its count.
        ///
        /// The index is read AFTER the setter clamped it (these hooks run downstream of that), so
        /// wrapping past either end reports the day it landed on rather than the out-of-range value
        /// it was handed. Spoken 1-based, because the stored index is 0-based and "picture 0" is
        /// not how anyone counts pictures.
        /// </summary>
        private static string Position(object manager, string idxField, string countField, string noun)
        {
            int idx = GetInt(manager, idxField);
            int count = GetInt(manager, countField);

            StringBuilder sb = new StringBuilder(noun);
            sb.Append(' ').Append(idx + 1);

            // Omitted rather than guessed when the count is unreadable - a wrong denominator would
            // have the player calibrating against a set size the game does not use.
            if (count > 0) sb.Append(" of ").Append(count);

            return sb.ToString();
        }

        /// <summary>True when the full-screen view is open, so the browsing hook should stand
        /// down in favour of the fuller announcement.</summary>
        private static bool IsZoomed(object manager, string panelField)
        {
            object v = AccessTools.Field(manager.GetType(), panelField)?.GetValue(manager);
            GameObject panel = v as GameObject;
            return panel != null && panel.activeSelf;
        }

        /// <summary>Asks the comic's scriptable data for its localized title.</summary>
        private static string ReadTitle(TG_ComicDisplayUI display)
        {
            try
            {
                object data = AccessTools.Field(typeof(TG_ComicDisplayUI), "scriptableComicData")?.GetValue(display);
                if (data == null) return string.Empty;

                object title = AccessTools.Method(data.GetType(), "GetTitleComic")?.Invoke(data, null);
                return title == null ? string.Empty : Dialogue.FungusText.ExtractWords(Convert.ToString(title));
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Counts a comic's panels in the player's current language.
        ///
        /// Comics are localized as separate image sets per language, so the count is asked for
        /// with the same CurrentLanguage the manager passes - a hardcoded language would report the
        /// wrong number for anyone not playing in it.
        /// </summary>
        private static int CountPanels(TG_ComicDisplayUI display)
        {
            try
            {
                object data = AccessTools.Field(typeof(TG_ComicDisplayUI), "scriptableComicData")?.GetValue(display);
                if (data == null) return 0;

                object settings = AccessTools.Field(AccessTools.TypeByName("TG_Static"), "userSettingsData")?.GetValue(null);
                if (settings == null) return 0;

                object language = AccessTools.Property(settings.GetType(), "CurrentLanguage")?.GetValue(settings, null)
                                  ?? AccessTools.Field(settings.GetType(), "CurrentLanguage")?.GetValue(settings);
                if (language == null) return 0;

                Array pictures = AccessTools.Method(data.GetType(), "GetcomicPictures")
                    ?.Invoke(data, new object[] { Convert.ToString(language) }) as Array;

                return pictures == null ? 0 : pictures.Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>Reads a legacy UI.Text field. Every text field on both screens is UI.Text,
        /// confirmed by reflecting over the shipped assembly.</summary>
        private static string ReadText(object owner, Type type, string field)
        {
            object v = AccessTools.Field(type, field)?.GetValue(owner);
            Text text = v as Text;
            if (text == null || string.IsNullOrEmpty(text.text)) return string.Empty;
            return Dialogue.FungusText.ExtractWords(text.text);
        }

        private static int GetInt(object target, string field)
        {
            object v = AccessTools.Field(target.GetType(), field)?.GetValue(target);
            return v is int ? (int)v : 0;
        }

        private static bool GetBool(object target, string field)
        {
            object v = AccessTools.Field(target.GetType(), field)?.GetValue(target);
            return v is bool && (bool)v;
        }

        /// <summary>
        /// Speaks a line, suppressing an immediate repeat of the identical announcement.
        ///
        /// The dedup is on the TEXT rather than on the index, because two different paths (a page
        /// change and an index change) can land on the same item and would otherwise each announce
        /// it. Cleared implicitly by any different line, so moving away and back re-announces.
        /// </summary>
        private static void Announce(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (line == _lastSpoken) return;
            _lastSpoken = line;

            ISpeechOutput speech = Speech;
            if (speech == null || !speech.IsAvailable) return;

            MelonLogger.Msg("[Gallery] " + line);
            speech.SpeakAs(null, line, TextType.Menu, true);
        }
    }
}
