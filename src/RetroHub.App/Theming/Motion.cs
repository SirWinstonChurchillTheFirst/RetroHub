using System.Runtime.InteropServices;
using RetroHub.Core.Config;

namespace RetroHub.App.Theming;

/// <summary>Effective motion after resolving <see cref="MotionLevel.System"/>.</summary>
public enum EffectiveMotion { Full, Reduced, Off }

/// <summary>
/// Motion levels. <see cref="MotionLevel.System"/> follows Windows "Animation effects"
/// (SystemParametersInfo SPI_GETCLIENTAREAANIMATION): on → Full, off → Off.
/// The shell sets a class on the window root ("motion-full" / "motion-reduced" / "motion-off"); control themes
/// switch their transitions with it, code animations ask <see cref="Current"/>.
/// </summary>
public static partial class Motion
{
    public static MotionLevel Setting { get; private set; } = MotionLevel.System;
    public static EffectiveMotion Current { get; private set; } = EffectiveMotion.Full;

    // ===== Tokens (DESIGN 9.1, "Fernsehlicht") – raw values; the properties below apply the motion level =====
    public static readonly TimeSpan BlinkDuration = TimeSpan.FromMilliseconds(90);
    public static readonly TimeSpan StepDuration = TimeSpan.FromMilliseconds(140);
    public static readonly TimeSpan GlideDuration = TimeSpan.FromMilliseconds(220);
    public static readonly TimeSpan GlideRepeatDuration = TimeSpan.FromMilliseconds(90);
    public static readonly TimeSpan SceneDuration = TimeSpan.FromMilliseconds(280);
    public static readonly TimeSpan LightDuration = TimeSpan.FromMilliseconds(420);
    /// <summary>Rest before the TV light / stage follows the focus.</summary>
    public static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(220);
    /// <summary>Cover decode delay while a direction is held (auto-repeat).</summary>
    public static readonly TimeSpan DecodeDwell = TimeSpan.FromMilliseconds(50);
    /// <summary>Safety net: auto-repeat ends this long after the last repeat event (lost KeyUp).</summary>
    public static readonly TimeSpan RepeatRelease = TimeSpan.FromMilliseconds(150);
    /// <summary>Channel display (OSD) stand time; shown and hidden hard.</summary>
    public static readonly TimeSpan Osd = TimeSpan.FromMilliseconds(1200);
    public static readonly TimeSpan PowerOn = TimeSpan.FromMilliseconds(520);

    /// <summary>Ring, inversion, colour changes (90 ms linear); zero when motion is off.</summary>
    public static TimeSpan Blink => Current == EffectiveMotion.Off ? TimeSpan.Zero : BlinkDuration;
    /// <summary>Lift, button scale (140 ms); zero when reduced/off (no lift then).</summary>
    public static TimeSpan Step => Current == EffectiveMotion.Full ? StepDuration : TimeSpan.Zero;
    /// <summary>Boards, search field, expanders (220 ms); reduced: rows jump.</summary>
    public static TimeSpan Glide => Current == EffectiveMotion.Full ? GlideDuration : TimeSpan.Zero;
    /// <summary>Page enter, wall change, overlays (280 ms); reduced: 100 ms fade.</summary>
    public static TimeSpan Scene => Current switch
    {
        EffectiveMotion.Full => SceneDuration,
        EffectiveMotion.Reduced => TimeSpan.FromMilliseconds(100),
        _ => TimeSpan.Zero,
    };
    /// <summary>TV light, board light colour (420 ms); reduced 200 ms; off: hard.</summary>
    public static TimeSpan Light => Current switch
    {
        EffectiveMotion.Full => LightDuration,
        EffectiveMotion.Reduced => TimeSpan.FromMilliseconds(200),
        _ => TimeSpan.Zero,
    };

    /// <summary>Selection/focus animation duration (legacy name, = <see cref="Blink"/> … <see cref="Step"/>).</summary>
    public static TimeSpan Selection => Current == EffectiveMotion.Off ? TimeSpan.Zero : StepDuration;
    /// <summary>Content fade on page enter (= <see cref="Scene"/>).</summary>
    public static TimeSpan Content => Scene;

    public static bool AllowsMovement => Current == EffectiveMotion.Full;
    public static bool Enabled => Current != EffectiveMotion.Off;

    public static event Action? Changed;

    public static void Apply(MotionLevel level)
    {
        Setting = level;
        var effective = Resolve(level, SystemAnimationsEnabled);
        if (effective == Current) return;
        Current = effective;
        try { Changed?.Invoke(); } catch { /* listeners must not break */ }
    }

    /// <summary>Re-reads the Windows setting (called when the window is activated).</summary>
    public static void Refresh() => Apply(Setting);

    public static EffectiveMotion Resolve(MotionLevel level, bool systemAnimations) => level switch
    {
        MotionLevel.Full => EffectiveMotion.Full,
        MotionLevel.Reduced => EffectiveMotion.Reduced,
        MotionLevel.Off => EffectiveMotion.Off,
        _ => systemAnimations ? EffectiveMotion.Full : EffectiveMotion.Off,
    };

    public static string ClassName(EffectiveMotion m) => m switch
    {
        EffectiveMotion.Full => "motion-full",
        EffectiveMotion.Reduced => "motion-reduced",
        _ => "motion-off",
    };

    /// <summary>Windows "Show animations in Windows" (true on other platforms or when the call fails).</summary>
    public static bool SystemAnimationsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return true;
            try
            {
                return SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out var on, 0) ? on : true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return true;
            }
        }
    }

    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
