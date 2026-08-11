using CoffeeTalkAccess.Speech;
using MelonLoader;
using UnityAccessibilityLib;

namespace CoffeeTalkAccess.Dialogue
{
    /// <summary>
    /// The player's on/off switch for AUTOMATIC story narration (Shift+BackQuote, i.e. "~").
    ///
    /// WHY THIS EXISTS: Coffee Talk's story lines are also VOICED-adjacent content a returning
    /// player may already know, and a replay with the screen reader talking over every line is
    /// worse than silence. This turns that off without unloading the mod.
    ///
    /// SCOPE: the in-game Say lines ONLY - SayPatches is the single consumer of this gate.
    ///
    /// WHAT IT DOES *NOT* SILENCE - deliberately, and this is the load-bearing part:
    ///  - the OPENING CUTSCENE (CutscenePatches). Its lines run on a timer and then vanish, so a
    ///    muted cutscene is content the player has no other route back to. Say lines are the
    ///    opposite: the player advances them, and the chat log can re-read them.
    ///  - menu focus, brewing narration, pop-ups, phone screens. Silencing those would take away
    ///    the way BACK OUT of wherever the player is, and a blind player who toggled by accident
    ///    would have no way to find the toggle again.
    ///  - the chat log (Up/Down stepping). That is the player ASKING for a line, not the game
    ///    pushing one; a request must always be answered or the key reads as broken.
    ///  - the repeat key. Same reason: it is a request.
    ///
    /// The state change itself always speaks, via Speak (TextType.System) rather than the gated
    /// path - otherwise turning narration off would produce silence indistinguishable from a
    /// crashed mod, which is the worst failure mode this project has.
    /// </summary>
    public static class DialogueToggle
    {
        /// <summary>True when automatic story narration should be spoken. On by default.</summary>
        public static bool Enabled { get; private set; } = true;

        /// <summary>
        /// Flips the gate and announces the new state.
        ///
        /// The announcement is TextType.System so it is not stored for the repeat key: it is mod
        /// chatter, and letting it overwrite the stored story line would mean toggling narration
        /// back on destroyed the line the player wanted to hear again.
        /// </summary>
        public static void Toggle()
        {
            Enabled = !Enabled;

            MelonLogger.Msg("[Toggle] Dialogue narration " + (Enabled ? "ON" : "OFF"));

            ISpeechOutput speech = AccessMod.Speech;
            if (speech == null || !speech.IsAvailable) return;

            // interrupt:true - if narration was just switched off, the line already in flight is
            // exactly what the player is trying to stop, so it must be cut rather than queued
            // behind.
            speech.SpeakAs(null,
                Enabled ? "Dialogue reading on." : "Dialogue reading off.",
                TextType.System, true);
        }
    }
}
