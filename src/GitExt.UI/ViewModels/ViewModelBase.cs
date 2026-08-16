using CommunityToolkit.Mvvm.ComponentModel;

namespace GitExt.UI.ViewModels;

/// <summary>
/// The common base of all the ViewModels.
/// </summary>
/// <remarks>
/// <see cref="ObservableObject"/> comes from CommunityToolkit.Mvvm and generates the
/// <c>INotifyPropertyChanged</c> implementation at compile time (ADR-0004).
/// Derived classes must be <c>partial</c> — the source generator requires it.
/// </remarks>
public abstract partial class ViewModelBase : ObservableObject;
