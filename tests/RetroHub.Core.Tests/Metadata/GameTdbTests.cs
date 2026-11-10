using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using RetroHub.Core.Library;
using RetroHub.Core.Metadata;

using Xunit;

namespace RetroHub.Core.Tests.Metadata;

/// <summary>Nintendo Switch covers from GameTDB: database parsing, title matching, the download pipeline (fake HTTP).</summary>
public sealed class GameTdbTests : IDisposable
{
    /// <summary>16 real SwitchTDB entries (2026-11-10), trimmed to id/type/region/locale titles (the real file has 24,241).</summary>
    internal const string SampleXml = """
<?xml version="1.0" encoding="UTF-8"?>
<datafile xmlns:xs="http://www.w3.org/2001/XMLSchema">
  <SwitchTDB version="20261110140939" filter="DE" games="16"/>
  <!-- trimmed copy of real SwitchTDB entries -->
  <genres><maingenre name="theme"><loc lang="FR">thème</loc></maingenre></genres>
  <game name="The Legend of Zelda: Breath of the Wild (EN,FR,DE,ES,IT,NL,JA,KO,ZHCN,RU)"><id>AAAAA</id><type>Switch</type><region>ALL</region><locale lang="EN"><title>The Legend of Zelda: Breath of the Wild</title><synopsis/></locale><locale lang="FR"><title>The Legend of Zelda: Breath of the Wild</title><synopsis/></locale><locale lang="NO"><title>The Legend of Zelda: Breath of the Wild</title><synopsis/></locale><locale lang="FI"><title>The Legend of Zelda: Breath of the Wild</title><synopsis/></locale><locale lang="JA"><title>ゼルダの伝説 ブレス オブ ザ ワイルド</title><synopsis/></locale><locale lang="KO"><title>젤다의 전설 브레스 오브 더 와일드 예약판</title><synopsis/></locale><rom version="0" name="x"/><rom version="1" name="x"/><rom version="2" name="x"/><rom version="3" name="x"/></game>
  <game name="The Legend of Zelda: Breath of the Wild + Expansion Pass (EN,FR,DE,ES,IT,NL,JA,KO,ZHCN,RU)"><id>AAAAG</id><type>Switch</type><region>JPN</region><locale lang="EN"><title>The Legend of Zelda: Breath of the Wild + Expansion Pass</title><synopsis/></locale><locale lang="JA"><title>ゼルダの伝説 ブレス オブ ザ ワイルド ＋ エキスパンション・パス</title><synopsis/></locale><rom version="0" name="x"/><rom version="1" name="x"/><rom version="2" name="x"/></game>
  <game name="Zelda Musou: Hyrule All Stars DX (JA,KO,ZHCN)"><id>AKUTA</id><type>Switch</type><region>JPN</region><locale lang="EN"><title>Zelda Musou: Hyrule All Stars DX</title><synopsis/></locale><locale lang="JA"><title>ゼルダ無双 ハイラルオールスターズ</title><synopsis/></locale><rom version="0" name="x"/></game>
  <game name="Hyrule Warriors: Definitive Edition (EN,FR,DE,ES,IT)"><id>AKUTB</id><type>Switch</type><region>USA,EUR,AUS</region><locale lang="EN"><title>Hyrule Warriors: Definitive Edition</title><synopsis/></locale><locale lang="FI"><title>Hyrule Warriors: Definitive Edition</title><synopsis/></locale><rom version="0" name="x"/></game>
  <game name="The Legend of Zelda: Link's Awakening: Dreamer Edition (EN,FR,DE,ES,IT,NL,JA,KO,ZHCN,RU)"><id>AR3N1</id><type>Switch</type><region>USA</region><locale lang="EN"><title>The Legend of Zelda: Link's Awakening: Dreamer Edition</title><synopsis/></locale><rom version="" name="x"/></game>
  <game name="The Legend of Zelda: Link's Awakening (EN,FR,DE,ES,IT,NL,JA,KO,ZHTW,RU)"><id>AR3NA</id><type>Switch</type><region>ALL</region><locale lang="EN"><title>The Legend of Zelda: Link's Awakening</title><synopsis/></locale><locale lang="FR"><title>The Legend of Zelda: Link's Awakening</title><synopsis/></locale><locale lang="DE"><title>The Legend of Zelda: Link's Awakening</title><synopsis/></locale><locale lang="FI"><title>The Legend of Zelda: Link's Awakening</title><synopsis/></locale><locale lang="JA"><title>ゼルダの伝説 夢をみる島</title><synopsis/></locale><locale lang="KO"><title>젤다의전설 꿈꾸는 섬</title><synopsis/></locale><locale lang="ZHTW"><title>薩爾達傳說 織夢島</title><synopsis/></locale><rom version="0" name="x"/><rom version="1" name="x"/></game>
  <game name="Cadence of Hyrule: Crypt of the NecroDancer Featuring The Legend of Zelda (EN,FR,DE,ES,IT,JA,KO,ZHCN)"><id>ASWBB</id><type>Switch</type><region>KOR</region><locale lang="EN"><title>Cadence of Hyrule: Crypt of the NecroDancer Featuring The Legend of Zelda</title><synopsis/></locale><locale lang="KO"><title>케이던스 오브 하이랄: 크립트 오브 더 네크로댄서 feat. 젤다의 전설</title><synopsis/></locale><rom version="" name="x"/></game>
  <game name="Cadence of Hyrule - Crypt of the NecroDancer Featuring The Legend of Zelda (EN,FR,DE,ES,IT,JA,KO,ZHTW,ZHCN)"><id>ASWBC</id><type>Switch</type><region>JPN</region><locale lang="EN"><title>Cadence of Hyrule - Crypt of the NecroDancer Featuring The Legend of Zelda</title><synopsis/></locale><locale lang="JA"><title>ケイデンス・オブ・ハイラル: クリプト・オブ・ネクロダンサー feat. ゼルダの伝説</title><synopsis/></locale><rom version="0" name="x"/></game>
  <game name="Cadence of Hyrule: Crypt of the NecroDancer featuring The Legend of Zelda (EN,FR,DE,ES,IT,JA,KO,ZHCN)"><id>ASWBD</id><type>Switch</type><region>USA,EUR,TWN</region><locale lang="EN"><title>Cadence of Hyrule: Crypt of the NecroDancer featuring The Legend of Zelda</title><synopsis/></locale><locale lang="FI"><title>Cadence of Hyrule: Crypt of the NecroDancer featuring The Legend of Zelda</title><synopsis/></locale><locale lang="ZHTW"><title>凱登絲勇闖海拉魯: 死靈舞師地牢 feat. 薩爾達傳說</title><synopsis/></locale><locale lang="ZHCN"><title>凯登丝勇闯海拉鲁: 死灵舞师地牢 feat. 萨尔达传说</title><synopsis/></locale><rom version="0" name="x"/></game>
  <game name="Hyrule Warriors: Age of Calamity (JA)"><id>AXEAA</id><type>Switch</type><region>JPN</region><locale lang="EN"><title>Hyrule Warriors: Age of Calamity</title><synopsis/></locale><locale lang="JA"><title>ゼルダ無双 厄災の黙示録</title><synopsis/></locale><rom version="0" name="x"/></game>
  <game name="Hyrule Warriors: Age of Calamity (EN,FR,DE,ES,IT,NL,KO,ZHTW)"><id>AXEAB</id><type>Switch</type><region>USA,EUR,KOR,TWN,CHN</region><locale lang="EN"><title>Hyrule Warriors: Age of Calamity</title><synopsis/></locale><locale lang="FR"><title>Hyrule Warriors : L'Ère du Fléau</title><synopsis/></locale><locale lang="DE"><title>Hyrule Warriors: Zeit der Verheerung</title><synopsis/></locale><locale lang="ES"><title>Hyrule Warriors: La Era del Cataclismo</title><synopsis/></locale><locale lang="IT"><title>Hyrule Warriors: L'era Della calamità</title><synopsis/></locale><locale lang="NO"><title>Hyrule Warriors: Age of Calamity</title><synopsis/></locale><locale lang="FI"><title>Hyrule Warriors: Age of Calamity</title><synopsis/></locale><locale lang="KO"><title>젤다무쌍 대재앙의 시대</title><synopsis/></locale><rom version="0" name="x"/><rom version="1" name="x"/><rom version="2" name="x"/></game>
  <game name="The Legend of Zelda: Tears of the Kingdom (EN,FR,DE,ES,IT,NL,JA,KO,ZHTW,ZHCN,RU)"><id>AXN7A</id><type>Switch</type><region>ALL</region><locale lang="EN"><title>The Legend of Zelda: Tears of the Kingdom</title><synopsis/></locale><locale lang="NO"><title>The Legend of Zelda: Tears of the Kingdom</title><synopsis/></locale><locale lang="FI"><title>The Legend of Zelda: Tears of the Kingdom</title><synopsis/></locale><locale lang="JA"><title>ゼルダの伝説　ティアーズ オブ ザ キングダム</title><synopsis/></locale><locale lang="KO"><title>젤다의 전설 티어스 오브 더 킹덤</title><synopsis/></locale><locale lang="ZHTW"><title>薩爾達傳說王國之淚</title><synopsis/></locale><rom version="0" name="x"/></game>
  <game name="The Legend of Zelda: Skyward Sword HD (EN,FR,DE,ES,IT,NL,JA,KO,ZHCN,RU)"><id>AZ89A</id><type>Switch</type><region>ALL</region><locale lang="EN"><title>The Legend of Zelda: Skyward Sword HD</title><synopsis/></locale><locale lang="NO"><title>The Legend of Zelda: Skyward Sword HD</title><synopsis/></locale><locale lang="FI"><title>The Legend of Zelda: Skyward Sword HD</title><synopsis/></locale><locale lang="JA"><title>ゼルダの伝説 スカイウォードソード HD</title><synopsis/></locale><locale lang="KO"><title>젤다의 전설 스카이워드 소드 HD</title><synopsis/></locale><locale lang="ZHTW"><title>薩爾達傳說 禦天之劍 HD</title><synopsis/></locale><rom version="0" name="x"/></game>
  <game name="The Legend of Zelda: Echoes of Wisdom (EN,FR,DE,ES,IT,NL,PT,JA,KO,ZHTW,ZHCN,RU)"><id>BDGEA</id><type>Switch</type><region>ALL</region><locale lang="EN"><title>The Legend of Zelda: Echoes of Wisdom</title><synopsis/></locale><locale lang="NO"><title>The Legend of Zelda: Echoes of Wisdom</title><synopsis/></locale><locale lang="FI"><title>The Legend of Zelda: Echoes of Wisdom</title><synopsis/></locale><locale lang="JA"><title>ゼルダの伝説 知恵のかりもの</title><synopsis/></locale><rom version="0" name="x"/></game>
  <game name="Beatus Creation Solitaire (EN,JA,ZHTW,ZHCN)"><id>A4CAA</id><type>eShop</type><region>ALL</region><locale lang="EN"><title>Beatus Creation Solitaire</title><synopsis/></locale><rom version="" name="x"/></game>
  <game name="Cadence of Hyrule: Crypt of the NecroDancer featuring The Legend of Zelda + Cadence of Hyrule Season Pass"><id>ASWBE</id><type>eShop</type><region>USA</region><locale lang="EN"><title>Cadence of Hyrule: Crypt of the NecroDancer featuring The Legend of Zelda + Cadence of Hyrule Season Pass</title><synopsis/></locale><rom version="" name="x"/></game>
</datafile>
""";

    private readonly ArtworkTestEnv _env = new(online: true);

    public void Dispose() => _env.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static byte[] SampleZip()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("switchtdb.xml");
            using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            w.Write(SampleXml);
        }
        return ms.ToArray();
    }

    private static List<GameTdbIndexStore.CacheEntry> SampleEntries() =>
        GameTdbIndexStore.ParseXml(new MemoryStream(Encoding.UTF8.GetBytes(SampleXml)));

    private static GameTdbIndex SampleIndex() =>
        new(SampleEntries().Select(e => new GameTdbEntry(e.Id, e.Type, e.Regions, e.Titles)));

    [Fact]
    public void Parses_ids_types_regions_and_every_locale_title()
    {
        var entries = SampleEntries();

        Assert.Equal(16, entries.Count);
        var aoc = Assert.Single(entries, e => e.Id == "AXEAB");
        Assert.Equal("Switch", aoc.Type);
        Assert.Equal("USA,EUR,KOR,TWN,CHN", aoc.Regions);
        Assert.Contains("Hyrule Warriors: Age of Calamity", aoc.Titles);      // name attribute without "(EN,FR,…)"
        Assert.Contains("Hyrule Warriors: Zeit der Verheerung", aoc.Titles);  // DE locale
        Assert.Contains("젤다무쌍 대재앙의 시대", aoc.Titles);
        Assert.Equal(aoc.Titles.Count, aoc.Titles.Distinct().Count());
        Assert.Equal("eShop", Assert.Single(entries, e => e.Id == "ASWBE").Type);
        Assert.Equal(entries.Count, GameTdbIndexStore.Parse(SampleZip(), "switch")!.Count); // the same from the ZIP
    }

    [Theory]
    // the owner's eight Switch games, named like their folders on disk
    [InlineData("The Legend of Zelda - Breath of the Wild", "AAAAA")]        // not AAAAG "+ Expansion Pass" (JPN)
    [InlineData("Hyrule Warriors - Zeit der Verheerung", "AXEAB")]           // German title, via the DE locale
    [InlineData("Hyrule Warriors - Definitive Edition", "AKUTB")]
    [InlineData("Cadence of Hyrule", "ASWBD")]                               // main title; USA,EUR before KOR/JPN, not the Season Pass bundle
    [InlineData("The Legend of Zelda - Link's Awakening", "AR3NA")]          // not the Dreamer Edition
    [InlineData("The Legend of Zelda - Echoes of Wisdom", "BDGEA")]
    [InlineData("The Legend of Zelda - Skyward Sword HD", "AZ89A")]
    [InlineData("The Legend of Zelda - Tears of the Kingdom", "AXN7A")]
    // scene-style names with title-ID tags, colons, other word order
    [InlineData("Hyrule Warriors - Age of Calamity [0100A6301214E000][v0]", "AXEAB")] // the European entry, not the Japanese AXEAA
    [InlineData("The Legend of Zelda: Tears of the Kingdom", "AXN7A")]
    [InlineData("Zelda, The Legend of - Skyward Sword HD", "AZ89A")]
    public void Matches_the_owners_games(string title, string expected) =>
        Assert.Equal(expected, SampleIndex().Match(title));

    [Theory]
    [InlineData("Zelda")]                 // one word: never a main-title guess
    [InlineData("Pokemon Karmesin")]      // not in the database
    [InlineData("Beatus")]
    public void Does_not_guess(string title) => Assert.Null(SampleIndex().Match(title));

    [Fact]
    public void Urls_follow_gametdbs_scheme_and_reject_odd_input()
    {
        Assert.Equal("https://www.gametdb.com/switchtdb.zip?LANG=DE", GameTdb.DatabaseUrl("switch").ToString());
        Assert.Equal("https://art.gametdb.com/switch/coverM/DE/AXEAB.jpg", GameTdb.CoverUrl("switch", "DE", "AXEAB").ToString());
        Assert.Throws<ArgumentException>(() => GameTdb.CoverUrl("switch", "DE", "../x"));
        Assert.Throws<ArgumentException>(() => GameTdb.DatabaseUrl("Switch/../"));
        Assert.Equal(["DE", "EN", "US"], GameTdb.CoverLanguages);
    }

    private Game AddSwitchGame(string folderAndFile) => _env.AddGame("games/switch/" + folderAndFile, "switch");

    /// <summary>www.gametdb.com serves the sample ZIP; art.gametdb.com serves a JPEG for the given "LANG/ID" keys.</summary>
    private void Serve(params string[] covers)
    {
        var have = covers.ToHashSet(StringComparer.Ordinal);
        var zip = SampleZip();
        _env.Http.Handler = req =>
        {
            var u = req.RequestUri!;
            if (u.Host == "www.gametdb.com")
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
                r.Headers.ETag = new EntityTagHeaderValue("\"abc\"");
                return r;
            }
            if (u.Host == "art.gametdb.com")
            {
                var parts = u.AbsolutePath.Split('/'); // /switch/coverM/DE/AXEAB.jpg
                var key = parts[3] + "/" + Path.GetFileNameWithoutExtension(parts[4]);
                if (have.Contains(key)) return FakeHttp.Bytes(Jpeg());
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html>404</html>") };
        };
    }

    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0];

    private string[] ArtRequests()
    {
        lock (_env.Http.Requests)
            return _env.Http.Requests.Where(r => r.Url.Host == "art.gametdb.com").Select(r => r.Url.AbsolutePath).ToArray();
    }

    [Fact]
    public async Task Downloads_the_german_cover_as_jpg_into_the_switch_folder()
    {
        Serve("DE/AXEAB");
        var g = AddSwitchGame("Hyrule Warriors - Zeit der Verheerung/Hyrule Warriors - Zeit der Verheerung.nsp");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(1, r.Downloaded);
        Assert.Equal("artwork/covers/switch/Hyrule Warriors - Zeit der Verheerung.jpg", _env.Reload(g).CoverPath);
        Assert.True(File.Exists(_env.Full("artwork/covers/switch/Hyrule Warriors - Zeit der Verheerung.jpg")));
        Assert.Equal(["/switch/coverM/DE/AXEAB.jpg"], ArtRequests());
        Assert.All(_env.Http.Requests, q => Assert.StartsWith("RetroHub/0.1", q.UserAgent));
        Assert.Equal(0, _env.Http.GitHubCount + _env.Http.ThumbnailCount); // never asks libretro for Switch
        Assert.True(File.Exists(Path.Combine(_env.Paths.Metadata, "gametdb", "switch.json")));
    }

    [Fact]
    public async Task A_miss_left_by_the_old_libretro_lookup_does_not_block_gametdb()
    {
        Serve("DE/AXEAB");
        var g = AddSwitchGame("Hyrule Warriors - Zeit der Verheerung/Hyrule Warriors - Zeit der Verheerung.nsp");
        using (var old = _env.Service())
        {
            old.Misses.AddMiss(ArtworkMissCache.KeyFor("switch", ArtworkNames.Stem(g))); // libretro had no Switch repository
            old.Misses.Save();
        }

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(1, r.Downloaded);
        Assert.Equal(0, r.SkippedRecentMiss);
    }

    [Fact]
    public async Task Falls_back_from_german_to_english_when_there_is_no_german_box()
    {
        Serve("EN/ASWBD", "US/ASWBD");
        var g = AddSwitchGame("Cadence of Hyrule/Cadence of Hyrule.nsp");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(1, r.Downloaded);
        Assert.Equal("artwork/covers/switch/Cadence of Hyrule.jpg", _env.Reload(g).CoverPath);
        Assert.Equal(["/switch/coverM/DE/ASWBD.jpg", "/switch/coverM/EN/ASWBD.jpg"], ArtRequests());
    }

    [Fact]
    public async Task Unknown_games_and_missing_covers_are_misses_and_not_asked_again()
    {
        Serve(); // the database knows Skyward Sword, but no language has its cover
        AddSwitchGame("Pokemon Karmesin/Pokemon Karmesin.nsp");
        AddSwitchGame("The Legend of Zelda - Skyward Sword HD/The Legend of Zelda - Skyward Sword HD.xci");

        var first = await _env.Service().ResolveAsync(null, null, Ct);
        Assert.Equal(2, first.NotFound);
        Assert.Equal(3, ArtRequests().Length); // DE, EN, US for Skyward Sword; nothing for the unknown game
        var before = _env.Http.Count;

        var second = await _env.Service().ResolveAsync(null, null, Ct);
        Assert.Equal(2, second.SkippedRecentMiss);
        Assert.Equal(before, _env.Http.Count);
    }

    [Fact]
    public async Task Database_is_fetched_once_and_a_304_only_renews_the_cache()
    {
        var zip = SampleZip();
        var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        var conditional = new List<string>();
        _env.Http.Handler = req =>
        {
            if (req.Headers.IfNoneMatch.Count > 0)
            {
                conditional.Add(req.Headers.IfNoneMatch.First().Tag);
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
            r.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
            return r;
        };
        var dir = Path.Combine(_env.Root, "gametdb");
        using var http = new HttpClient(_env.Http);

        var a = await new GameTdbIndexStore(dir, http, () => now).GetAsync("switch", Ct);
        var b = await new GameTdbIndexStore(dir, http, () => now.AddDays(29)).GetAsync("switch", Ct); // fresh cache: no request
        Assert.Equal(1, _env.Http.Count);
        Assert.Equal(16, a!.Count);
        Assert.Equal("AXEAB", b!.Match("Hyrule Warriors - Zeit der Verheerung"));

        var c = await new GameTdbIndexStore(dir, http, () => now.AddDays(31)).GetAsync("switch", Ct); // stale: conditional request → 304
        Assert.Equal(["\"v1\""], conditional);
        Assert.Equal("AXN7A", c!.Match("The Legend of Zelda - Tears of the Kingdom"));
        var d = await new GameTdbIndexStore(dir, http, () => now.AddDays(32)).GetAsync("switch", Ct); // the 304 renewed the date
        Assert.NotNull(d);
        Assert.Equal(2, _env.Http.Count);
    }

    [Fact]
    public async Task An_unreachable_database_is_an_error_not_a_miss()
    {
        _env.Http.Handler = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        AddSwitchGame("Cadence of Hyrule/Cadence of Hyrule.nsp");

        var r = await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(1, r.Errors);
        Assert.Empty(ArtRequests());
        var again = await _env.Service().ResolveAsync(null, null, Ct); // asked again on the next run, not remembered as a miss
        Assert.Equal(1, again.Errors);
        Assert.Equal(0, again.SkippedRecentMiss);
    }

    [Fact]
    public async Task Online_off_sends_nothing_to_gametdb()
    {
        Serve("DE/AXEAB");
        _env.Settings.Current.OnlineCovers = false;
        AddSwitchGame("Hyrule Warriors - Zeit der Verheerung/Hyrule Warriors - Zeit der Verheerung.nsp");

        await _env.Service().ResolveAsync(null, null, Ct);

        Assert.Equal(0, _env.Http.Count);
    }

    [Theory]
    [InlineData("Zelda [01007EF00011E800][v786432].nsp", true)]   // update (…800)
    [InlineData("Zelda [01007EF00011F001][v0].nsp", true)]        // DLC
    [InlineData("Zelda [01007EF00011E000][v0].xci", false)]       // base game (…000)
    [InlineData("Breath of the Wild - Update v1.9.0.nsp", true)]
    [InlineData("X - Update v1.4.1.nsp", true)]
    [InlineData("X - DLC 1 (Version 3).nsp", true)]
    [InlineData("X (DLC).nsz", true)]
    [InlineData("Updated Edition.nsp", false)]
    [InlineData("DLC Quest.nsp", false)]
    [InlineData("Cadence of Hyrule.nsp", false)]
    [InlineData("Zelda - Update v1.1.nes", false)]                // only Switch packages
    public void Recognises_loose_switch_updates_and_dlc(string file, bool addOn) =>
        Assert.Equal(addOn, ScanRules.IsSwitchAddOn(file));
}
