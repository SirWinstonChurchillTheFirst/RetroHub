using RetroHub.Core.Catalog;
using RetroHub.Core.Library;
using Xunit;

namespace RetroHub.Core.Tests.Library;

/// <summary>
/// Scans copies of the real homebrew ROMs from the main checkout (D:\RetroHub\games, read-only: copied to a temp
/// folder first). Skipped when that folder is not present (other machines / CI).
/// </summary>
public sealed class RealRomScanTests : IDisposable
{
    private const string Source = @"D:\RetroHub\games";
    private readonly TempRoot _tmp = new();
    public void Dispose() => _tmp.Dispose();

    [Fact]
    public async Task Scans_real_homebrew_roms()
    {
        if (!Directory.Exists(Source)) Assert.Skip($"{Source} not present");
        foreach (var file in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_tmp.Paths.Games, Path.GetRelativePath(Source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        static SystemDef Sys(string id, string folder, string[] ext, string sniffer) =>
            new() { Id = id, Name = id.ToUpperInvariant(), Folders = [folder], Extensions = ext, Sniffers = [sniffer] };
        var catalog = new FakeCatalog(
        [
            Sys("nes", "nes", [".nes", ".zip"], "nes"),
            Sys("gb", "gb", [".gb", ".zip"], "gb"),
            Sys("gbc", "gbc", [".gbc", ".zip"], "gbc"),
            Sys("gba", "gba", [".gba", ".zip"], "gba"),
        ]);
        var repo = new InMemoryGameRepository();
        var scanner = new LibraryScanner(catalog, repo, _tmp.Paths);

        var report = await scanner.ScanAsync(new ScanOptions(), null, TestContext.Current.CancellationToken);

        var output = TestContext.Current.TestOutputHelper;
        output?.WriteLine($"Report: added {report.Added}, updated {report.Updated}, unchanged {report.Unchanged}, " +
                          $"missing {report.Missing}, unknown {report.Unknown.Count}, duplicates {report.Duplicates.Count}, " +
                          $"errors {report.Errors.Count}, {report.Duration.TotalMilliseconds:0} ms");
        foreach (var g in repo.All())
            output?.WriteLine($"  [{g.SystemId}] {g.Path} | title '{g.Title}' sort '{g.SortTitle}' | {g.Kind} {g.Status} | " +
                              $"{g.Size} bytes crc {g.Crc32} serial {g.Serial ?? "-"} region {g.Region ?? "-"}");
        foreach (var i in report.Unknown.Concat(report.Errors)) output?.WriteLine($"  issue: {i.Path}: {i.Reason}");

        // Every system detection is cross-checked against each file's header sniffer.
        var sourceCount = Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories).Count();
        Assert.Equal(sourceCount, report.Added);
        Assert.Empty(report.Unknown);
        Assert.Empty(report.Errors);
        foreach (var g in repo.All())
        {
            Assert.Equal(GameStatus.Ok, g.Status);
            Assert.Equal(Path.GetFileName(Path.GetDirectoryName(_tmp.Paths.FromPortable(g.Path))), g.SystemId);
            var sniffer = RetroHub.Core.Library.Sniffers.RomSniffers.ById(catalog.GetSystem(g.SystemId)!.Sniffers[0])!;
            var bytes = File.ReadAllBytes(_tmp.Paths.FromPortable(g.Path));
            Assert.NotNull(sniffer.Sniff(_tmp.Paths.FromPortable(g.Path), bytes.AsSpan(0, Math.Min(bytes.Length, sniffer.HeaderBytes))));
            Assert.Equal(Crc32.ToHex(Crc32.Compute(bytes)), g.Crc32);
        }

        var again = await scanner.ScanAsync(new ScanOptions(), null, TestContext.Current.CancellationToken);
        Assert.Equal(sourceCount, again.Unchanged);
    }
}
