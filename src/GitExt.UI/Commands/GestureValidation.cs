using Avalonia.Input;

namespace GitExt.UI.Commands;

/// <summary>
/// Bir jestin bir komuta atanabilir olup olmadığı (P08-T03).
/// </summary>
public enum GestureRejection
{
    /// <summary>Atanabilir.</summary>
    None,

    /// <summary>Tuş yalnızca bir değiştirici (Ctrl/Alt/Shift/Meta) — jest değil.</summary>
    ModifierOnly,

    /// <summary>
    /// Küresel bir komuta değiştiricisiz harf/rakam/ok atanamaz.
    /// </summary>
    BareKeyInGlobalContext,

    /// <summary>Uygulamanın işleyemeyeceği bir tuş.</summary>
    Unsupported,
}

/// <summary>
/// Kullanıcının atamasına izin verilen jestleri süzer (P08-T03).
/// </summary>
/// <remarks>
/// <para>
/// Kısıtların tamamı P08-T00 ölçümlerinden çıktı; keyfî değiller.
/// </para>
/// <para>
/// 🔴 <b>Küresel bağlamda çıplak tuş yasak.</b> M11+M12'de ölçüldü: pencere seviyesindeki bir
/// jest odaklı kontrolden tuşu <b>koşulsuz</b> alır ve kontrol <c>Handled=true</c> yapsa bile
/// komut çalışır. Küresel bir <c>S</c> atanabilseydi kullanıcı bir daha hiçbir metin kutusuna
/// "s" yazamazdı ve sebebini de bulamazdı — çünkü hiçbir hata çıkmaz.
/// </para>
/// </remarks>
public static class GestureValidation
{
    /// <summary>Tek başına basıldığında jest sayılmayan tuşlar.</summary>
    private static readonly Key[] ModifierKeys =
    [
        Key.LeftCtrl, Key.RightCtrl,
        Key.LeftAlt, Key.RightAlt,
        Key.LeftShift, Key.RightShift,
        Key.LWin, Key.RWin,
        Key.System,
        Key.None,
    ];

    public static GestureRejection Validate(KeyGesture gesture, CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(gesture);

        if (ModifierKeys.Contains(gesture.Key))
        {
            return GestureRejection.ModifierOnly;
        }

        if (gesture.Key is Key.Cancel or Key.Clear)
        {
            return GestureRejection.Unsupported;
        }

        if (context.HasFlag(CommandContext.Global)
            && gesture.KeyModifiers == KeyModifiers.None
            && !IsSafeBareKey(gesture.Key))
        {
            return GestureRejection.BareKeyInGlobalContext;
        }

        return GestureRejection.None;
    }

    /// <summary>
    /// Değiştiricisiz de küresel olabilen tuşlar.
    /// </summary>
    /// <remarks>
    /// Fonksiyon tuşları ve <c>Escape</c> hiçbir metin üretmez, dolayısıyla yazmayı
    /// engellemezler — <c>F5</c>, <c>F1</c> zaten şemada böyle.
    /// </remarks>
    private static bool IsSafeBareKey(Key key) =>
        key is >= Key.F1 and <= Key.F24 or Key.Escape or Key.Pause or Key.PrintScreen;

    /// <summary>Reddetme sebebinin kullanıcıya gösterilecek açıklaması.</summary>
    public static string Describe(GestureRejection rejection) => rejection switch
    {
        GestureRejection.ModifierOnly =>
            "Only a modifier key was pressed. Press another key together with Ctrl/Alt/Shift.",
        GestureRejection.BareKeyInGlobalContext =>
            "A key without a modifier cannot be assigned to an application-wide command: the key would be stolen from text "
            + "boxes too and you would not be able to type it again. Add Ctrl, Alt or Shift "
            + "(function keys are the exception).",
        GestureRejection.Unsupported =>
            "This key cannot be used as a shortcut.",
        _ => "",
    };
}
