using Avalonia;
using Avalonia.Controls;

namespace RetroHub.App.Views.Pages;

public partial class HomePage : PageBase
{
    /// <summary>Below this page height (logical; 1080 − header − footer = 952 at the reference) the ≈1100 values apply.</summary>
    public const double CompactBelowHeight = 860;

    private bool? _compact;

    public HomePage()
    {
        InitializeComponent();
        // box and screen cast their contact shadows on the stage board
        StageBoard.Casters = [StageBox, Screen];
        SizeChanged += (_, e) => ApplyLayout(e.NewSize);
        Stage.LayoutUpdated += (_, _) => StageBoard.InvalidateVisual();
    }

    /// <summary>
    /// DESIGN 7.1: 1920×1080 logical → stage board at y 560, screen 560×420, box frame 300×420, text column 764,
    /// shelf boxes 200 high. ≈1100 px window (logical 1375×875) → board at y 464, screen 400×300, box 214×300,
    /// shelf boxes 160 high anchored at y 520, so the first label sits fully above the footer fade.
    /// </summary>
    private void ApplyLayout(Size size)
    {
        if (size.Height <= 0) return;
        var compact = size.Height < CompactBelowHeight;
        if (_compact == compact) return;
        _compact = compact;
        Shelves.Classes.Set("compact", compact);
        var stage = compact ? 392.0 : 488.0;
        Stage.Height = stage;
        EmptyStage.Height = stage;
        BoxSlot.Width = compact ? 214 : 300;
        BoxSlot.Height = compact ? 300 : 420;
        Screen.Width = compact ? 400 : 560;
        Screen.Height = compact ? 300 : 420;
        var gap = compact ? 40 : 52;
        Stage.ColumnDefinitions[1].Width = new GridLength(gap);
        Stage.ColumnDefinitions[3].Width = new GridLength(gap);
        StageTitle.Sizes = compact ? "56,40" : "72,56,40";
        TextColumn.Margin = new Thickness(0, 0, 0, compact ? 24 : 32);
        Shelves.Margin = new Thickness(0, compact ? 0 : 8, 0, 48);
    }

    public override bool FocusDefault() => FocusVisible(PlayButton);
}
