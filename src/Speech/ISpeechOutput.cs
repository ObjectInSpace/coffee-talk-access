namespace CoffeeTalkAccess.Speech
{
    /// <summary>
    /// Thin output seam over whatever screen-reader channel is active. The rest of the mod
    /// (dialogue hooks, brewing narration) talks only to this interface, so switching the
    /// channel is a configuration change, not a rewrite. Exactly one implementation is live
    /// at a time - this is not a hybrid runtime.
    /// </summary>
    public interface ISpeechOutput
    {
        /// <summary>True if this channel believes it can currently deliver speech.</summary>
        bool IsAvailable { get; }

        /// <summary>Speak a line. <paramref name="interrupt"/> cuts off current speech first.</summary>
        void Speak(string text, bool interrupt = false);

        /// <summary>
        /// Speak a line attributed to a speaker. The channel decides how to render attribution
        /// (UnityAccessibilityLib prefixes "Speaker: text" for Dialogue-type output).
        /// </summary>
        void SpeakAs(string speaker, string text, int textType, bool interrupt = false);

        /// <summary>Re-speak the last stored dialogue/narrator line.</summary>
        void RepeatLast();

        /// <summary>Human-readable channel name for logs/diagnostics.</summary>
        string Name { get; }
    }
}
