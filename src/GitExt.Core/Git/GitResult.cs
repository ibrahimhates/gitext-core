using System.Text;

namespace GitExt.Core.Git;

/// <summary>
/// The result of a completed <c>git</c> call.
/// </summary>
/// <remarks>
/// stdout is kept as <b>raw bytes</b>, not as a <see cref="string"/>: file names may not be valid
/// UTF-8, and commands such as <c>git show</c> can return binary content. When text is needed,
/// <see cref="GetStandardOutputText"/> is used.
/// </remarks>
public sealed class GitResult
{
    public GitResult(
        GitCommand command,
        int exitCode,
        byte[] standardOutput,
        string standardError,
        TimeSpan duration,
        bool outputTruncated = false)
    {
        Command = command;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        Duration = duration;
        OutputTruncated = outputTruncated;
    }

    /// <summary>
    /// Did the output hit the <see cref="GitCommand.MaximumOutputBytes"/> limit?
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/>, <see cref="StandardOutput"/> is <b>incomplete</b>; parsing it
    /// silently produces missing data. The caller has to handle this case explicitly.
    /// </remarks>
    public bool OutputTruncated { get; }

    /// <summary>The command that was run.</summary>
    public GitCommand Command { get; }

    /// <summary>The process's exit code.</summary>
    public int ExitCode { get; }

    /// <summary>The raw stdout content.</summary>
    public byte[] StandardOutput { get; }

    /// <summary>The stderr content. Git writes progress information here too, so it can be non-empty without an error.</summary>
    public string StandardError { get; }

    /// <summary>The time elapsed from the start of the process to its end.</summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Is the exit code one of the success codes the command declared?
    /// </summary>
    /// <remarks>
    /// 🔴 A <b>truncated</b> output counts as success. The exit code says nothing there: reaching
    /// the limit is OUR decision, and the process is killed for it — so what comes back is a
    /// termination code, not git's verdict. Reading it literally would turn the size guard
    /// (a protection) into an error, and the caller would never see the partial file list it is
    /// entitled to. Truncation is signalled by <see cref="OutputTruncated"/>, which every caller
    /// must handle anyway.
    /// </remarks>
    public bool IsSuccess => OutputTruncated || Command.SuccessExitCodes.Contains(ExitCode);

    /// <summary>
    /// Returns stdout as UTF-8 text.
    /// </summary>
    /// <remarks>
    /// Invalid bytes are replaced with U+FFFD — throwing an exception over a file name in a broken
    /// encoding is worse than not showing that file at all.
    /// </remarks>
    public string GetStandardOutputText() =>
        StandardOutput.Length == 0 ? string.Empty : _utf8Lenient.GetString(StandardOutput);

    /// <summary>
    /// Converts stdout to text <b>losslessly</b>: every byte maps one to one onto a character.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>git diff</c> output is <b>not in a single encoding</b>: the headers and markers are ASCII
    /// while the line contents are <b>the file's own bytes</b>. Decoding it as UTF-8 <b>silently
    /// corrupts</b> the content of a file that is not UTF-8 (measured: in a Latin-5 file the
    /// <c>0xFC</c> bytes become U+FFFD).
    /// </para>
    /// <para>
    /// Decoding with Latin-1 preserves every byte; because the structure is ASCII the parsing is
    /// unaffected, and the content can be <b>re-decoded</b> later with the right encoding. This
    /// approach was taken from GitExtensions' <c>PatchProcessor</c>: there too the output is read
    /// losslessly and the headers and the content are re-encoded <b>separately</b>.
    /// </para>
    /// </remarks>
    public string GetStandardOutputLossless() =>
        StandardOutput.Length == 0 ? string.Empty : Encoding.Latin1.GetString(StandardOutput);

    /// <summary>
    /// Splits stdout on the NUL (<c>\0</c>) separator; <b>dropping empty parts</b>.
    /// </summary>
    /// <remarks>
    /// Suitable only for outputs where every part is known to be non-empty
    /// (<c>ls-files -z</c>, for instance).
    /// <para>
    /// ⚠️ <b>Do not use</b> it to parse fixed-field records: when an empty field (a commit with no
    /// body, say) is dropped, every following field shifts and the data is silently wrong. Use
    /// <see cref="SplitStandardOutputAtNulPreservingEmpty"/> in that case.
    /// </para>
    /// </remarks>
    public string[] SplitStandardOutputAtNul()
    {
        string text = GetStandardOutputText();
        return text.Length == 0
            ? []
            : text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Splits stdout on the NUL separator; <b>keeping empty parts</b>.
    /// </summary>
    /// <remarks>
    /// Required to keep fixed-field records aligned. Only the empty part arising from the separator at
    /// the very end of the stream is dropped — <c>git log -z</c> puts a NUL after the last record too.
    /// </remarks>
    public string[] SplitStandardOutputAtNulPreservingEmpty()
    {
        string text = GetStandardOutputText();

        if (text.Length == 0)
        {
            return [];
        }

        // The trailing separator produces an artificial empty part; drop that one and keep the others.
        if (text[^1] == '\0')
        {
            text = text[..^1];
        }

        return text.Split('\0');
    }

    private static readonly UTF8Encoding _utf8Lenient = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);
}
