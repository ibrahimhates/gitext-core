namespace GitExt.Core.Model;

/// <summary>
/// Keşfedilmiş bir Git deposunun konum bilgisi.
/// </summary>
/// <remarks>
/// Bu tipin bir örneği varsa, yolun gerçekten bir Git deposu olduğu doğrulanmış demektir.
/// </remarks>
public sealed class RepositoryLocation
{
    internal RepositoryLocation(
        string gitDirectory,
        string commonDirectory,
        string? workTreeRoot,
        string? superprojectWorkTree)
    {
        GitDirectory = gitDirectory;
        CommonDirectory = commonDirectory;
        WorkTreeRoot = workTreeRoot;
        SuperprojectWorkTree = superprojectWorkTree;
    }

    /// <summary>
    /// Bu çalışma ağacına ait git dizini — <c>HEAD</c> ve <c>index</c> burada.
    /// </summary>
    /// <remarks>
    /// Bağlı (linked) bir worktree'de bu <c>&lt;ana&gt;/.git/worktrees/&lt;ad&gt;</c> olur,
    /// <see cref="CommonDirectory"/> ile aynı değildir.
    /// </remarks>
    public string GitDirectory { get; }

    /// <summary>
    /// Paylaşılan git dizini — <b>ref'ler, nesneler ve config burada</b>.
    /// </summary>
    /// <remarks>
    /// Normal bir depoda <see cref="GitDirectory"/> ile aynıdır. Worktree'lerde farklıdır ve
    /// ref/nesne okuyan her şey <b>bunu</b> kullanmalıdır — worktree'ye özel dizini değil.
    /// </remarks>
    public string CommonDirectory { get; }

    /// <summary>
    /// Çalışma ağacının kökü. Bare depoda <see langword="null"/>.
    /// </summary>
    public string? WorkTreeRoot { get; }

    /// <summary>
    /// Bu depo bir submodule ise üst projenin çalışma ağacı; değilse <see langword="null"/>.
    /// </summary>
    public string? SuperprojectWorkTree { get; }

    /// <summary>Çalışma ağacı olmayan (bare) depo mu?</summary>
    public bool IsBare => WorkTreeRoot is null;

    /// <summary>
    /// Bağlı bir worktree mi (<c>git worktree add</c> ile oluşturulmuş)?
    /// </summary>
    public bool IsLinkedWorkTree =>
        !string.Equals(GitDirectory, CommonDirectory, StringComparison.Ordinal);

    /// <summary>Bu depo bir submodule mü?</summary>
    public bool IsSubmodule => SuperprojectWorkTree is not null;

    /// <summary>
    /// Komutların çalıştırılacağı dizin.
    /// </summary>
    /// <remarks>
    /// Çalışma ağacı varsa onun kökü, bare depoda git dizininin kendisi.
    /// </remarks>
    public string WorkingDirectory => WorkTreeRoot ?? GitDirectory;

    public override string ToString() => WorkingDirectory;
}
