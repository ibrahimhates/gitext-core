using GitExt.Core;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Faz 07'nin servisleri (P07-T03 … P07-T21).
/// </summary>
/// <remarks>
/// <para>
/// Neden demet? <see cref="MainWindowViewModel"/>'in kurucusu Faz 06 sonunda 20 isteğe
/// bağlı parametreye ulaşmıştı; Faz 07'nin sekiz servisini de tek tek eklemek onu
/// okunamaz hale getirirdi. Bunlar <b>birlikte</b> anlamlı bir küme: hepsi "ileri
/// operasyonlar" ekranlarını besliyor.
/// </para>
/// <para>
/// Hepsi isteğe bağlı: testler yalnızca ilgilendikleri servisi veriyor, gerisi
/// <see langword="null"/> kalıyor ve ilgili komut devre dışı görünüyor.
/// </para>
/// </remarks>
public sealed record AdvancedOperationServices
{
    public IConflictReader? Conflicts { get; init; }

    public IConflictResolver? Resolver { get; init; }

    public IMergeToolRunner? MergeTools { get; init; }

    public IResetWriter? Reset { get; init; }

    public ISequencerWriter? Sequencer { get; init; }

    public IRebaseWriter? Rebase { get; init; }

    public IStashWriter? Stash { get; init; }

    public IReflogReader? Reflog { get; init; }

    public IBlameReader? Blame { get; init; }

    public IFileHistoryReader? FileHistory { get; init; }

    public ITagWriter? Tags { get; init; }

    public IWorkTreeReader? WorkTrees { get; init; }

    public ISubmoduleReader? Submodules { get; init; }

    public ISearchReader? Search { get; init; }
}
