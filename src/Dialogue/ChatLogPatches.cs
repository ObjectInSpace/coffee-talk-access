using System;
using System.Collections;
using System.Reflection;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using UnityAccessibilityLib;
using UnityEngine;

namespace CoffeeTalkAccess.Dialogue
{
    /// <summary>
    /// Makes the chat log readable: an ENTRY CURSOR over the game's own log data.
    ///
    /// WHY A CURSOR, WHEN NOTHING ELSE IN THE MOD INVENTS NAVIGATION. Everywhere else the rule has
    /// been "announce what the game focuses, never build a second cursor" - that rule exists because
    /// MenuCursor once disagreed with the game's cursorIdx and quit the game. It does not apply here,
    /// because THE CHAT LOG HAS NO SELECTION TO DISAGREE WITH. The game scrolls it as a wall of
    /// pixels: TG_ChatLogManager.UpdateDefaultChatLog reads playerActions.Up/Down.IsPressed every
    /// frame and nudges chatLogScroolBar.value by a continuous float (with an acceleration ramp
    /// after one second held). There is no row index, no Selectable, no EventSystem focus - for the
    /// GAMEPAD either. A focus watcher would have literally nothing to observe, so there is no
    /// second cursor to conflict with: this is the only cursor in the screen.
    ///
    /// WHERE THE TEXT COMES FROM (a trap worth naming). TG_ChatLogManager.ChatLog.Set() accepts a
    /// `dialogue` argument and THROWS IT AWAY - look at the body, it assigns stringId, characterName,
    /// nameColor and characterKey, and never touches `dialogue`. The line itself is recovered later
    /// through GetStoryText(chatLog), which looks the stringId up in the localization dictionary and
    /// runs it through SubstituteVariables + StripTagsCharArray. So we call the GAME's GetStoryText
    /// rather than reading a field: reading the struct directly would yield an empty string for
    /// every entry, and the screen would go silent in a way that looks like a broken hook.
    ///
    /// SPEAKER NAMES ARE NOT chatLog.characterName EITHER. OnFillItem resolves the displayed name
    /// through TG_Static unlocked-character data, showing "????" (or the localized unknownName) for
    /// anyone who has not introduced themselves yet. Speaking the raw characterName would leak
    /// identities the player has not been told - a spoiler the sighted player does not get. We
    /// mirror OnFillItem's resolution so the spoken name matches what is drawn on screen.
    ///
    /// ARROWS ALSO PIXEL-SCROLL, AND THAT IS FINE. KeyboardNav binds the arrow keys onto the
    /// JOYSTICK action set, which is the same set UpdateDefaultChatLog polls, so an arrow press both
    /// steps this cursor and scrolls the view. They are not fighting: the scroll moves what a
    /// sighted onlooker sees, the cursor moves what is spoken. Nothing reads the scrollbar back, so
    /// the two cannot desynchronise into a wrong announcement - the failure mode that made a second
    /// cursor dangerous elsewhere.
    /// </summary>
    [HarmonyPatch]
    public static class ChatLogPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        // Where the player is in the log. -1 means "not positioned yet"; opening the log seeds this
        // to the newest entry, because that is the line they just heard and the natural place to
        // start reading backwards from.
        private static int _cursor = -1;

        // Edge detection for our own stepping. The game's UpdateDefaultChatLog deliberately uses
        // IsPressed (continuous, for smooth scrolling); a cursor must move ONCE per press instead,
        // or one tap would race through the whole log.
        private static bool _upHeld;
        private static bool _downHeld;

        // True while the log is open, so we only consume arrows on that screen.
        private static bool _open;

        /// <summary>
        /// Notices the log opening and reads the newest entry.
        ///
        /// StartOpenChatLog is the single entry point - TG_ControllerInputManager.YButtonPressed
        /// calls it from IN_DIALOGUE, BREWING and the phone states, and the on-screen button calls
        /// it too. Postfixing it covers all of them.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ChatLogManager), nameof(TG_ChatLogManager.StartOpenChatLog))]
        public static void AfterStartOpenChatLog(TG_ChatLogManager __instance)
        {
            try
            {
                // StartOpenChatLog returns early when ingameButtonInteractable is false, without
                // opening anything. Announcing unconditionally here would describe a log that never
                // appeared, so confirm the panel is actually up.
                if (__instance == null || !IsPanelOpen(__instance)) return;

                _open = true;

                int count = CountOf(__instance);
                if (count <= 0)
                {
                    // An empty log is a real state (the log opens before anyone has spoken). Say so
                    // rather than going quiet - silence here is indistinguishable from a broken mod.
                    Announce("Chat log. Empty.");
                    _cursor = -1;
                    return;
                }

                _cursor = count - 1;
                Announce("Chat log, " + count + " " + (count == 1 ? "entry" : "entries")
                    + ". Up and down arrows to read. " + Describe(__instance, _cursor));
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ChatLog] open hook threw: " + e.Message);
            }
        }

        /// <summary>Stops consuming arrows once the log closes.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ChatLogManager), nameof(TG_ChatLogManager.CloseChatLog))]
        public static void AfterCloseChatLog()
        {
            _open = false;
            _upHeld = false;
            _downHeld = false;
            MelonLogger.Msg("[ChatLog] closed.");
        }

        /// <summary>
        /// Steps the cursor. Driven from OnUpdate rather than from a patch on
        /// UpdateDefaultChatLog, so that the cursor keeps working if the game ever routes the log's
        /// update differently - and so a fault here can never throw inside the game's own frame
        /// callback.
        /// </summary>
        internal static void Update()
        {
            if (!_open) return;

            try
            {
                TG_ChatLogManager mgr = ResolveManager();
                if (mgr == null) return;

                // The log can be closed by paths we do not patch (backChatLogButton, Escape via
                // EscapeHandler). Trust the panel, not our own flag.
                if (!IsPanelOpen(mgr))
                {
                    _open = false;
                    return;
                }

                bool up = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W);
                bool down = Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S);

                if (up && !_upHeld) Step(mgr, -1);
                if (down && !_downHeld) Step(mgr, +1);

                _upHeld = up;
                _downHeld = down;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ChatLog] update threw: " + e.Message);
            }
        }

        /// <summary>
        /// Moves the cursor by one entry and speaks it.
        ///
        /// At either end we say so instead of silently refusing. A player who cannot see the
        /// scrollbar has no other way to learn they have reached the start of the conversation, and
        /// an unexplained non-response reads as a broken control.
        /// </summary>
        private static void Step(TG_ChatLogManager mgr, int delta)
        {
            int count = CountOf(mgr);
            if (count <= 0) return;

            if (_cursor < 0) _cursor = count - 1;

            int next = _cursor + delta;
            if (next < 0)
            {
                Announce("Start of log. " + Describe(mgr, 0));
                _cursor = 0;
                return;
            }
            if (next >= count)
            {
                Announce("End of log. " + Describe(mgr, count - 1));
                _cursor = count - 1;
                return;
            }

            _cursor = next;
            Announce(Describe(mgr, _cursor));
        }

        /// <summary>
        /// Renders one entry as "Speaker: line", using the game's own text lookup and its own
        /// name-resolution rules.
        /// </summary>
        private static string Describe(TG_ChatLogManager mgr, int index)
        {
            try
            {
                IList list = ListOf(mgr);
                if (list == null || index < 0 || index >= list.Count) return string.Empty;

                object entry = list[index];
                if (entry == null) return string.Empty;

                string text = GetStoryText(mgr, entry);
                string speaker = ResolveSpeaker(mgr, entry);

                // GetStoryText already strips Fungus braces via StripTagsCharArray, but it
                // deliberately KEEPS <b>/<i>/<color> so the UI can render them. Those would be read
                // aloud as markup, so the text still goes through our cleaner.
                text = FungusText.ExtractWords(text);

                if (string.IsNullOrEmpty(text))
                {
                    // A named entry whose line will not resolve is a gap, and it must sound like
                    // one. Saying just the speaker's name would imply they said nothing.
                    return string.IsNullOrEmpty(speaker)
                        ? "Entry " + (index + 1) + ", text unavailable."
                        : speaker + ", text unavailable.";
                }

                return string.IsNullOrEmpty(speaker) ? text : speaker + ": " + text;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ChatLog] describe threw: " + e.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Calls the game's GetStoryText(ChatLog), which resolves the line from its stringId through
        /// the localization dictionary. See the class comment: the text is NOT stored on the entry.
        /// </summary>
        private static string GetStoryText(TG_ChatLogManager mgr, object entry)
        {
            MethodInfo m = AccessTools.Method(typeof(TG_ChatLogManager), "GetStoryText");
            if (m == null) return string.Empty;
            return m.Invoke(mgr, new[] { entry }) as string ?? string.Empty;
        }

        /// <summary>
        /// Resolves the speaker name the way OnFillItem does, so the spoken name matches the drawn
        /// one - including "????" for a character who has not introduced themselves.
        ///
        /// The unlock check is done through TG_Static by reflection rather than referenced directly,
        /// so that a rename in the retail build degrades to "use the raw name" instead of throwing.
        /// Falling back to characterName can only ever be MORE informative than the screen, never
        /// less, so the failure mode is a mild spoiler rather than silence - but the primary path is
        /// the faithful one.
        /// </summary>
        private static string ResolveSpeaker(TG_ChatLogManager mgr, object entry)
        {
            string rawName = AccessTools.Field(entry.GetType(), "characterName")?.GetValue(entry) as string;
            string key = AccessTools.Field(entry.GetType(), "characterKey")?.GetValue(entry) as string;

            if (string.IsNullOrEmpty(key)) return (rawName ?? string.Empty).Trim();

            try
            {
                Type tgStatic = AccessTools.TypeByName("TG_Static");
                if (tgStatic == null) return (rawName ?? string.Empty).Trim();

                string lowerKey = key.ToLower();

                // Characters absent from allCharacterDataList are the barista (the player) and the
                // narrator - never hidden, always spoken by their own name.
                object all = AccessTools.Field(tgStatic, "allCharacterDataList")?.GetValue(null);
                if (all is IDictionary && !((IDictionary)all).Contains(lowerKey))
                    return (rawName ?? string.Empty).Trim();

                object idxObj = AccessTools.Method(tgStatic, "CheckContainsUnlockedCharacterDataIdx")
                    ?.Invoke(null, new object[] { lowerKey });
                int idx = idxObj is int ? (int)idxObj : -1;

                bool introduced = false;
                if (idx >= 0)
                {
                    object profile = AccessTools.Field(tgStatic, "profileData")?.GetValue(null);
                    object unlockedList = profile == null
                        ? null
                        : AccessTools.Field(profile.GetType(), "UnlockedCharacterDataList")?.GetValue(profile)
                          ?? AccessTools.Property(profile.GetType(), "UnlockedCharacterDataList")?.GetValue(profile, null);

                    IList ul = unlockedList as IList;
                    object data = (ul != null && idx < ul.Count) ? ul[idx] : null;
                    if (data != null)
                    {
                        object flag = AccessTools.Property(data.GetType(), "IntroducedSelf")?.GetValue(data, null)
                                      ?? AccessTools.Field(data.GetType(), "IntroducedSelf")?.GetValue(data);
                        introduced = flag is bool && (bool)flag;
                    }
                }

                if (introduced) return LocalizedName(mgr, lowerKey, known: true) ?? (rawName ?? string.Empty).Trim();

                // Not yet introduced: the screen shows the localized unknown name, or "????".
                return LocalizedName(mgr, lowerKey, known: false) ?? "????";
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ChatLog] speaker resolve threw: " + e.Message);
                return (rawName ?? string.Empty).Trim();
            }
        }

        /// <summary>
        /// Reads the localized character name (or its "unknown" form) via the game's own
        /// GetCharacterNameLocalization. Returns null when unavailable so the caller can fall back.
        /// </summary>
        private static string LocalizedName(TG_ChatLogManager mgr, string lowerKey, bool known)
        {
            try
            {
                object loc = AccessTools.Method(typeof(TG_ChatLogManager), "GetCharacterNameLocalization")
                    ?.Invoke(mgr, new object[] { lowerKey });
                if (loc == null) return null;

                string field = known ? "characterName" : "unknownName";
                string value = AccessTools.Field(loc.GetType(), field)?.GetValue(loc) as string;
                return string.IsNullOrEmpty(value) ? null : value.Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>True when the log's panel is actually on screen.</summary>
        private static bool IsPanelOpen(TG_ChatLogManager mgr)
        {
            try
            {
                TG_ChatLogUI ui = mgr.chatLogUI;
                return ui != null && ui.chatLogUIPanel != null && ui.chatLogUIPanel.activeInHierarchy;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static IList ListOf(TG_ChatLogManager mgr)
        {
            return mgr == null ? null : mgr.chatLogList as IList;
        }

        private static int CountOf(TG_ChatLogManager mgr)
        {
            IList list = ListOf(mgr);
            return list == null ? 0 : list.Count;
        }

        /// <summary>
        /// Resolves the live TG_ChatLogManager through TG_GameManager, which owns it. Reflection
        /// keeps this from becoming a hard dependency on the singleton's shape.
        /// </summary>
        private static TG_ChatLogManager ResolveManager()
        {
            try
            {
                Type gmType = AccessTools.TypeByName("TG_GameManager");
                if (gmType == null) return null;

                Type singleton = AccessTools.TypeByName("TG_GenericSingelton`1");
                if (singleton == null) return null;

                Type closed = singleton.MakeGenericType(gmType);
                object gm = AccessTools.Property(closed, "Instance")?.GetValue(null)
                            ?? AccessTools.Field(closed, "Instance")?.GetValue(null);
                if (gm == null) return null;

                return AccessTools.Field(gmType, "chatLogManager")?.GetValue(gm) as TG_ChatLogManager;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Speaks a chat-log line.
        ///
        /// interrupt:true because stepping is a navigation action - the player who presses Up twice
        /// wants the second entry now, not after the first finishes. TextType.Dialogue so the
        /// backquote repeat key can bring an entry back (SpeechManager stores everything except
        /// System).
        /// </summary>
        private static void Announce(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            ISpeechOutput speech = Speech;
            if (speech == null || !speech.IsAvailable) return;

            MelonLogger.Msg("[ChatLog] " + line);
            speech.SpeakAs(null, line, TextType.Dialogue, true);
        }
    }
}
