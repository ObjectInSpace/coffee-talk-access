using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace CoffeeTalkAccess.Dialogue
{
    /// <summary>
    /// H = open the dialog history (the chat log), the keyboard's missing equivalent of the
    /// gamepad's Y / Triangle.
    ///
    /// THE GAP. The chat log has three routes in, and a keyboard player has NONE of them:
    ///     - the gamepad's Y button, via TG_ControllerInputManager.YButtonPressed (:1514-1552)
    ///     - the on-screen chatLogButton, wired in TG_ChatLogManager.Init (:88-94) - a mouse target
    ///     - TG_KeyboardHotkeyManager.HandlerKeyboard, which reads Submit/Confirm/SmartPhoneToggle/
    ///       Escape and NOTHING else.
    /// So the log the mod already knows how to read aloud (ChatLogPatches, a full entry cursor over
    /// the log data) could only be reached with a mouse or a pad. This class supplies the key.
    ///
    /// ⚠ WE CALL THE GAME'S StartOpenChatLog, AND WE MIRROR ITS STATE LIST RATHER THAN INVENTING
    /// ONE. YButtonPressed opens the log from exactly three places - IN_DIALOGUE, BREWING, and the
    /// seven PHONE_* states - and each carries a guard that is NOT incidental:
    ///
    ///   - `currentScene != "EndlessModeScene"` on all three. Endless mode HAS a TG_ChatLogManager
    ///     reference but no story conversation behind it, and TG_GameManager (which owns the
    ///     manager) is not the live singleton in that scene at all - so opening there would either
    ///     do nothing or throw.
    ///   - `!comicPanelActive` on IN_DIALOGUE. A comic panel is a full-screen takeover; the game
    ///     refuses the log underneath it, and so must we.
    ///
    /// Reproducing those conditions by hand would be a second copy of the game's rules, free to
    /// drift. Instead this defers to the game twice over: it checks the same states, then calls
    /// StartOpenChatLog, which applies its OWN final guard (`ingameButtonInteractable`, :157-160)
    /// and simply returns when the log is not allowed. ⚠ That last one is why this method can be
    /// called safely even if the state list above is ever too generous - the game gets the last
    /// word, not us.
    ///
    /// H IS ALSO THE CLOSE KEY, because a player who cannot see the screen needs the key they
    /// pressed to be the key that undoes it. Escape already closes the log (EscapeHandler), but
    /// pressing H twice is the obvious thing to try, and a second press that silently reopened -
    /// or did nothing - would read as a stuck screen.
    ///
    /// WHY H. Unbound everywhere: it appears in neither KeyboardPlayerActions' bindings
    /// (:50-65, which cover Return/Space/Tab/arrows/WASD/E/Q/Escape/Control) nor
    /// InputModuleActionAdapter's, and no Input.GetKey call in the mod or the game reads it. It is
    /// the natural mnemonic for "history", and unlike a function key it is reachable one-handed.
    ///
    /// ⚠ NOT bound onto an action set - read with Input.GetKeyDown, exactly like PhoneBackKey. A
    /// binding would feed HandlerControllerPress, which dispatches on every screen, and the whole
    /// point here is to act on a named few.
    /// </summary>
    [HarmonyPatch]
    public static class ChatLogHotkey
    {
        private const KeyCode HistoryKey = KeyCode.H;

        /// <summary>
        /// Hosted on ControllerUpdateFunction for the reason spelled out in KeyboardNav: it runs
        /// unconditionally from Update() in BOTH input modes, whereas HandlerKeyboard runs only in
        /// KEYBOARD mode - which a connected pad can suppress indefinitely, taking the hotkey with
        /// it. This is the same host MainMenuHotkeys and PhoneBackKey use, and they cannot collide:
        /// different keys, disjoint states.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TG_ControllerInputManager), "ControllerUpdateFunction")]
        public static void AfterControllerUpdate(TG_ControllerInputManager __instance)
        {
            try
            {
                if (__instance == null) return;
                if (!Input.GetKeyDown(HistoryKey)) return;

                object state = AccessTools.Field(__instance.GetType(), "currentState")
                    ?.GetValue(__instance);
                string s = state != null ? state.ToString() : null;
                if (s == null) return;

                // Already open: close it, so H is its own undo. CHAT_LOG is the state the game puts
                // itself in via OpenChatLogGameState, so this needs no panel probe.
                if (s == "CHAT_LOG")
                {
                    TG_ChatLogManager open = ResolveChatLog();
                    if (open == null) return;

                    MelonLogger.Msg("[ChatLog] H -> close.");
                    open.CloseChatLog();
                    return;
                }

                if (!CanOpenFrom(s)) return;

                // ⚠ Endless mode has no story log and a different owning singleton - the same guard
                // the game puts on all three of its own call sites.
                if (IsEndlessScene())
                {
                    MelonLogger.Msg("[ChatLog] H ignored: no story log in endless mode.");
                    return;
                }

                // A comic panel is a full-screen takeover; the game refuses the log under it.
                if (s == "IN_DIALOGUE" && ComicPanelActive())
                {
                    MelonLogger.Msg("[ChatLog] H ignored: comic panel is up.");
                    return;
                }

                TG_ChatLogManager mgr = ResolveChatLog();
                if (mgr == null)
                {
                    MelonLogger.Msg("[ChatLog] H: no chat log manager in this scene.");
                    return;
                }

                MelonLogger.Msg("[ChatLog] H -> open (" + s + ")");

                // The game's own opener. It applies the final ingameButtonInteractable guard itself
                // and returns quietly when the log is not allowed right now; ChatLogPatches'
                // postfix on this same method is what announces the result, so there is deliberately
                // no announcement here. Announcing before the call would describe a log that may
                // never have opened.
                mgr.StartOpenChatLog();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[ChatLog] history key threw: " + e.Message);
            }
        }

        /// <summary>
        /// The states the GAME opens the chat log from, mirrored from YButtonPressed (:1516-1551).
        ///
        /// Kept as an explicit list rather than "any state with a log" because the log is a story
        /// artefact: opening it from a menu or the main screen would announce conversation history
        /// over a screen that has nothing to do with it.
        /// </summary>
        private static bool CanOpenFrom(string state)
        {
            switch (state)
            {
                case "IN_DIALOGUE":
                case "BREWING":
                // The seven phone states YButtonPressed lists. The phone is drawn over the cafe and
                // the log opens on top of both, which is the game's own behaviour here.
                case "PHONE_DRINK":
                case "PHONE_HOME":
                case "PHONE_MUSIC":
                case "PHONE_NEWSPAPER":
                case "PHONE_SOCMED":
                case "PHONE_SOCMED_ACCOUNT":
                case "PHONE_NEWSPAPER_DETAIL":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True in the endless-mode scene, where there is no story conversation to show.
        ///
        /// Read from TG_Static.currentScene - the same value the game's own guards test - by
        /// reflection, so a missing field degrades to "not endless" (i.e. try to open, and let
        /// StartOpenChatLog's own guard decide) rather than throwing.
        /// </summary>
        private static bool IsEndlessScene()
        {
            try
            {
                Type tgStatic = AccessTools.TypeByName("TG_Static");
                if (tgStatic == null) return false;

                string scene = AccessTools.Field(tgStatic, "currentScene")?.GetValue(null) as string;
                return scene == "EndlessModeScene";
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// True while a comic panel is on screen. Bound by string because TG_ComicPanelManager is a
        /// singleton the demo may never instantiate; absent means "no panel", which is correct.
        /// </summary>
        private static bool ComicPanelActive()
        {
            try
            {
                Type t = AccessTools.TypeByName("TG_ComicPanelManager");
                if (t == null) return false;

                UnityEngine.Object mgr = UnityEngine.Object.FindObjectOfType(t);
                if (mgr == null) return false;

                object active = AccessTools.Field(t, "comicPanelActive")?.GetValue(mgr)
                                ?? AccessTools.Property(t, "comicPanelActive")?.GetValue(mgr, null);
                return active is bool && (bool)active;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Resolves the live TG_ChatLogManager through TG_GameManager, which owns it - the same
        /// path ChatLogPatches.ResolveManager takes, with a scene-scan fallback.
        /// </summary>
        private static TG_ChatLogManager ResolveChatLog()
        {
            try
            {
                TG_GameManager gm = TG_GenericSingelton<TG_GameManager>.Instance;
                if (gm != null && gm.chatLogManager != null) return gm.chatLogManager;
            }
            catch (Exception)
            {
                // Singleton not up in this scene - fall through to the scan.
            }

            try
            {
                return UnityEngine.Object.FindObjectOfType<TG_ChatLogManager>();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
