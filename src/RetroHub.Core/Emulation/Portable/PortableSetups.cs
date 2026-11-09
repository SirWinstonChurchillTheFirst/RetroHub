using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation.Portable;

/// <summary>
/// The built-in portable recipes, keyed by <see cref="Catalog.EmulatorDef.Portable"/>. The session service calls
/// <see cref="Apply(EmulatorInstall, LaunchPlan, AppPaths)"/> right before starting the emulator.
/// </summary>
public static class PortableSetups
{
    public static IReadOnlyList<IPortableSetup> All { get; } =
    [
        new RetroArchSetup(),
        new MgbaSetup(),
        new MelonDsSetup(),
        new DesmumeSetup(),
        new AzaharSetup(),
        new DolphinSetup(),
        new Pcsx2Setup(),
        new DuckStationSetup(),
        new PpssppSetup(),
        new CemuSetup(),
        new EdenSetup(),
    ];

    /// <summary>The recipe with that id (case-insensitive), or null.</summary>
    public static IPortableSetup? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Runs the recipe named by the emulator definition's Portable id (nothing when it has none).
    /// Never throws; the result says what happened (also logged).
    /// </summary>
    public static PortableApplyResult Apply(EmulatorInstall emulator, LaunchPlan plan, AppPaths paths)
    {
        var id = emulator.Def.Portable;
        if (string.IsNullOrWhiteSpace(id)) return PortableApplyResult.NoRecipe;
        var setup = Find(id);
        if (setup == null)
        {
            Log.Warn($"Emulator {emulator.Def.Id}: unknown portable recipe '{id}'");
            return PortableApplyResult.NoRecipe;
        }
        if (setup is PortableSetupBase b) return b.Run(emulator, plan, paths);
        try
        {
            setup.Apply(emulator, plan, paths);
            return PortableApplyResult.Applied;
        }
        catch (Exception ex)
        {
            Log.Warn($"Portable setup '{id}' failed", ex);
            return PortableApplyResult.Failed;
        }
    }

    /// <summary>German description for the Emulators page, or null.</summary>
    public static string? SummaryFor(string? id) => (Find(id) as PortableSetupBase)?.Summary;
}
