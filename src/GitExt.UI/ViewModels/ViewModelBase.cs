using CommunityToolkit.Mvvm.ComponentModel;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Tüm ViewModel'ların ortak tabanı.
/// </summary>
/// <remarks>
/// <see cref="ObservableObject"/> CommunityToolkit.Mvvm'den gelir ve
/// <c>INotifyPropertyChanged</c> uygulamasını derleme zamanında üretir (ADR-0004).
/// Türeyen sınıflar <c>partial</c> olmalıdır — source generator bunu gerektirir.
/// </remarks>
public abstract partial class ViewModelBase : ObservableObject;
