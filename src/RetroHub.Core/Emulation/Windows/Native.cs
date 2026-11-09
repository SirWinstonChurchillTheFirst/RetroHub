using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RetroHub.Core.Emulation.Windows;

/// <summary>Owned handle of a Win32 Job Object.</summary>
internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeJobHandle() : base(true) { }
    protected override bool ReleaseHandle() => Native.CloseHandle(handle);
}

/// <summary>
/// Source-generated P/Invoke declarations (x64 Windows only) for process/job control and window embedding.
/// Every caller guards with <see cref="OperatingSystem.IsWindows"/> or runs only on Windows code paths.
/// </summary>
internal static unsafe partial class Native
{
    // ---- process creation -------------------------------------------------------------------------------------
    public const uint CREATE_SUSPENDED = 0x00000004;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
    public const uint CREATE_NO_WINDOW = 0x08000000;
    public const int STARTF_USESHOWWINDOW = 0x00000001;

    public const uint PROCESS_TERMINATE = 0x0001;
    public const uint PROCESS_SET_QUOTA = 0x0100;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint SYNCHRONIZE = 0x00100000;

    public const uint STILL_ACTIVE = 259;
    public const uint WAIT_OBJECT_0 = 0;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_MORE_DATA = 234;

    // ---- job objects ------------------------------------------------------------------------------------------
    public const int JobObjectBasicAccountingInformation = 1;
    public const int JobObjectBasicProcessIdList = 3;

    // ---- windows ----------------------------------------------------------------------------------------------
    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const long WS_CHILD = 0x40000000, WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000,
        WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000,
        WS_MAXIMIZEBOX = 0x00010000, WS_MAXIMIZE = 0x01000000, WS_MINIMIZE = 0x20000000, WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const long WS_EX_APPWINDOW = 0x00040000, WS_EX_WINDOWEDGE = 0x100, WS_EX_CLIENTEDGE = 0x200,
        WS_EX_DLGMODALFRAME = 0x1, WS_EX_TOOLWINDOW = 0x80;
    public const uint SS_BLACKRECT = 0x4;

    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10,
        SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40, SWP_NOCOPYBITS = 0x100;

    public const int SW_HIDE = 0, SW_SHOWNORMAL = 1, SW_SHOWNOACTIVATE = 4, SW_SHOWMINNOACTIVE = 7, SW_RESTORE = 9;

    public const uint GW_HWNDNEXT = 2, GW_OWNER = 4, GW_CHILD = 5;
    public const uint GA_ROOT = 2;

    public const uint WM_NULL = 0x0000, WM_CLOSE = 0x0010;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    public const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002;
    public const ushort VK_MENU = 0x12;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    /// <summary>STARTUPINFOW, blittable (strings are left null).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFOW
    {
        public int cb;
        public nint lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public ushort wShowWindow, cbReserved2;
        public nint lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public nint hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
    {
        public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
    }

    /// <summary>INPUT with the KEYBDINPUT member of the union (x64 layout: 40 bytes).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct KEYBOARD_INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public ushort wVk;
        [FieldOffset(10)] public ushort wScan;
        [FieldOffset(12)] public uint dwFlags;
        [FieldOffset(16)] public uint time;
        [FieldOffset(24)] public nint dwExtraInfo;
    }

    // ---- kernel32 ---------------------------------------------------------------------------------------------
    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateProcess(string? applicationName, char* commandLine, nint processAttributes, nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, char* environment, string? currentDirectory,
        STARTUPINFOW* startupInfo, PROCESS_INFORMATION* processInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint ResumeThread(nint thread);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* buffer, ref uint size);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsProcessInJob(nint process, nint job, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeJobHandle CreateJobObject(nint attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryInformationJobObject(SafeJobHandle job, int infoClass, void* info, uint length, uint* returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateJobObject(SafeJobHandle job, uint exitCode);

    // ---- user32 -----------------------------------------------------------------------------------------------
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsHungAppWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindow(nint hwnd, uint cmd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint SetParent(nint child, nint newParent);

    [LibraryImport("user32.dll")]
    public static partial nint GetParent(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(nint hwnd, ref POINT point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint hwnd, ref POINT point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustWindowRectEx(ref RECT rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint exStyle);

    [LibraryImport("user32.dll")]
    public static partial nint GetMenu(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DrawMenuBar(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BringWindowToTop(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);

    [LibraryImport("user32.dll")]
    public static partial nint SetFocus(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    public static partial nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    public static partial int GetWindowText(nint hwnd, char* buffer, int maxCount);

    [LibraryImport("user32.dll")]
    public static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint SendInput(uint count, KEYBOARD_INPUT* inputs, int size);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    // ---- gdi32 ------------------------------------------------------------------------------------------------
    [LibraryImport("gdi32.dll")]
    public static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint obj);

    // ---- helpers ----------------------------------------------------------------------------------------------
    public static long GetStyle(nint hwnd) => (long)GetWindowLongPtr(hwnd, GWL_STYLE);
    public static long GetExStyle(nint hwnd) => (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);

    /// <summary>The window's caption as kept by the system (does not send WM_GETTEXT to another process, never blocks).</summary>
    public static string GetTitle(nint hwnd)
    {
        if (hwnd == 0) return "";
        const int max = 512;
        char* buffer = stackalloc char[max];
        int n = GetWindowText(hwnd, buffer, max);
        return n > 0 ? new string(buffer, 0, n) : "";
    }

    public static int ProcessIdOf(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        return (int)pid;
    }

    /// <summary>Full image path of a process via QueryFullProcessImageName (works for most processes, even elevated ones).</summary>
    public static string? ImagePathOf(int pid)
    {
        using var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h.IsInvalid) return null;
        return ImagePathOf(h);
    }

    public static string? ImagePathOf(SafeProcessHandle h)
    {
        const int max = 32768;
        var buffer = new char[max];
        fixed (char* p = buffer)
        {
            uint size = max;
            return QueryFullProcessImageName(h, 0, p, ref size) ? new string(p, 0, (int)size) : null;
        }
    }
}
