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

/// <summary>
/// Emits the current text immediately and again on every language change. The subscription is registered weakly in
/// <see cref="Loc"/> and kept alive only by the binding that owns it, so discarded controls are not leaked.
/// </summary>
internal sealed class LocObservable(string key, bool upper) : IObservable<string>
{
    public IDisposable Subscribe(IObserver<string> observer)
    {
        var subscription = new Subscription(key, upper, observer);
        Loc.AddTextSubscriber(subscription);
        subscription.Refresh();
        return subscription;
    }

    private sealed class Subscription(string key, bool upper, IObserver<string> observer) : Loc.ITextSubscriber, IDisposable
    {
        private IObserver<string>? _observer = observer;

        public void Refresh()
        {
            var o = _observer;
            if (o == null) return;
            var text = Loc.T(key);
            o.OnNext(upper ? text.ToUpper(Loc.Culture) : text);
        }

        public void Dispose()
        {
            if (_observer == null) return;
            _observer = null;
            Loc.RemoveTextSubscriber(this);
        }
    }
}
