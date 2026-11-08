using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using RetroHub.Core.Logging;

namespace RetroHub.Core.Emulation.Windows;

/// <summary>
/// An unnamed Win32 Job Object used to follow a game's whole process tree (launcher stubs, child processes).
/// Deliberately created WITHOUT JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE: if RetroHub crashes or is closed, the
/// game keeps running (and can be re-attached on the next start). All methods are thread-safe and never throw.
/// </summary>
internal sealed class JobObject : IDisposable
{
    private readonly SafeJobHandle _handle;

    private JobObject(SafeJobHandle handle) { _handle = handle; }

    public static JobObject? TryCreate()
    {
        try
        {
            var h = Native.CreateJobObject(0, null);
            if (h.IsInvalid)
            {
                Log.Warn($"CreateJobObject failed: {new Win32Exception().Message}");
                h.Dispose();
                return null;
            }
            return new JobObject(h);
        }
        catch (Exception ex)
        {
            Log.Warn("CreateJobObject failed", ex);
            return null;
        }
    }

    /// <summary>Puts a process into the job. Fails e.g. when an enclosing job forbids nesting (pre-Windows 8 rules).</summary>
    public bool TryAssign(SafeProcessHandle process, out int error)
    {
        error = 0;
        try
        {
            if (Native.AssignProcessToJobObject(_handle, process)) return true;
            error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            return false;
        }
        catch (ObjectDisposedException)
        {
            error = -1;
            return false;
        }
    }

    /// <summary>Number of live processes in the job; null when it cannot be queried.</summary>
    public unsafe int? ActiveProcesses()
    {
        try
        {
            Native.JOBOBJECT_BASIC_ACCOUNTING_INFORMATION info;
            uint len;
            if (!Native.QueryInformationJobObject(_handle, Native.JobObjectBasicAccountingInformation, &info, (uint)sizeof(Native.JOBOBJECT_BASIC_ACCOUNTING_INFORMATION), &len))
                return null;
            return (int)info.ActiveProcesses;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>Ids of all live processes in the job (buffer grows as needed); null when it cannot be queried.</summary>
    public unsafe IReadOnlyList<int>? ProcessIds()
    {
        try
        {
            int capacity = 32;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                // JOBOBJECT_BASIC_PROCESS_ID_LIST: DWORD NumberOfAssignedProcesses; DWORD NumberOfProcessIdsInList; ULONG_PTR ProcessIdList[]
                int bytes = 8 + capacity * sizeof(nuint);
                var buffer = new byte[bytes];
                fixed (byte* p = buffer)
                {
                    uint len;
                    bool ok = Native.QueryInformationJobObject(_handle, Native.JobObjectBasicProcessIdList, p, (uint)bytes, &len);
                    uint assigned = *(uint*)p, inList = *(uint*)(p + 4);
                    if (!ok && System.Runtime.InteropServices.Marshal.GetLastPInvokeError() != Native.ERROR_MORE_DATA) return null;
                    if (ok && inList >= assigned)
                    {
                        var ids = new int[inList];
                        var list = (nuint*)(p + 8);
                        for (int i = 0; i < inList; i++) ids[i] = (int)list[i];
                        return ids;
                    }
                    capacity = (int)Math.Max(assigned + 16, (uint)capacity * 2);
                }
            }
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>Kills every process in the job.</summary>
    public bool Terminate(uint exitCode)
    {
        try
        {
            return Native.TerminateJobObject(_handle, exitCode);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Closes the handle only; the processes keep running (no kill-on-close).</summary>
    public void Dispose() => _handle.Dispose();
}
