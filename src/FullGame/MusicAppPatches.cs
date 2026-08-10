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
    /// Speaks the smartphone's music app: which song is playing, and the playlist rows as the
    /// player moves through them.
    ///
    /// ⚠ UNREACHABLE ON THE DEMO, like everything else behind the phone (`canOpenSmartPhone` is
    /// false - see SmartPhonePatches and project_status.md). Built because the DATA is fully
    /// present offline and the code transfers to retail unchanged; do not let it drift to
    /// "working" without a retail run.
    ///
    /// ⚠ THE TWO APP CLASSES ARE GENUINELY DIFFERENT, and PLAN.md's advice to "target the shared
    /// base TG_MusicAppGeneral" does not survive contact with the decompiled source. Every method
    /// on that base is an EMPTY VIRTUAL - `PlaylistSongButtonClick`, `NextButtonClick`,
    /// `PreviousButtonClick` and `ButtonInput` all have `{ }` bodies, and Harmony patching a
    /// virtual does NOT intercept calls dispatched to an override. A postfix there would attach
    /// (so VerifyExpectedPatches would report it live and green) and never once fire. The advice
    /// was right about the RISK - testing only against the demo class gives false confidence about
    /// retail - and wrong about the remedy: the fix is to hook BOTH concrete classes, which is what
    /// this file does.
    ///
    /// The two differ in more than layout:
    ///   - TG_MusicApp (retail) keeps song/artist on a separate TG_MusicAppHomeUI +
    ///     TG_MusicAppPlaylistUI pair, shuffles by default, and has an "under maintenance" state
    ///     that swallows every input.
    ///   - TG_MusicAppDemo builds its rows from a prefab into its own `playlistSongUIList`, writes
    ///     `songNameText`/`artistNameText` directly, and has no shuffle and no maintenance gate.
    /// So `PlaylistSongButtonClick` is postfixed on EACH concrete class rather than on the base.
    ///
    /// Rows are a LABELLER, not a narrator - the same arrangement as the recipe rows and the friend
    /// list. TG_PlaylistSongUI implements ISelectHandler and holds `SongNameText`/`ArtistNameText`
    /// as PROPERTIES, while the EventSystem focuses the child `PlaylistSongButton`, so
    /// FocusNarrator walks the parent chain to find the row. A second announcer here would talk
    /// over the focus line.
    /// </summary>
    [HarmonyPatch]
    public static class MusicAppPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        /// <summary>
        /// The song the app last started playing, so a row can be marked "now playing" and a
        /// repeated announcement of the same track can be suppressed.
        ///
        /// ⚠ Held as the SONG NAME rather than the index: the two app classes index different
        /// lists (retail shuffles through `shuffleIndexPlaylist`, the demo does not), so an index
        /// is only meaningful next to the class that produced it. The name is comparable across
        /// both.
        /// </summary>
        private static string _nowPlaying;

        /// <summary>Suppresses a duplicate "now playing" for the track already announced.</summary>
        private static string _lastAnnouncedSong;

        /// <summary>
        /// Announces the track the retail app just started.
        ///
        /// Hooked on the CONCRETE class for the reason in the file header: the base's
        /// PlaylistSongButtonClick is an empty virtual and a postfix there never runs.
        ///
        /// ⚠ This method is re-entered by every route into playback - a row click, Next, Previous,
        /// and `SetCurrentSong` when the app opens onto whatever the café is already playing. That
        /// is fine (the announcement is idempotent and deduped) but it means this is NOT a
        /// "fires once per user action" hook. Same shape as RefreshList -> DisplayDrinks on the
        /// recipes app; check the callers for re-entry, not only that they converge.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_MusicApp), "PlaylistSongButtonClick")]
        public static void AfterRetailSongClick(TG_MusicApp __instance, int index)
        {
            AnnounceSongAt(__instance, index);
        }

        /// <summary>Same announcement for the demo build's own implementation.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_MusicAppDemo), "PlaylistSongButtonClick")]
        public static void AfterDemoSongClick(TG_MusicAppDemo __instance, int index)
        {
            AnnounceSongAt(__instance, index);
        }

        /// <summary>
        /// Reads the song metadata off the app's own scriptable-object array and speaks it.
        ///
        /// ⚠ Read from `scriptableMusicSongAppsArray` rather than from the UI Text fields, which is
        /// the opposite of the choice the newspaper reader made, and deliberately: there the app
        /// had already resolved a localization key and applied a fallback we did not want to
        /// duplicate, whereas these are plain authored strings on a ScriptableObject with no
        /// localization step at all. The array is also the ONLY source that works for both classes
        /// - retail writes its text through TG_MusicAppHomeUI, the demo writes its own fields.
        ///
        /// ⚠ Song/artist/album are PROPERTIES on TG_ScriptableMusicSongApp, not fields.
        /// AccessTools.Field returns null on a property SILENTLY, so this goes through the
        /// property-then-field helper (memory: coffee-talk-decompile-field-vs-property).
        /// </summary>
        private static void AnnounceSongAt(object app, int index)
        {
            try
            {
                if (app == null) return;

                Array songs = Read<Array>(app, "scriptableMusicSongAppsArray");
                if (songs == null || index < 0 || index >= songs.Length) return;

                object song = songs.GetValue(index);
                if (song == null) return;

                string name = Read<string>(song, "SongName");
                string artist = Read<string>(song, "ArtistName");
                string album = Read<string>(song, "AlbumName");

                // The song NAME is the only part worth refusing to speak over. A track with no
                // name at all is a data gap, and saying so is better than silence - the player
                // otherwise cannot tell a nameless track from a dead hook.
                if (string.IsNullOrEmpty(name)) name = "Untitled track";

                _nowPlaying = name;

                // Deduped because every route into playback re-enters this hook, and because
                // TG_MusicApp.SetCurrentSong loops the whole array calling PlaylistSongButtonClick
                // on a match - so opening the app onto the current song can announce it twice.
                if (name == _lastAnnouncedSong) return;
                _lastAnnouncedSong = name;

                StringBuilder sb = new StringBuilder();
                sb.Append("Now playing, ").Append(name);
                if (!string.IsNullOrEmpty(artist)) sb.Append(", by ").Append(artist);
                if (!string.IsNullOrEmpty(album)) sb.Append(", from ").Append(album);

                Announce(sb.ToString(), true);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Music] song hook threw: " + e.Message);
            }
        }

        /// <summary>
        /// Names a focused playlist row for FocusNarrator: song, artist, position, and whether it
        /// is the track currently playing.
        ///
        /// Supplied as a LABEL rather than spoken here for the reason recorded on the recipe and
        /// achievement screens - the focus watcher announces a frame later and would talk over
        /// anything said from this file.
        ///
        /// ⚠ `SongNameText`/`ArtistNameText` are PROPERTIES on TG_PlaylistSongUI. The generic
        /// component scan in FocusNarrator would find the child Texts eventually, but in an
        /// arbitrary hierarchy order - artist before song as often as not - so the row is composed
        /// explicitly here instead.
        /// </summary>
        internal static string DescribeSongRow(MonoBehaviour row)
        {
            try
            {
                if (row == null) return string.Empty;

                Text songText = Read<Text>(row, "SongNameText");
                Text artistText = Read<Text>(row, "ArtistNameText");

                string name = songText != null ? songText.text : null;
                string artist = artistText != null ? artistText.text : null;

                // An empty row is a real state on this screen: TG_MusicAppDemo.InitPlaylistSong
                // blanks the header texts before it fills the list, and a row whose scriptable
                // object carried no name renders empty rather than absent. Return empty so
                // FocusNarrator falls through to its own "unlabeled" path, which names the gap.
                if (string.IsNullOrEmpty(name)) return string.Empty;

                StringBuilder sb = new StringBuilder();
                sb.Append(Clean(name));
                if (!string.IsNullOrEmpty(artist)) sb.Append(", by ").Append(Clean(artist));

                // "Now playing" is the one thing a sighted player reads instantly off this screen
                // (the row turns green and shows a speaker icon) and that is otherwise completely
                // inaudible while browsing a list during playback.
                if (!string.IsNullOrEmpty(_nowPlaying) && Clean(name) == _nowPlaying)
                    sb.Append(", now playing");

                string position = RowPosition(row);
                if (!string.IsNullOrEmpty(position)) sb.Append(", ").Append(position);

                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// "3 of 12" for a playlist row, found by asking the row's own parent for its index.
        ///
        /// Located through the TRANSFORM rather than through the app's `playlistSongUIList`,
        /// because that list is private to TG_MusicAppDemo and the retail app keeps its equivalent
        /// on TG_MusicAppPlaylistUI - two different owners for the same question. Sibling index
        /// under the shared content container answers it for both, and degrades to no position
        /// rather than to a wrong one.
        /// </summary>
        private static string RowPosition(MonoBehaviour row)
        {
            try
            {
                Transform t = row.transform;
                Transform parent = t.parent;
                if (parent == null || parent.childCount <= 1) return string.Empty;

                return (t.GetSiblingIndex() + 1) + " of " + parent.childCount;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Clears the per-screen state when the music app closes.
        ///
        /// ⚠ `_nowPlaying` deliberately SURVIVES: the café keeps playing the track after the app
        /// is dismissed, so it is still true. Only the DEDUP is reset, so re-opening the app
        /// announces the current song again - which is exactly what a player who just returned
        /// wants to hear.
        ///
        /// Filtered on the instance type because TG_SmartPhoneApps.Close is a shared base method
        /// that the newspaper and recipes readers also postfix; see Main.ReportDoublePatches, which
        /// exempts it for that reason.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_SmartPhoneApps), nameof(TG_SmartPhoneApps.Close))]
        public static void AfterClose(TG_SmartPhoneApps __instance)
        {
            if (__instance is TG_MusicAppGeneral) _lastAnnouncedSong = null;
        }

        /// <summary>
        /// Reads a member by name, trying the PROPERTY first and then the field.
        ///
        /// ⚠ This order is load-bearing. TG_ScriptableMusicSongApp exposes SongName/ArtistName/
        /// AlbumName as properties over private backing fields, and AccessTools.Field returns null
        /// on a property WITHOUT throwing - so a field-only lookup fails silently and the screen
        /// goes quiet with a clean log (memory: coffee-talk-decompile-field-vs-property).
        /// </summary>
        private static T Read<T>(object owner, string member) where T : class
        {
            if (owner == null) return null;

            Type t = owner.GetType();

            System.Reflection.PropertyInfo p = AccessTools.Property(t, member);
            if (p != null) return p.GetValue(owner, null) as T;

            System.Reflection.FieldInfo f = AccessTools.Field(t, member);
            if (f != null) return f.GetValue(owner) as T;

            return null;
        }

        /// <summary>Strips any markup the same way every other spoken line is cleaned.</summary>
        private static string Clean(string raw)
        {
            return Dialogue.FungusText.ExtractWords(raw);
        }

        private static void Announce(string line, bool interrupt)
        {
            if (string.IsNullOrEmpty(line)) return;

            ISpeechOutput speech = Speech;
            if (speech == null || !speech.IsAvailable) return;

            MelonLogger.Msg("[Music] " + line);
            speech.SpeakAs(null, line, TextType.Menu, interrupt);
        }
    }
}
