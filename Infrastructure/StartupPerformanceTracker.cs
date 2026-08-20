using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace WinVora
{
    internal static class StartupPerformanceTracker
    {
        public static async Task MeasureAsync(string stage, Func<Task> action)
        {
            var timer = Stopwatch.StartNew();
            long memoryBefore = Process.GetCurrentProcess().WorkingSet64;
            try
            {
                await action();
            }
            finally
            {
                LogResult(stage, timer, memoryBefore);
            }
        }

        public static async Task<T> MeasureAsync<T>(string stage, Func<Task<T>> action)
        {
            var timer = Stopwatch.StartNew();
            long memoryBefore = Process.GetCurrentProcess().WorkingSet64;
            try
            {
                return await action();
            }
            finally
            {
                LogResult(stage, timer, memoryBefore);
            }
        }

        private static void LogResult(string stage, Stopwatch timer, long memoryBefore)
        {
            using var process = Process.GetCurrentProcess();
            long memoryAfter = process.WorkingSet64;
            long privateMemory = process.PrivateMemorySize64;
            long managedMemory = GC.GetTotalMemory(forceFullCollection: false);
            double deltaMb = (memoryAfter - memoryBefore) / 1024d / 1024d;
            string level = timer.ElapsedMilliseconds >= 5000 ? "LANGSAM · " : string.Empty;
            Logger.Log($"{level}Startphase '{stage}': {timer.ElapsedMilliseconds} ms; " +
                $"Working Set {memoryAfter / 1024d / 1024d:0.0} MB ({deltaMb:+0.0;-0.0;0.0} MB); " +
                $"privat {privateMemory / 1024d / 1024d:0.0} MB; verwaltet {managedMemory / 1024d / 1024d:0.0} MB.");
        }
    }
}
