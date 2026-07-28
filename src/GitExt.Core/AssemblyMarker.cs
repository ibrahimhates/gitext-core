namespace GitExt.Core;

/// <summary>
/// Bu derlemeyi (assembly) referans almak için kullanılan işaretçi tip.
/// Testlerin ve DI taramalarının <c>typeof(AssemblyMarker).Assembly</c> diyebilmesi içindir.
/// </summary>
/// <remarks>
/// Bu proje şu an iskelet hâlindedir; gerçek Git kodu (süreç yürütücü, komut sarmalayıcıları,
/// çıktı ayrıştırıcıları) bir sonraki aşamada eklenecektir. Bkz. <c>docs/adr/0002-git-backend.md</c>.
/// </remarks>
public static class AssemblyMarker;
