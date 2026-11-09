using Avalonia;
using Avalonia.Data;

namespace RetroHub.App.Localization;

/// <summary>
/// XAML: <c>Text="{l:T nav.home}"</c> (xmlns:l="using:RetroHub.App.Localization").
/// Produces a binding that follows language switches at runtime.
/// Optional <see cref="Upper"/> renders the text in upper case (labels/overlines).
/// </summary>
public sealed class TExtension
{
    public TExtension() { }
    public TExtension(string key) { Key = key; }

    public string Key { get; set; } = "";
    public bool Upper { get; set; }

    public BindingBase ProvideValue(IServiceProvider serviceProvider) =>
        new LocObservable(Key, Upper).ToBinding();
}

/// <summary>Emits the current text immediately and again on every language change.</summary>
internal sealed class LocObservable(string key, bool upper) : IObservable<string>
{
    public IDisposable Subscribe(IObserver<string> observer)
    {
        void Push() => observer.OnNext(upper ? Loc.T(key).ToUpper(Loc.Culture) : Loc.T(key));
        Action handler = Push;
        Loc.LanguageChanged += handler;
        Push();
        return new Unsubscriber(() => Loc.LanguageChanged -= handler);
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() { _dispose?.Invoke(); _dispose = null; }
    }
}
