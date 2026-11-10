using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RetroHub.App.ViewModels;

/// <summary>One value of a service-menu row ("Bernstein", "An", "1,5 s"), optionally with a 16 × 16 colour swatch.</summary>
public sealed record ServiceValue(object? Value, string Label, IBrush? Swatch = null);

/// <summary>
/// A row of the service menu (DESIGN 7.6 "Einstellungen: Service-Menü"): a label, an optional one-line description and
/// either a value between two arrows (◀ Wert ▶) or an action ("Öffnen ›").
/// <list type="bullet">
/// <item>Left/right on the pad or keyboard change the value at once (<see cref="Step"/>); confirm moves on
/// (<see cref="Activate"/>, wraps round); the mouse clicks the arrows.</item>
/// <item>Ordered values (seconds) stop at their ends (<see cref="Wraps"/> = false), choices (An/Aus, colours) wrap.</item>
/// </list>
/// The row never stores anything itself: <see cref="Changed"/> applies and saves the value.
/// </summary>
public sealed partial class ServiceSetting : ObservableObject
{
    private readonly Action<ServiceValue>? _changed;
    private readonly Action? _invoke;

    private ServiceSetting(string label, string? hint, IReadOnlyList<ServiceValue> values, int index, bool wraps,
        Action<ServiceValue>? changed, Action? invoke, string? actionText)
    {
        Label = label;
        Hint = hint ?? "";
        Values = values;
        Wraps = wraps;
        _changed = changed;
        _invoke = invoke;
        ActionText = actionText ?? "";
        _index = Math.Clamp(index, 0, Math.Max(0, values.Count - 1));
    }

    /// <summary>A value row: <paramref name="values"/>, the current one at <paramref name="index"/>.</summary>
    public static ServiceSetting Choice(string label, string? hint, IReadOnlyList<ServiceValue> values, int index,
        Action<ServiceValue> changed, bool wraps = true) =>
        new(label, hint, values, index, wraps, changed, null, null);

    /// <summary>"An" / "Aus" (wraps; confirm toggles).</summary>
    public static ServiceSetting Switch(string label, string? hint, bool on, Action<bool> changed, string onText, string offText) =>
        new(label, hint, [new ServiceValue(true, onText), new ServiceValue(false, offText)], on ? 0 : 1, true,
            v => changed((bool)v.Value!), null, null);

    /// <summary>An action row: confirm (or a click) runs <paramref name="invoke"/>; the value column reads "<paramref name="actionText"/> ›".</summary>
    public static ServiceSetting Action(string label, string? hint, string actionText, Action invoke) =>
        new(label, hint, [], 0, false, null, invoke, actionText);

    public string Label { get; }
    /// <summary>One line in <c>meta</c> Text 3 under the label (empty = none).</summary>
    public string Hint { get; private set; }
    public bool HasHint => Hint.Length > 0;
    public IReadOnlyList<ServiceValue> Values { get; }
    public bool Wraps { get; }
    public bool IsAction => _invoke != null;
    /// <summary>Only one value (a built-in folder is always "Aktiv"): no arrows.</summary>
    public bool IsFixed => !IsAction && Values.Count < 2;
    /// <summary>
    /// Confirm does something else than moving the value on (a folder row opens its "Mehr" list); left/right still
    /// change the value.
    /// </summary>
    public Action? ActivateOverride { get; set; }
    public bool IsChoice => !IsAction;
    public string ActionText { get; }

    private int _index;

    /// <summary>The index of the current value.</summary>
    public int Index => _index;
    public ServiceValue? Current => Values.Count > 0 ? Values[_index] : null;
    public string ValueText => Current?.Label ?? "";
    public IBrush? Swatch => Current?.Swatch;
    public bool HasSwatch => Swatch != null;
    /// <summary>Is there a value before / after the current one (an ordered row stops at its ends)?</summary>
    public bool CanPrevious => Values.Count > 1 && (Wraps || _index > 0);
    public bool CanNext => Values.Count > 1 && (Wraps || _index < Values.Count - 1);

    /// <summary>Left (-1) / right (+1): the neighbouring value, applied at once. Returns false at the end of an ordered row.</summary>
    public bool Step(int direction)
    {
        if (IsAction || Values.Count < 2 || direction == 0) return false;
        var next = _index + Math.Sign(direction);
        if (Wraps) next = (next + Values.Count) % Values.Count;
        else if (next < 0 || next >= Values.Count) return false;
        Select(next);
        return true;
    }

    /// <summary>Confirm: an action runs, a value moves on (and wraps round, also in an ordered row).</summary>
    public void Activate()
    {
        if (ActivateOverride != null)
        {
            ActivateOverride();
            return;
        }
        if (_invoke != null)
        {
            _invoke();
            return;
        }
        if (Values.Count < 2) return;
        Select((_index + 1) % Values.Count);
    }

    /// <summary>Sets the value without applying it (the settings were read again).</summary>
    public void Show(int index)
    {
        if (Values.Count == 0) return;
        _index = Math.Clamp(index, 0, Values.Count - 1);
        Raise();
    }

    /// <summary>Replaces the description (e.g. the motion level's own hint).</summary>
    public void SetHint(string? hint)
    {
        Hint = hint ?? "";
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(HasHint));
    }

    private void Select(int index)
    {
        if (index == _index) return;
        _index = index;
        Raise();
        _changed?.Invoke(Values[index]);
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(Index));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(Swatch));
        OnPropertyChanged(nameof(HasSwatch));
        OnPropertyChanged(nameof(CanPrevious));
        OnPropertyChanged(nameof(CanNext));
    }
}

/// <summary>A section of the service menu: its paper label ("Darstellung") and its rows.</summary>
public sealed record ServiceSection(string Title, IReadOnlyList<ServiceSetting> Rows, string? Note = null)
{
    public bool HasNote => !string.IsNullOrEmpty(Note);
}
