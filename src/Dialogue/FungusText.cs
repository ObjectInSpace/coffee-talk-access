using System.Collections.Generic;
using System.Text;
using Fungus;

namespace CoffeeTalkAccess.Dialogue
{
    /// <summary>
    /// Turns raw Fungus story text into something worth speaking.
    ///
    /// WHY NOT A REGEX: Fungus markup is not just {w}/{wi}. The upstream TextTagParser matches
    /// tags with @"\{.*?\}" and recognizes ~28 token types, many parameterized -
    /// {w=0.5} {wi} {wc} {wvo} {wop=0.2} {/wop} {c} {x} {s=40} {/s} {b} {i} {color=#fff}
    /// {size=30} {link=X} {m=Msg} {vp=x,y} {hp=x,y} {p=x,y} {f=0.5} {audio=Clip} {audioloop=X}
    /// {audiopause=X} {audiostop=X}. A hand-written subset regex silently leaks the rest into
    /// speech ("audio equals Clip"). This game ships the stock parser (Fungus.TextTagParser is
    /// public), so we tokenize with the GAME'S OWN parser and keep only TokenType.Words. That is
    /// correct by construction and cannot drift from whatever the writer actually understands.
    ///
    /// TextMeshPro rich text (&lt;color&gt;, &lt;size&gt;, &lt;b&gt;) can also appear inline; the Words
    /// payload is handed to UnityAccessibilityLib's TextCleaner, which strips Unity rich text
    /// and normalizes whitespace.
    /// </summary>
    public static class FungusText
    {
        /// <summary>
        /// Extracts only the spoken words from Fungus story text, dropping every control tag.
        /// Returns an empty string when the line is pure markup (e.g. a bare "{c}").
        /// </summary>
        public static string ExtractWords(string storyText)
        {
            if (string.IsNullOrEmpty(storyText)) return string.Empty;

            StringBuilder sb = new StringBuilder(storyText.Length);
            try
            {
                List<TextTagToken> tokens = TextTagParser.Tokenize(storyText);
                if (tokens != null)
                {
                    for (int i = 0; i < tokens.Count; i++)
                    {
                        TextTagToken t = tokens[i];
                        if (t == null || t.type != TokenType.Words) continue;
                        if (t.paramList == null || t.paramList.Count == 0) continue;
                        sb.Append(t.paramList[0]);
                    }
                }
            }
            catch
            {
                // If the parser ever throws on malformed input, fall back to the raw text rather
                // than going silent - a line with tag noise still beats no line at all for a
                // player who cannot see it.
                sb.Length = 0;
                sb.Append(storyText);
            }

            return Collapse(sb.ToString());
        }

        /// <summary>
        /// Collapses runs of whitespace (including the newlines Fungus uses for layout) into
        /// single spaces so speech does not stutter on wrapped lines.
        /// </summary>
        private static string Collapse(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            StringBuilder sb = new StringBuilder(value.Length);
            bool lastWasSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isSpace = char.IsWhiteSpace(c);
                if (isSpace)
                {
                    if (!lastWasSpace && sb.Length > 0) sb.Append(' ');
                }
                else
                {
                    sb.Append(c);
                }
                lastWasSpace = isSpace;
            }

            return sb.ToString().Trim();
        }
    }
}
