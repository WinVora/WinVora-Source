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
            try
            {
                await action();
            }
            finally
            {
                Logger.Log($"Startphase '{stage}': {timer.ElapsedMilliseconds} ms.");
            }
        }

        public static async Task<T> MeasureAsync<T>(string stage, Func<Task<T>> action)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                return await action();
            }
            finally
            {
                Logger.Log($"Startphase '{stage}': {timer.ElapsedMilliseconds} ms.");
            }
        }
    }
}
