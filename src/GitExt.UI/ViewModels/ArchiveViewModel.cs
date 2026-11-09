using CommunityToolkit.Mvvm.ComponentModel;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

public sealed partial class ArchiveViewModel : ViewModelBase
{
    private readonly string _workingDirectory;
    private readonly IArchiveWriter _writer;

    public ArchiveViewModel(string workingDirectory, IArchiveWriter writer, CommitInfo revision, CommitInfo? diffRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(revision);

        _workingDirectory = workingDirectory;
        _writer = writer;
        Revision = revision;
        DiffRevision = diffRevision;
    }

    public CommitInfo Revision { get; }

    public CommitInfo? DiffRevision { get; }

    public string RevisionText => $"{Revision.Id.ToShortString()} {Revision.Subject}";

    public string DiffRevisionText => DiffRevision is { } diff ? $"{diff.Id.ToShortString()} {diff.Subject}" : "...";

    public bool HasDiffRevision => DiffRevision is not null;

    [ObservableProperty]
    public partial bool FilterPaths { get; set; }

    [ObservableProperty]
    public partial string PathsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ChangedOnly { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTar), nameof(Extension), nameof(DefaultFileName))]
    public partial bool IsZip { get; set; } = true;

    public bool IsTar
    {
        get => !IsZip;
        set => IsZip = !value;
    }

    public string Extension => IsZip ? "zip" : "tar";

    [ObservableProperty]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    public string DefaultFileName => $"{Revision.Id.ToShortString()}.{Extension}";

    public ArchiveOptions BuildOptions(string outputPath) => new()
    {
        Revision = Revision.Id.ToString(),
        OutputPath = outputPath,
        Format = IsZip ? ArchiveFormat.Zip : ArchiveFormat.Tar,
        Paths = FilterPaths
            ? [.. PathsText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [],
        ChangedSince = ChangedOnly && DiffRevision is { } diff ? diff.Id.ToString() : null,
    };

    public async Task<bool> ArchiveAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        Error = null;
        IsBusy = true;

        try
        {
            await _writer.ArchiveAsync(_workingDirectory, BuildOptions(outputPath), cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch (NothingToArchiveException)
        {
            Error = Loc.T("archive.no_changed_files");
        }
        catch (GitException error)
        {
            Error = Loc.GitError(error);
        }
        catch (IOException error)
        {
            Error = error.Message;
        }
        catch (UnauthorizedAccessException error)
        {
            Error = error.Message;
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }
}

public interface IArchivePrompt
{
    Task ShowAsync(ArchiveViewModel model);
}
