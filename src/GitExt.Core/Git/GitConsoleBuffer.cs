using System.Text;

namespace GitExt.Core.Git;

/// <summary>
/// The screen buffer behind the process window — the terminal a running command writes into.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GitOutputText.CleanForDisplay"/> does the same job for a <b>finished</b> command; this
/// one does it <b>chunk by chunk</b>, while the process is still writing. The two cannot be the same
/// code: cleaning is a backwards pass over the whole text, and re-running it on every read of a
/// clone that emits four hundred progress updates is quadratic.
/// </para>
/// <para>
/// 🔴 <b>MEASURED — <c>\r</c> is not a line ending.</b> In a real clone 404 carriage returns were
/// counted against 7 line feeds: git overwrites the same line to animate progress. Treating
/// <c>\r</c> as a line break would turn one progress line into hundreds of rows of scrollback;
/// treating it as an ordinary character would leave the old, longer text visible behind the new one.
/// It is applied the way a terminal applies it — the cursor returns to column zero and what follows
/// overwrites in place.
/// </para>
/// </remarks>
public sealed class GitConsoleBuffer
{
    private const int TrimSlack = 256;

    private readonly List<string> _lines = [];
    private readonly StringBuilder _current = new();
    private readonly int _maximumLines;

    private int _column;
    private bool _inEscape;
    private bool _inControlSequence;

    public GitConsoleBuffer(int maximumLines = GitOutputText.MaximumDisplayLines)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLines, 1);
        _maximumLines = maximumLines;
    }

    /// <summary>How many completed lines were dropped off the top to stay inside the limit.</summary>
    public int DroppedLines { get; private set; }

    /// <summary>
    /// <see langword="false"/> when the last thing written did not finish its line.
    /// </summary>
    /// <remarks>
    /// This is the "the process is waiting for you to type something" signal: git's own prompts
    /// (<c>Username for 'https://…':</c>, <c>Password:</c>, an SSH host-key question) are written
    /// <b>without</b> a trailing newline, because the answer is expected on the same line.
    /// GitExtensions reveals its password box on exactly this condition.
    /// </remarks>
    public bool EndsWithNewLine => _current.Length == 0;

    /// <summary>Writes a piece of raw output into the buffer.</summary>
    public void Append(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (char character in text)
        {
            if (_inEscape)
            {
                // `[` opens a CSI sequence and is NOT its final byte, even though it falls inside
                // the `@`–`~` range that ends one. Treating it as the end lets the parameters of
                // every colour code (`ESC [ 31 m` -> `31m`) through as ordinary text.
                if (!_inControlSequence)
                {
                    _inControlSequence = character == '[';

                    if (!_inControlSequence)
                    {
                        _inEscape = false;
                    }

                    continue;
                }

                if (character is >= '@' and <= '~')
                {
                    _inEscape = false;
                    _inControlSequence = false;

                    // `ESC [ K` (erase to end of line) is the one git actually emits, once per
                    // progress update. Dropping it instead of obeying it leaves the tail of the
                    // PREVIOUS, longer line hanging off the end of the shorter new one.
                    if (character == 'K' && _column < _current.Length)
                    {
                        _current.Length = _column;
                    }
                }

                continue;
            }

            switch (character)
            {
                case '\u001B':
                    _inEscape = true;
                    break;

                case '\n':
                    CommitLine();
                    break;

                case '\r':
                    _column = 0;
                    break;

                case '\f' or '\v':
                    CommitLine();
                    break;

                case '\t':
                    Write(' ');
                    while (_column % 8 != 0)
                    {
                        Write(' ');
                    }

                    break;

                default:
                    if (!char.IsControl(character))
                    {
                        Write(character);
                    }

                    break;
            }
        }
    }

    /// <summary>The text as it should appear on screen.</summary>
    public string GetText()
    {
        if (_lines.Count == 0)
        {
            return _current.ToString().TrimEnd();
        }

        StringBuilder builder = new();

        foreach (string line in _lines)
        {
            builder.Append(line).Append('\n');
        }

        builder.Append(_current);

        return builder.ToString().TrimEnd('\n');
    }

    /// <summary>The last completed (or in-progress) line — what the title bar shows.</summary>
    public string LastLine =>
        _current.Length > 0
            ? _current.ToString().TrimEnd()
            : _lines.Count > 0
                ? _lines[^1]
                : string.Empty;

    public void Clear()
    {
        _lines.Clear();
        _current.Clear();
        _column = 0;
        _inEscape = false;
        _inControlSequence = false;
        DroppedLines = 0;
    }

    private void Write(char character)
    {
        if (_column < _current.Length)
        {
            _current[_column] = character;
        }
        else
        {
            _current.Append(character);
        }

        _column++;
    }

    private void CommitLine()
    {
        _lines.Add(_current.ToString().TrimEnd());
        _current.Clear();
        _column = 0;

        // Without a cap a hook that prints 60,000 lines (measured: 4.1 MB) is held in memory twice
        // over — once here and once in the text box bound to it. Trimmed in batches: removing one
        // line at a time shifts the whole list on every single line past the limit.
        if (_lines.Count > _maximumLines + TrimSlack)
        {
            int excess = _lines.Count - _maximumLines;
            _lines.RemoveRange(0, excess);
            DroppedLines += excess;
        }
    }
}
