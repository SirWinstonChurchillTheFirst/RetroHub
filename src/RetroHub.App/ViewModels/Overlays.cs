using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroHub.App.Localization;
using RetroHub.Core.Logging;

namespace RetroHub.App.ViewModels;

public enum ToastKind { Info, Success, Warning, Error }

public sealed partial class ToastViewModel : ObservableObject
{
    public required string Text { get; init; }
    public string? Detail { get; init; }
    public ToastKind Kind { get; init; }
    public bool IsInfo => Kind == ToastKind.Info;
    public bool IsSuccess => Kind == ToastKind.Success;
    public bool IsWarning => Kind == ToastKind.Warning;
    public bool IsError => Kind == ToastKind.Error;
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
    /// <summary>The detail as the second run of the one footer line (Text 2 behind the message).</summary>
    public string DetailRun => HasDetail ? "   " + Detail : "";
}

/// <summary>
/// Transient notifications, docked left in the footer in place of the location sentence (QA 2: no floating cards).
/// Thread-safe: may be called from any thread.
/// </summary>
public sealed partial class ToastService : ObservableObject
{
    /// <summary>The UI dispatcher, taken when this object was made (posts from other threads never create a new one).</summary>
    private readonly Dispatcher _ui = Dispatcher.UIThread;

    public const int MaxVisible = 4;
    public ObservableCollection<ToastViewModel> Items { get; } = [];

    public void Show(string text, ToastKind kind = ToastKind.Info, string? detail = null, double seconds = 4)
    {
        void Add()
        {
            var toast = new ToastViewModel { Text = text, Kind = kind, Detail = detail };
            Items.Add(toast);
            while (Items.Count > MaxVisible) Items.RemoveAt(0);
            DispatcherTimer.RunOnce(() => Items.Remove(toast), TimeSpan.FromSeconds(seconds));
        }
        if (_ui.CheckAccess()) Add();
        else _ui.Post(Add);
    }

    [RelayCommand]
    private void Dismiss(ToastViewModel? toast)
    {
        if (toast != null) Items.Remove(toast);
    }
}

/// <summary>One button of a dialog.</summary>
public sealed record DialogButton(string Label, string Result, bool IsPrimary = false, bool IsDanger = false, bool IsCancel = false);

/// <summary>A line in a dialog's detail list (e.g. a launch problem).</summary>
public sealed record DialogDetail(string Text, bool IsBlocking);

public sealed partial class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<string?> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public required string Title { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<DialogDetail> Details { get; init; } = [];
    public required IReadOnlyList<DialogButton> Buttons { get; init; }
    public bool IsDanger { get; init; }
    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public bool HasDetails => Details.Count > 0;

    public Task<string?> Result => _done.Task;

    [RelayCommand]
    private void Choose(DialogButton? button) => _done.TrySetResult(button?.Result);

    /// <summary>Esc / B: the cancel button's result (or null).</summary>
    public void Cancel() => _done.TrySetResult(Buttons.FirstOrDefault(b => b.IsCancel)?.Result);
}

/// <summary>
/// In-window modal dialogs (controller-navigable; focus stays in the one TopLevel). One at a time; a second request
/// waits for the first to close.
/// </summary>
public sealed partial class DialogService : ObservableObject
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    [ObservableProperty] public partial DialogViewModel? Current { get; private set; }

    public bool IsOpen => Current != null;

    partial void OnCurrentChanged(DialogViewModel? value) => OnPropertyChanged(nameof(IsOpen));

    public async Task<string?> ShowAsync(DialogViewModel dialog)
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            Current = dialog;
            return await dialog.Result.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warn("Dialog failed", ex);
            return null;
        }
        finally
        {
            Current = null;
            _gate.Release();
        }
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool danger = false)
    {
        var result = await ShowAsync(new DialogViewModel
        {
            Title = title,
            Message = message,
            IsDanger = danger,
            Buttons =
            [
                new DialogButton(confirmLabel, "ok", IsPrimary: true, IsDanger: danger),
                new DialogButton(Loc.T("common.cancel"), "cancel", IsCancel: true),
            ],
        });
        return result == "ok";
    }

    public Task InfoAsync(string title, string message, IReadOnlyList<DialogDetail>? details = null) =>
        ShowAsync(new DialogViewModel
        {
            Title = title,
            Message = message,
            Details = details ?? [],
            Buttons = [new DialogButton(Loc.T("common.ok"), "ok", IsPrimary: true, IsCancel: true)],
        });
}
