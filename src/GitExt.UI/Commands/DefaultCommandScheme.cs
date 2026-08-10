using Avalonia.Input;

namespace GitExt.UI.Commands;

/// <summary>
/// Varsayılan komut ve kısayol şeması (P08-T02).
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Bu şema <c>v1.0.0</c> ile DONAR</b> (ADR-0006): sonrasında bir kısayolu değiştirmek
/// MAJOR sürüm gerektirir. Bu yüzden her atama ya GitExtensions'tan geliyor ya da sapma
/// olarak <b>gerekçesiyle</b> yazılı.
/// </para>
/// <para>
/// <b>Kaynak:</b> GitExtensions <c>src/app/GitUI/Hotkey/HotkeySettingsManager.cs</c> →
/// <c>CreateDefaultSettings()</c>. Orada kısayollar forma/kontrole göre gruplanmış
/// (<c>FormBrowse</c>, <c>RevisionGridControl</c>, <c>FileViewer</c>,
/// <c>RevisionDiffControl</c>, <c>RepoObjectsTree</c>) — bizim
/// <see cref="CommandContext"/>'imizin karşılığı bu. Aynı jestin iki grupta farklı iş
/// yapması orada da olağan (<c>Ctrl+D</c>, <c>S</c>, <c>F5</c>), dolayısıyla
/// "farklı bağlam çakışma değildir" kuralı GitExtensions'ta da geçerli.
/// </para>
/// <para>
/// <b>Bilinçli sapmalar</b> (körü körüne kopyalanmadı):
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Diff'te üç seviyeli gezinme.</b> GitExtensions'ın <c>FileViewer</c>'ında yalnızca
///     "sonraki değişiklik" var. Bizde değişiklik / hunk / dosya ayrı ayrı geziliyor:
///     <c>Alt+↓↑</c> değişiklik (GitExtensions'la aynı), <c>Ctrl+PgDn/PgUp</c> hunk,
///     <c>Alt+→←</c> dosya. Üstkümeyiz; GitExtensions'ın <c>Alt+→←</c>'i
///     (<c>NextOccurrence</c>) bizde yok, o yüzden boşta.
///   </item>
///   <item>
///     <b>Commit listesinde arama <c>Ctrl+F</c>.</b> GitExtensions'ta SHA'ya atlama
///     <c>Ctrl+Shift+G</c>. <c>Ctrl+F</c> "bul" anlamında evrensel ve plan onu açıkça
///     "tanıdık olanı koru" listesine almış.
///   </item>
///   <item>
///     <b>Stage/unstage tümü <c>Ctrl+Shift+S</c> / <c>Ctrl+Shift+U</c>.</b> GitExtensions
///     <c>FormBrowse</c>'da <c>Ctrl+Space</c>'i <i>commit ekranını aç</i> için kullanıyor;
///     o daha sık bir işlem, bu yüzden <c>Ctrl+Space</c> ona verildi.
///   </item>
///   <item>
///     <b><c>Ctrl+Q</c> çıkış.</b> GitExtensions'ta yok; Linux masaüstlerinde standart.
///   </item>
///   <item>
///     <b><c>Ctrl+Shift+P</c> komut paleti</b> ve <b><c>F1</c> kısayol listesi</b>: GitExtensions'ta
///     karşılığı yok, ikisi de bu projenin eklediği keşfedilebilirlik araçları.
///     <c>F6</c> panel gezinmesi de öyle — P08-T00/M09'da ölçüldü, <c>F6</c> Avalonia'da boşta.
///   </item>
/// </list>
/// <para>
/// <b>Kasıtlı olarak varsayılansız</b> bırakılanlar (menüden ve paletten erişilir): yıkıcı ya da
/// nadir işlemler — cherry-pick, revert, reset, dal silme, uzak depo yönetimi. Yıkıcı bir
/// işlemi yanlışlıkla basılan bir tuşa bağlamak, geri alma yolunu her seferinde sınamak
/// demekti.
/// </para>
/// </remarks>
public static class DefaultCommandScheme
{
    private static readonly CommandDefinition[] All =
    [
        // ------------------------------------------------------------------ Global
        Define(CommandIds.RepositoryOpen, "Depo aç…", CommandCategory.Repository,
            CommandContext.Global, Key.O, KeyModifiers.Control),
        Define(CommandIds.RepositoryClose, "Depoyu kapat", CommandCategory.Repository,
            CommandContext.Global, Key.W, KeyModifiers.Control),
        Define(CommandIds.RepositoryRefresh, "Yenile", CommandCategory.Repository,
            CommandContext.Global, Key.F5),
        Define(CommandIds.RepositoryRemotes, "Uzak depolar…", CommandCategory.Remote,
            CommandContext.Global, gesture: null),

        Define(CommandIds.CommitShow, "Commit…", CommandCategory.Commit,
            CommandContext.Global, Key.Space, KeyModifiers.Control),

        Define(CommandIds.RemotePull, "Pull/Fetch…", CommandCategory.Remote,
            CommandContext.Global, Key.Down, KeyModifiers.Control),
        Define(CommandIds.RemotePush, "Push…", CommandCategory.Remote,
            CommandContext.Global, Key.Up, KeyModifiers.Control),

        Define(CommandIds.BranchCreate, "Dal oluştur…", CommandCategory.Branch,
            CommandContext.Global, Key.B, KeyModifiers.Control),
        Define(CommandIds.BranchDelete, "Dal sil…", CommandCategory.Branch,
            CommandContext.Global, gesture: null),
        Define(CommandIds.BranchCheckout, "Dala geç (checkout)…", CommandCategory.Branch,
            CommandContext.Global, Key.OemPeriod, KeyModifiers.Control),
        Define(CommandIds.BranchMerge, "Dalları birleştir…", CommandCategory.Branch,
            CommandContext.Global, Key.M, KeyModifiers.Control),

        Define(CommandIds.HistoryRebase, "Rebase…", CommandCategory.History,
            CommandContext.Global, Key.E, KeyModifiers.Control | KeyModifiers.Shift),
        Define(CommandIds.HistoryCherryPick, "Cherry pick…", CommandCategory.History,
            CommandContext.Global, gesture: null),
        Define(CommandIds.HistoryRevert, "Commit'i geri al (revert)…", CommandCategory.History,
            CommandContext.Global, gesture: null),
        Define(CommandIds.HistoryReset, "Bu commit'e sıfırla…", CommandCategory.History,
            CommandContext.Global, gesture: null),
        Define(CommandIds.HistoryReflog, "Reflog'u göster…", CommandCategory.History,
            CommandContext.Global, Key.L, KeyModifiers.Control | KeyModifiers.Shift),
        Define(CommandIds.TagCreate, "Etiket oluştur…", CommandCategory.History,
            CommandContext.Global, Key.T, KeyModifiers.Control),
        Define(CommandIds.StashManage, "Stash'leri yönet…", CommandCategory.History,
            CommandContext.Global, Key.S, KeyModifiers.Control | KeyModifiers.Alt),

        Define(CommandIds.ViewToggleLeftPanel, "Sol paneli aç/kapat", CommandCategory.View,
            CommandContext.Global, Key.C, KeyModifiers.Control | KeyModifiers.Alt),
        Define(CommandIds.ViewToggleBottomPanel, "Alt paneli aç/kapat", CommandCategory.View,
            CommandContext.Global, Key.D, KeyModifiers.Control | KeyModifiers.Alt),
        Define(CommandIds.ViewFocusLeftPanel, "Sol panele odaklan", CommandCategory.Navigation,
            CommandContext.Global, Key.D0, KeyModifiers.Control),
        Define(CommandIds.ViewFocusCommitList, "Commit listesine odaklan", CommandCategory.Navigation,
            CommandContext.Global, Key.D1, KeyModifiers.Control),
        Define(CommandIds.ViewFocusCommitDetails, "Commit detayına odaklan", CommandCategory.Navigation,
            CommandContext.Global, Key.D2, KeyModifiers.Control),
        Define(CommandIds.ViewFocusDiff, "Diff'e odaklan", CommandCategory.Navigation,
            CommandContext.Global, Key.D3, KeyModifiers.Control),
        Define(CommandIds.ViewNextPanel, "Sonraki panel", CommandCategory.Navigation,
            CommandContext.Global, Key.F6),
        Define(CommandIds.ViewPreviousPanel, "Önceki panel", CommandCategory.Navigation,
            CommandContext.Global, Key.F6, KeyModifiers.Shift),

        Define(CommandIds.ToolsCommandLog, "Git komut günlüğü…", CommandCategory.Tools,
            CommandContext.Global, Key.D9, KeyModifiers.Control),
        // Ctrl+Shift+F12: teşhis paneli (P09-T03). Menüde yok, bilerek — kısayol ve komut
        // paleti yeterli erişim, günlük kullanımda görünmemesi tercih edildi.
        //
        // 🔴 İlk seçilen Ctrl+Shift+D, "Seçili iki commit'i karşılaştır" ile çakışıyordu;
        // çakışma testi yakaladı. Sessizce kalsaydı iki komuttan biri ölü tuşa dönerdi.
        Define(CommandIds.ToolsDiagnostics, "Performans teşhisi…", CommandCategory.Tools,
            CommandContext.Global, Key.F12, KeyModifiers.Control | KeyModifiers.Shift),
        Define(CommandIds.ToolsSettings, "Ayarlar…", CommandCategory.Tools,
            CommandContext.Global, Key.OemComma, KeyModifiers.Control),
        Define(CommandIds.ToolsCommandPalette, "Komut paleti", CommandCategory.Tools,
            CommandContext.Global, Key.P, KeyModifiers.Control | KeyModifiers.Shift),
        Define(CommandIds.HelpShortcuts, "Klavye kısayolları", CommandCategory.Help,
            CommandContext.Global, Key.F1),
        Define(CommandIds.HelpAbout, "Hakkında", CommandCategory.Help,
            CommandContext.Global, gesture: null),
        Define(CommandIds.AppExit, "Çıkış", CommandCategory.Repository,
            CommandContext.Global, Key.Q, KeyModifiers.Control),

        // ------------------------------------------------------------- Commit listesi
        Define(CommandIds.CommitListFind, "Commit ara (SHA veya mesaj)", CommandCategory.Navigation,
            CommandContext.CommitList, Key.F, KeyModifiers.Control),
        Define(CommandIds.CommitListGoToParent, "Ebeveyne git", CommandCategory.Navigation,
            CommandContext.CommitList, Key.P, KeyModifiers.Control),
        Define(CommandIds.CommitListGoToChild, "Çocuğa git", CommandCategory.Navigation,
            CommandContext.CommitList, Key.N, KeyModifiers.Control),
        Define(CommandIds.CommitListPageDown, "Bir sayfa aşağı", CommandCategory.Navigation,
            CommandContext.CommitList, Key.PageDown),
        Define(CommandIds.CommitListPageUp, "Bir sayfa yukarı", CommandCategory.Navigation,
            CommandContext.CommitList, Key.PageUp),
        Define(CommandIds.CommitListCompareToWorkingDirectory, "Çalışma diziniyle karşılaştır",
            CommandCategory.History, CommandContext.CommitList, Key.D, KeyModifiers.Control),
        Define(CommandIds.CommitListCompareSelected, "Seçili iki commit'i karşılaştır",
            CommandCategory.History, CommandContext.CommitList,
            Key.D, KeyModifiers.Control | KeyModifiers.Shift),

        // ---------------------------------------------------------------------- Diff
        Define(CommandIds.DiffFind, "Diff'te bul", CommandCategory.Navigation,
            CommandContext.Diff, Key.F, KeyModifiers.Control),
        Define(CommandIds.DiffFindNext, "Sonrakini bul", CommandCategory.Navigation,
            CommandContext.Diff, Key.F3),
        Define(CommandIds.DiffFindPrevious, "Öncekini bul", CommandCategory.Navigation,
            CommandContext.Diff, Key.F3, KeyModifiers.Shift),
        Define(CommandIds.DiffNextChange, "Sonraki değişiklik", CommandCategory.Navigation,
            CommandContext.Diff, Key.Down, KeyModifiers.Alt),
        Define(CommandIds.DiffPreviousChange, "Önceki değişiklik", CommandCategory.Navigation,
            CommandContext.Diff, Key.Up, KeyModifiers.Alt),
        Define(CommandIds.DiffNextHunk, "Sonraki hunk", CommandCategory.Navigation,
            CommandContext.Diff, Key.PageDown, KeyModifiers.Control),
        Define(CommandIds.DiffPreviousHunk, "Önceki hunk", CommandCategory.Navigation,
            CommandContext.Diff, Key.PageUp, KeyModifiers.Control),
        Define(CommandIds.DiffNextFile, "Sonraki dosya", CommandCategory.Navigation,
            CommandContext.Diff, Key.Right, KeyModifiers.Alt),
        Define(CommandIds.DiffPreviousFile, "Önceki dosya", CommandCategory.Navigation,
            CommandContext.Diff, Key.Left, KeyModifiers.Alt),
        Define(CommandIds.DiffStageLines, "Seçili satırları stage'le", CommandCategory.Commit,
            CommandContext.Diff, Key.S),
        Define(CommandIds.DiffUnstageLines, "Seçili satırları unstage'le", CommandCategory.Commit,
            CommandContext.Diff, Key.U),
        Define(CommandIds.DiffResetLines, "Seçili satırları geri al", CommandCategory.Commit,
            CommandContext.Diff, Key.R),
        Define(CommandIds.DiffCopyCode, "Kodu kopyala", CommandCategory.Tools,
            CommandContext.Diff, Key.C, KeyModifiers.Control),
        Define(CommandIds.DiffCopyPatch, "Yamayı kopyala", CommandCategory.Tools,
            CommandContext.Diff, Key.C, KeyModifiers.Control | KeyModifiers.Shift),

        // ------------------------------------------------------------- Çalışma ağacı
        Define(CommandIds.WorkingTreeToggleStage, "Seçiliyi stage'le / unstage'le",
            CommandCategory.Commit, CommandContext.WorkingTree, Key.Space),
        Define(CommandIds.WorkingTreeStageAll, "Tümünü stage'le", CommandCategory.Commit,
            CommandContext.WorkingTree, Key.S, KeyModifiers.Control | KeyModifiers.Shift),
        Define(CommandIds.WorkingTreeUnstageAll, "Tümünü unstage'le", CommandCategory.Commit,
            CommandContext.WorkingTree, Key.U, KeyModifiers.Control | KeyModifiers.Shift),
        Define(CommandIds.WorkingTreeCommit, "Commit et", CommandCategory.Commit,
            CommandContext.WorkingTree, Key.Enter, KeyModifiers.Control),

        // --------------------------------------------------------------- Dal paneli
        Define(CommandIds.RefTreeDelete, "Seçili dalı sil…", CommandCategory.Branch,
            CommandContext.RefTree, Key.Delete),
        Define(CommandIds.RefTreeRename, "Seçili dalı yeniden adlandır…", CommandCategory.Branch,
            CommandContext.RefTree, Key.F2),
    ];

    /// <summary>Şemanın tamamı, tanım sırasında.</summary>
    public static IReadOnlyList<CommandDefinition> Definitions => All;

    private static CommandDefinition Define(
        string id,
        string title,
        CommandCategory category,
        CommandContext context,
        Key key,
        KeyModifiers modifiers = KeyModifiers.None) =>
        new(id, title, category, context, new KeyGesture(key, modifiers));

    private static CommandDefinition Define(
        string id,
        string title,
        CommandCategory category,
        CommandContext context,
        KeyGesture? gesture) =>
        new(id, title, category, context, gesture);
}
