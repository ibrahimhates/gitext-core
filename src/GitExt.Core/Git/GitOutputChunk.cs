namespace GitExt.Core.Git;

/// <summary>
/// A piece of a running command's output, delivered <b>while the process is still alive</b>.
/// </summary>
/// <param name="Text">
/// The raw text exactly as git wrote it. Line endings are <b>not</b> normalised: progress uses
/// <c>\r</c> to overwrite a line and the consumer has to be able to tell the two apart.
/// </param>
/// <param name="IsError">Did the piece come from <c>stderr</c>?</param>
/// <remarks>
/// 🔴 <b>MEASURED</b> — without this, a process window is impossible. <c>RunAsync</c> hands the
/// caller the whole output at once when the process exits: with a <c>pre-commit</c> hook printing
/// one line a second for five seconds, every line arrived at <c>+5061 ms</c>, while a terminal
/// running the same command saw them at 1-second intervals. A window fed from the return value
/// would sit empty for the whole operation and then fill up after it no longer matters.
/// </remarks>
public readonly record struct GitOutputChunk(string Text, bool IsError);
