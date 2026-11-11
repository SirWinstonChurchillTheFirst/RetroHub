using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using RetroHub.Core.Catalog;
using RetroHub.Core.Config;
using RetroHub.Core.Data;
using RetroHub.Core.Library;
using RetroHub.Core.Metadata;
using RetroHub.Core.Tests.Emulation.Config;

using Xunit;

namespace RetroHub.Core.Tests.Metadata;

/// <summary>A throw-away portable root with a real SQLite library and a small catalog (gba, nes, snes, pc, switch via GameTDB).</summary>
internal sealed class ArtworkTestEnv : IDisposable
{
    public const string GbaLibretro = "Nintendo - Game Boy Advance";

    public string Root { get; }
    public AppPaths Paths { get; }
    public SettingsStore Settings { get; }
    public SqliteGameRepository Repo { get; }
    public FakeCatalog Catalog { get; } = new();
    public FakeHttp Http { get; } = new();

    public ArtworkTestEnv(bool online = false)
    {
        Root = Directory.CreateTempSubdirectory("retrohub-artwork-").FullName;
        Paths = new AppPaths(Root, Path.Combine(Root, "app"));
        Settings = new SettingsStore(Paths.SettingsFile);
        Settings.Current.OnlineCovers = online;
        Repo = new SqliteGameRepository(Paths.DatabaseFile);
        Catalog.Add(new SystemDef { Id = "gba", Name = "Game Boy Advance", Folders = ["gba", "gameboyadvance"], Extensions = [".gba"], LibretroName = GbaLibretro });
        Catalog.Add(new SystemDef { Id = "nes", Name = "NES", Folders = ["nes"], Extensions = [".nes"], LibretroName = "Nintendo - Nintendo Entertainment System" });
        Catalog.Add(new SystemDef { Id = "snes", Name = "SNES", Folders = ["snes"], Extensions = [".sfc", ".smc", ".bs"], LibretroName = LibretroThumbnails.SnesSet });
        Catalog.Add(new SystemDef { Id = "pc", Name = "PC", Folders = ["pc"], Category = SystemCategory.Pc, FolderGames = true });
        Catalog.Add(new SystemDef { Id = "switch", Name = "Nintendo Switch", Folders = ["switch"], Extensions = [".nsp", ".xci"], GameTdb = "switch" });
    }

    public ArtworkService Service(TimeSpan? downloadTimeout = null, TimeSpan? gameTdbTimeout = null) =>
        new(Paths, Repo, Catalog, Settings, new HttpClient(Http))
        {
            DownloadTimeout = downloadTimeout ?? TimeSpan.FromSeconds(10),
            GameTdbTimeout = gameTdbTimeout ?? TimeSpan.FromSeconds(10),
            GameTdbRetryDelay = TimeSpan.Zero,
        };

    /// <summary>Creates the file below Root and adds the game (title parsed like the scanner does).</summary>
    public Game AddGame(string relative, string system = "gba", Action<Game>? setup = null)
    {
        Touch(relative, "ROM");
        var parsed = TitleParser.Parse(Path.GetFileName(relative));
        var g = new Game
        {
            SystemId = system,
            Path = relative,
            Title = parsed.Title,
            SortTitle = parsed.SortTitle,
            Region = parsed.Region,
            Kind = relative.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? GameKind.Executable : GameKind.Rom,
            AddedUtc = DateTime.UtcNow,
            FileTimeUtc = DateTime.UtcNow,
        };
        setup?.Invoke(g);
        g.Id = Repo.Upsert(g);
        return Repo.Get(g.Id)!;
    }

    public Game Reload(Game g) => Repo.Get(g.Id)!;

    public string Touch(string relative, string content = "") => Touch(relative, Encoding.UTF8.GetBytes(content));

    public string Touch(string relative, byte[] content)
    {
        var full = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }

    public string Full(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A tiny byte array with a PNG signature (the service only checks the signature).</summary>
    public static byte[] Png(byte marker = 1) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, marker, marker, marker];

    /// <summary>A GitHub git-trees response listing these boxart names (plus some noise).</summary>
    public static string TreeJson(IEnumerable<string> boxarts)
    {
        var items = new List<string>
        {
            """{"path":".gitignore","type":"blob"}""",
            """{"path":"Named_Boxarts","type":"tree"}""",
            """{"path":"Named_Snaps/Advance Wars (USA).png","type":"blob"}""",
        };
        items.AddRange(boxarts.Select(b => $$"""{"path":"Named_Boxarts/{{b.Replace("\"", "\\\"")}}.png","mode":"100644","type":"blob"}"""));
        return $$"""{"sha":"x","tree":[{{string.Join(",", items)}}],"truncated":false}""";
    }

    public void Dispose()
    {
        Repo.Dispose();
        SqliteConnection.ClearAllPools();
        for (var i = 0; i < 5; i++)
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); return; }
            catch (IOException) { Thread.Sleep(50); }
            catch (UnauthorizedAccessException) { Thread.Sleep(50); }
        }
    }
}

/// <summary>Fake HTTP: routes by URL, counts and records every request (URL + User-Agent).</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly object _gate = new();
    public List<(Uri Url, string UserAgent)> Requests { get; } = new();
    public Func<HttpRequestMessage, HttpResponseMessage>? Handler { get; set; }

    public int Count { get { lock (_gate) return Requests.Count; } }
    public int GitHubCount { get { lock (_gate) return Requests.Count(r => r.Url.Host == "api.github.com"); } }
    public int ThumbnailCount { get { lock (_gate) return Requests.Count(r => r.Url.Host == "thumbnails.libretro.com"); } }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (_gate) Requests.Add((request.RequestUri!, request.Headers.UserAgent.ToString()));
        var h = Handler ?? (_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        return Task.FromResult(h(request));
    }

    public static HttpResponseMessage Bytes(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
    public static HttpResponseMessage Text(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };

    /// <summary>The boxart name requested from thumbnails.libretro.com (unescaped, without ".png").</summary>
    public static string? BoxartName(Uri url)
    {
        if (url.Host != "thumbnails.libretro.com") return null;
        var last = Uri.UnescapeDataString(url.AbsolutePath.Split('/').Last());
        return last.EndsWith(".png", StringComparison.Ordinal) ? last[..^4] : last;
    }
}

/// <summary>A stream that delivers a few bytes and then fails (simulates a dropped connection).</summary>
internal sealed class BrokenStream(byte[] head) : Stream
{
    private int _pos;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _pos; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_pos >= head.Length) throw new IOException("connection reset (test)");
        var n = Math.Min(count, head.Length - _pos);
        Array.Copy(head, _pos, buffer, offset, n);
        _pos += n;
        return n;
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
