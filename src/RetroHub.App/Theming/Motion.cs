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

    /// <summary>Selection/focus animation duration (Nova: 160 ms ease-out).</summary>
    public static TimeSpan Selection => Current == EffectiveMotion.Off ? TimeSpan.Zero : TimeSpan.FromMilliseconds(160);
    /// <summary>Content fade (Nova: 220 ms). Reduced keeps a short fade without movement.</summary>
    public static TimeSpan Content => Current switch
    {
        EffectiveMotion.Full => TimeSpan.FromMilliseconds(220),
        EffectiveMotion.Reduced => TimeSpan.FromMilliseconds(140),
        _ => TimeSpan.Zero,
    };

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
