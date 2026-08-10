using System;
using MelonLoader;
using UnityAccessibilityLib;

namespace CoffeeTalkAccess.Speech
{
    /// <summary>
    /// Speech channel backed by UnityAccessibilityLib (UniversalSpeech) - talks to the player's
    /// NVDA/JAWS directly via P/Invoke, with a Windows SAPI fallback and braille output.
    ///
    /// Requires the *** 32-BIT *** UniversalSpeech.dll + nvdaControllerClient.dll in the GAME ROOT
    /// (CoffeeTalk.exe is PE32/i386). The build's DeployToGameMods target copies libs\*.dll there.
    /// A 64-bit build throws BadImageFormatException at first P/Invoke; a missing one throws
    /// DllNotFoundException. Both surface here as IsAvailable == false rather than a hard crash.
    /// </summary>
    public sealed class UalAnnouncer : ISpeechOutput
    {
        private readonly bool _initialized;

        public string Name => "UnityAccessibilityLib (UniversalSpeech)";

        public bool IsAvailable => _initialized;

        public UalAnnouncer()
        {
            try
            {
                // Returns false if neither a screen reader nor SAPI could be brought up.
                _initialized = SpeechManager.Initialize();
                MelonLogger.Msg("[Speech] SpeechManager.Initialize() = " + _initialized + ".");
            }
            catch (Exception e)
            {
                MelonLogger.Warning(
                    "[Speech] Initialize threw (x86 UniversalSpeech.dll missing from game root?): " + e.Message);
                _initialized = false;
            }
        }

        public void Speak(string text, bool interrupt = false)
        {
            SpeakAs(null, text, TextType.System, interrupt);
        }

        public void SpeakAs(string speaker, string text, int textType, bool interrupt = false)
        {
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return;
            if (!_initialized) return;

            try
            {
                // SpeechManager has no per-call interrupt flag, so Stop() + Output() is how every
                // caller expresses "cut current speech and say this now".
                if (interrupt) SpeechManager.Stop();

                if (string.IsNullOrEmpty(speaker))
                    SpeechManager.Announce(text, textType);
                else
                    SpeechManager.Output(speaker, text, textType);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Speech] Speak threw: " + e.Message);
            }
        }

        /// <summary>
        /// Re-speaks the stored line.
        ///
        /// The Stop() is deliberate - repeat means "say it again NOW", and without it the repeat
        /// queues behind whatever is still being read. But Stop() before a repeat that turns out to
        /// be EMPTY is actively harmful: SpeechManager.RepeatLast only logs "Nothing to repeat", it
        /// speaks nothing, so the player's keypress silences the line currently being read and
        /// replaces it with silence. A blind player cannot tell that outcome apart from a crashed
        /// mod - and a silent stop is this project's worst failure mode.
        ///
        /// So: check whether there is anything stored FIRST, and if not, say so out loud. The
        /// nothing-to-repeat case is reached through HasStoredLine rather than by catching it after
        /// the fact, because by then the speech has already been cut off.
        /// </summary>
        public void RepeatLast()
        {
            if (!_initialized) return;
            try
            {
                if (!HasStoredLine())
                {
                    // No Stop() on this path - nothing to replace it with.
                    SpeechManager.Announce("Nothing to repeat.", TextType.System);
                    return;
                }

                SpeechManager.Stop();
                SpeechManager.RepeatLast();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Speech] RepeatLast threw: " + e.Message);
            }
        }

        /// <summary>
        /// True if SpeechManager currently holds a line that RepeatLast would actually speak.
        ///
        /// The library exposes no such property, so we read its private `_currentText` field - the
        /// exact value RepeatLast tests. Reflection is the honest option here: the alternative is
        /// mirroring every stored line in our own field, which would be a second copy of state that
        /// can drift out of agreement with the library's (the same two-cursors mistake that retired
        /// MenuCursor). If the field is ever renamed we return true and fall through to the old
        /// behaviour, which is no worse than before this fix.
        /// </summary>
        private static bool HasStoredLine()
        {
            try
            {
                System.Reflection.FieldInfo f = typeof(SpeechManager).GetField(
                    "_currentText",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                if (f == null) return true;

                string stored = f.GetValue(null) as string;
                return !string.IsNullOrEmpty(stored) && stored.Trim().Length > 0;
            }
            catch
            {
                return true;
            }
        }
    }
}
