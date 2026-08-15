using System;
using CoffeeTalkAccess.Dialogue;
using CoffeeTalkAccess.Speech;
using HarmonyLib;
using MelonLoader;
using TMPro;
using UnityAccessibilityLib;
using UnityEngine;

namespace CoffeeTalkAccess.Brewing
{
    /// <summary>
    /// R = re-read the current drink request while brewing.
    ///
    /// THE GAP, and it is a memory problem rather than a missing announcement. The customer asks
    /// for their drink as a normal line of dialogue, which the mod already speaks - and then the
    /// brew screen opens and that line stays on screen for the whole brew, pinned in a chat balloon
    /// beside the glass (TG_DrinkManager:1453, `SetChatBallonDialogue(GetLastChat())`). A sighted
    /// player re-reads it as often as they like while choosing ingredients. A blind player heard it
    /// once, several seconds and one screen transition ago, and Coffee Talk's puzzles turn on the
    /// exact wording ("something warm, and not too sweet").
    ///
    /// This is the same reasoning that made F9 a QUERY key for the stats rather than an automatic
    /// re-read: the information is permanently visible to a sighted player, so the accessible
    /// equivalent is answering on demand, not repeating unprompted. See StatsPatches' header.
    ///
    /// ⚠ READ THE BALLOON, DO NOT RECONSTRUCT THE ORDER. The obvious-looking alternative is to
    /// resolve what the customer actually wants from TG_DialogueManager's `_ruleList` - the
    /// TG_DrinkBranchingRule list the brewing command installs (TG_BrewingCommand:26). Do NOT: that
    /// is the ANSWER KEY, not the request. Those rules are what CheckDrink grades the finished
    /// drink against (TG_DialogueManager:154-163), so speaking them would tell the player the exact
    /// ingredients to use - turning every brewing puzzle into a dictation exercise and removing the
    /// thing the sighted player is actually doing. The balloon holds what the customer SAID, which
    /// is precisely what is on screen and precisely what the puzzle gives you.
    ///
    /// ⚠ THE BALLOON IS NOT A STORED "ORDER" FIELD EITHER - there is no such thing. In the story
    /// scene it is literally the last chat-log line (GetLastChat, :1516-1521); in endless mode
    /// TG_EndlessModeDrinkManager writes a generated request into the SAME field
    /// (`SetChatBallonDialogue`, :451/:463/:513/:525). Reading `chatBallonBrewingText` therefore
    /// covers both modes with one implementation and cannot drift from what is drawn, because it IS
    /// what is drawn.
    ///
    /// WHY R. Unbound by the game (KeyboardPlayerActions binds Return/Space/Tab/arrows/WASD/E/Q/
    /// Escape/Control, :50-65) and by the mod. "R" for request, and it sits under the same hand as
    /// the arrow keys the player is navigating ingredients with.
    /// </summary>
    [HarmonyPatch]
    public static class RequestPatches
    {
        private static ISpeechOutput Speech => AccessMod.Speech;

        /// <summary>
        /// Speaks the request currently pinned beside the glass.
        ///
        /// Called from the F9-style query path in AccessMod.OnUpdate rather than hosted on a
        /// patch, because it answers a question rather than reacting to a game event.
        /// </summary>
        internal static void SpeakCurrentRequest()
        {
            try
            {
                TG_DrinkManager mgr = FindDrinkManager();
                if (mgr == null)
                {
                    // Naming the gap rather than staying silent, so a wrong-screen press is
                    // diagnosable by ear instead of by log - the StatsPatches convention.
                    Announce("No drink request here.");
                    return;
                }

                string request = ReadBalloon(mgr);

                // ⚠ Fungus markup and the game's own <color>/<b> tags would otherwise be read out
                // as literal angle brackets. Same cleaner the chat log and dialogue paths use.
                request = FungusText.ExtractWords(request ?? string.Empty);

                if (string.IsNullOrEmpty(request))
                {
                    // A real state, not a failure: the balloon is empty on the free-brew mode
                    // (TG_EndlessModeDrinkManager:423 sets it to "") and between requests. Saying so
                    // beats silence, which is indistinguishable from a dead key.
                    Announce("No drink request right now.");
                    return;
                }

                Announce(request);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Request] query threw: " + e.Message);
                Announce("Could not read the drink request.");
            }
        }

        /// <summary>
        /// Reads the chat balloon's text off the manager.
        ///
        /// `chatBallonBrewingText` is a PUBLIC TextMeshProUGUI field on TG_DrinkManager (:131) and
        /// is inherited by TG_EndlessModeDrinkManager, so one read covers both modes. Fetched by
        /// reflection anyway, so a rename degrades to "no request" rather than throwing inside a
        /// keypress handler.
        /// </summary>
        private static string ReadBalloon(TG_DrinkManager mgr)
        {
            try
            {
                object field = AccessTools.Field(typeof(TG_DrinkManager), "chatBallonBrewingText")
                    ?.GetValue(mgr);

                TextMeshProUGUI label = field as TextMeshProUGUI;
                if (label != null) return label.text;

                // Defensive: if the field type ever changes, still try for a `text` member rather
                // than reporting nothing.
                if (field != null)
                    return AccessTools.Property(field.GetType(), "text")?.GetValue(field, null) as string;

                return null;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Request] balloon read threw: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Resolves the live drink manager, preferring the story singleton and falling back to a
        /// scene scan so endless mode (a different owning manager) is covered too. Mirrors
        /// StatsPatches.FindDrinkManager deliberately - the two query keys answer about the same
        /// screen and must never disagree about which manager that is.
        /// </summary>
        private static TG_DrinkManager FindDrinkManager()
        {
            try
            {
                TG_GameManager gm = TG_GenericSingelton<TG_GameManager>.Instance;
                if (gm != null && gm.drinkManager != null) return gm.drinkManager;
            }
            catch (Exception)
            {
                // Singleton not up in this scene - fall through.
            }

            try
            {
                return UnityEngine.Object.FindObjectOfType<TG_DrinkManager>();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Speaks the request line.
        ///
        /// TextType.Dialogue, not Menu: this is the customer's own words being replayed, so it
        /// should be storable by the backquote repeat key alongside the rest of the conversation.
        /// interrupt:true because a query answers now - the player pressed a key to hear this
        /// instead of whatever is currently being read.
        /// </summary>
        private static void Announce(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            ISpeechOutput speech = Speech;
            if (speech == null || !speech.IsAvailable) return;

            MelonLogger.Msg("[Request] " + line);
            speech.SpeakAs(null, line, TextType.Dialogue, true);
        }
    }
}
