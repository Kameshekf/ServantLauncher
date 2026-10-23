using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ServantLauncher.Services
{
    public static class MemoryOptimizer
    {
        [DllImport("kernel32.dll", EntryPoint = "SetProcessWorkingSetSize")]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minSize, IntPtr maxSize);

        /// <summary>
        /// Flushes unused memory pages to system pagefile and performs aggressive GC cleanup.
        /// Dramatically reduces working set in background/tray (down to ~15-25 MB).
        /// </summary>
        public static void TrimWorkingSet()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);

                if (OperatingSystem.IsWindows())
                {
                    using var currentProc = Process.GetCurrentProcess();
                    SetProcessWorkingSetSize(currentProc.Handle, -1, -1);
                }
            }
            catch { }
        }

        /// <summary>
        /// Gets current working set in megabytes.
        /// </summary>
        public static long GetCurrentWorkingSetMb()
        {
            try
            {
                using var proc = Process.GetCurrentProcess();
                proc.Refresh();
                return proc.WorkingSet64 / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }
    }
}
