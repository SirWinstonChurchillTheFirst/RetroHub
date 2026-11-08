using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RetroHub.Core;
using RetroHub.Core.Catalog;
using RetroHub.Core.Emulation;
using RetroHub.Core.Emulation.Windows;
using RetroHub.Core.Library;
using Xunit;

namespace RetroHub.Core.Tests.Emulation.Runtime;

/// <summary>Tests that start real processes run one after another (timing-sensitive, and they share ping.exe).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RuntimeCollection
{
    public const string Name = "Runtime processes";
}

/// <summary>In-memory repository: records sessions, everything else minimal.</summary>
internal sealed class FakeRepository : IGameRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Game> _games = new();
    public List<PlaySession> Added { get; } = new();
    public event Action? Changed;

    public PlaySession[] SessionsSnapshot() { lock (_gate) return Added.ToArray(); }

    public IReadOnlyList<Game> Query(GameQuery query) { lock (_gate) return _games.Values.ToList(); }
    public Game? Get(long id) { lock (_gate) return _games.GetValueOrDefault(id); }
    public Game? GetByPath(string portablePath) { lock (_gate) return _games.Values.FirstOrDefault(g => g.Path == portablePath); }
    public long Upsert(Game game)
    {
        lock (_gate)
        {
            if (game.Id == 0) game.Id = _games.Count + 1;
            _games[game.Id] = game;
        }
        Changed?.Invoke();
        return game.Id;
    }
    public void UpsertMany(IEnumerable<Game> games) { foreach (var g in games) Upsert(g); }
    public void Delete(long id) { lock (_gate) _games.Remove(id); }
    public void SetFavorite(long id, bool favorite) { lock (_gate) if (_games.TryGetValue(id, out var g)) g.Favorite = favorite; }
    public void SetHidden(long id, bool hidden) { lock (_gate) if (_games.TryGetValue(id, out var g)) g.Hidden = hidden; }
    public void AddSession(PlaySession session)
    {
        lock (_gate)
        {
            Added.Add(session);
            if (_games.TryGetValue(session.GameId, out var g))
            {
                g.PlaySeconds += session.Seconds;
                g.PlayCount++;
                g.LastPlayedUtc = session.EndUtc;
            }
        }
        Changed?.Invoke();
    }
    public IReadOnlyList<PlaySession> Sessions(long gameId, int limit = 50) { lock (_gate) return Added.Where(s => s.GameId == gameId).Take(limit).ToList(); }
    public LibraryStats Stats() => new(0, 0, 0, 0, new Dictionary<string, int>(), new Dictionary<string, long>());
    public IReadOnlyList<LibraryFolder> Folders() => [];
    public long UpsertFolder(LibraryFolder folder) => 0;
    public void DeleteFolder(long id) { }
    public string? GetState(string key) => null;
    public void SetState(string key, string? value) { }
    public void Dispose() { }
}

internal sealed class FakePortableSetup(string id, bool fail = false) : IPortableSetup
{
    public string Id { get; } = id;
    public int Calls;
    public void Apply(EmulatorInstall emulator, LaunchPlan plan, AppPaths paths)
    {
        Interlocked.Increment(ref Calls);
        if (fail) throw new IOException("simulated recipe failure");
    }
}

/// <summary>A temp portable root that is deleted afterwards.</summary>
internal sealed class TempRoot : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "RetroHub-runtime-tests", Guid.NewGuid().ToString("N"));
    public AppPaths Paths { get; }

    public TempRoot()
    {
        Directory.CreateDirectory(Dir);
        Paths = new AppPaths(Dir, Dir);
        Directory.CreateDirectory(Paths.Cache);
    }

    public void Dispose()
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(300);
            }
        }
    }
}

internal static class Runtime
{
    public static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");
    public static string Ping => Path.Combine(Environment.SystemDirectory, "PING.EXE");

    public static LaunchPlan Plan(string file, IEnumerable<string> args, EmulatorInstall? emulator = null, LaunchMode mode = LaunchMode.Windowed,
        IReadOnlyDictionary<string, string>? env = null, bool shell = false, long gameId = 42, IReadOnlyList<LaunchProblem>? problems = null,
        string? workingDir = null) => new()
    {
        Game = new Game { Id = gameId, SystemId = "test", Path = "games/test/x.bin", Title = "Testspiel" },
        System = new SystemDef { Id = "test", Name = "Test" },
        FileName = file,
        Arguments = args.ToList(),
        Emulator = emulator,
        Mode = mode,
        Environment = env ?? new Dictionary<string, string>(),
        UseShellExecute = shell,
        Problems = problems ?? [],
        WorkingDirectory = workingDir,
    };

    /// <summary>A service for console-program tests: CREATE_NO_WINDOW so nothing pops up and nothing has a window.</summary>
    public static SessionService Service(FakeRepository repo, TempRoot root, params IPortableSetup[] setups) => new(repo, root.Paths, setups)
    {
        ExtraCreationFlags = Native.CREATE_NO_WINDOW,
        ShellWindowStyle = ProcessWindowStyle.Hidden,
    };

    public static Task<IGameSession> EndedTask(IGameSession session)
    {
        var tcs = new TaskCompletionSource<IGameSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Ended += s => tcs.TrySetResult(s);
        return tcs.Task;
    }

    public static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, string what)
    {
        var done = await Task.WhenAny(task, Task.Delay(timeout));
        if (done != task) throw new TimeoutException($"{what} did not happen within {timeout.TotalSeconds:0.#} s");
        return await task;
    }

    public static bool IsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    public static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout, int stepMs = 50)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition()) return true;
            await Task.Delay(stepMs);
        }
        return condition();
    }
}

/// <summary>Test-only Win32 calls (classic DllImport: the test project has no unsafe code).</summary>
internal static class TestWin32
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CommandLineToArgvW(string cmdLine, out int numArgs);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr mem);

    public static string[] ParseCommandLine(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out int n);
        if (argv == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try
        {
            var result = new string[n];
            for (int i = 0; i < n; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!;
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName, hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int ptX, ptY;
        public uint lPrivate;
    }

    [DllImport("user32.dll", SetLastError = true)] public static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")] public static extern bool PostThreadMessageW(uint thread, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr h, int index);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out Native.RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, System.Text.StringBuilder sb, int max);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] public static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();

    public const uint WM_APP = 0x8000, WM_QUIT = 0x0012;
    public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000;

    public static string ClassName(IntPtr h)
    {
        var sb = new System.Text.StringBuilder(256);
        GetClassNameW(h, sb, 256);
        return sb.ToString();
    }

    public static long Style(IntPtr h) => (long)GetWindowLongPtrW(h, -16);
}

/// <summary>
/// A top-level Win32 window on its own STA thread with a message loop (stands in for RetroHub's Avalonia window),
/// with a host created by <see cref="WindowEmbedder.CreateHostWindow"/> filling its client area.
/// </summary>
internal sealed class TestWindowThread : IDisposable
{
    private static readonly Lazy<string> WindowClass = new(Register);
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private Exception? _startError;

    public IntPtr Top { get; private set; }
    public IntPtr Host { get; private set; }
    public int HostWidth { get; private set; }
    public int HostHeight { get; private set; }

    public TestWindowThread(string title, int width, int height, bool withHost = true)
    {
        _thread = new Thread(() => Run(title, width, height, withHost)) { IsBackground = true, Name = "Test window" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("test window thread did not start");
        if (_startError != null) throw new InvalidOperationException("test window could not be created", _startError);
    }

    private static string Register()
    {
        const string name = "RetroHubRuntimeTestWindow";
        var wc = new TestWin32.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<TestWin32.WNDCLASSEXW>(),
            lpfnWndProc = TestWin32.GetProcAddress(TestWin32.GetModuleHandleW("user32.dll"), "DefWindowProcW"),
            hInstance = TestWin32.GetModuleHandleW(null),
            hbrBackground = (IntPtr)(5 + 1), // COLOR_WINDOW + 1
            lpszClassName = Marshal.StringToHGlobalUni(name), // lives for the process
        };
        if (TestWin32.RegisterClassExW(ref wc) == 0 && Marshal.GetLastWin32Error() != 1410 /* already exists */)
            throw new System.ComponentModel.Win32Exception();
        return name;
    }

    private void Run(string title, int width, int height, bool withHost)
    {
        try
        {
            // like Avalonia: per-monitor DPI aware v2
            TestWin32.SetThreadDpiAwarenessContext(new IntPtr(-4));
            _threadId = TestWin32.GetCurrentThreadId();
            Top = TestWin32.CreateWindowExW(0, WindowClass.Value, title, TestWin32.WS_OVERLAPPEDWINDOW | TestWin32.WS_VISIBLE | TestWin32.WS_CLIPCHILDREN,
                120, 120, width, height, IntPtr.Zero, IntPtr.Zero, TestWin32.GetModuleHandleW(null), IntPtr.Zero);
            if (Top == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            if (withHost)
            {
                Host = WindowEmbedder.CreateHostWindow(Top);
                if (Host == IntPtr.Zero) throw new InvalidOperationException("CreateHostWindow returned 0");
                TestWin32.GetClientRect(Top, out var rc);
                HostWidth = rc.Width;
                HostHeight = rc.Height;
                TestWin32.SetWindowPos(Host, IntPtr.Zero, 0, 0, HostWidth, HostHeight, 0x4 | 0x10 /* NOZORDER|NOACTIVATE */);
            }
        }
        catch (Exception ex)
        {
            _startError = ex;
            _ready.Set();
            return;
        }
        _ready.Set();
        while (TestWin32.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.hwnd == IntPtr.Zero && msg.message == TestWin32.WM_APP)
            {
                while (_queue.TryDequeue(out var a)) a();
                continue;
            }
            TestWin32.TranslateMessage(ref msg);
            TestWin32.DispatchMessageW(ref msg);
        }
    }

    /// <summary>Runs on the window thread and waits for it.</summary>
    public T Invoke<T>(Func<T> f)
    {
        var tcs = new TaskCompletionSource<T>();
        _queue.Enqueue(() =>
        {
            try { tcs.SetResult(f()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        TestWin32.PostThreadMessageW(_threadId, TestWin32.WM_APP, IntPtr.Zero, IntPtr.Zero);
        if (!tcs.Task.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("window thread did not answer");
        return tcs.Task.Result;
    }

    public void Dispose()
    {
        try
        {
            Invoke(() =>
            {
                if (Host != IntPtr.Zero) WindowEmbedder.DestroyHostWindow(Host);
                TestWin32.DestroyWindow(Top);
                return 0;
            });
        }
        catch (TimeoutException)
        {
        }
        TestWin32.PostThreadMessageW(_threadId, TestWin32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}

/// <summary>Screen capture of a window's area (for checking that an embedded picture is really drawn) + tiny PNG writer.</summary>
internal static class ScreenCapture
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Native.RECT r);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    /// <summary>BGRA pixels (top-down) of the screen area covered by the window. Call on a DPI-aware thread.</summary>
    public static (int W, int H, byte[] Bgra) Capture(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var r);
        int w = Math.Max(1, r.Width), h = Math.Max(1, r.Height);
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(mem, bmp);
        BitBlt(mem, 0, 0, w, h, screen, r.Left, r.Top, 0x00CC0020 | 0x40000000 /* SRCCOPY | CAPTUREBLT */);
        SelectObject(mem, old);
        var info = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var bits = new byte[w * h * 4];
        GetDIBits(mem, bmp, 0, (uint)h, bits, ref info, 0);
        DeleteObject(bmp);
        DeleteDC(mem);
        ReleaseDC(IntPtr.Zero, screen);
        return (w, h, bits);
    }

    public static double NonBlackFraction(byte[] bgra)
    {
        long n = 0;
        for (int i = 0; i < bgra.Length; i += 4)
            if (bgra[i] > 24 || bgra[i + 1] > 24 || bgra[i + 2] > 24) n++;
        return n / (double)(bgra.Length / 4);
    }

    public static void SavePng(string path, int w, int h, byte[] bgra)
    {
        var raw = new byte[h * (w * 3 + 1)];
        for (int y = 0; y < h; y++)
        {
            int o = y * (w * 3 + 1);
            raw[o] = 0; // filter: none
            for (int x = 0; x < w; x++)
            {
                int s = (y * w + x) * 4, d = o + 1 + x * 3;
                raw[d] = bgra[s + 2];
                raw[d + 1] = bgra[s + 1];
                raw[d + 2] = bgra[s];
            }
        }
        using var ms = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw);
        using var f = File.Create(path);
        f.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8; ihdr[9] = 2; // 8-bit RGB
        Chunk(f, "IHDR", ihdr);
        Chunk(f, "IDAT", ms.ToArray());
        Chunk(f, "IEND", []);
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typed = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(typed, 0);
        data.CopyTo(typed, 4);
        s.Write(typed);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, Crc32(typed));
        s.Write(len);
    }

    private static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in data)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return ~c;
    }
}
