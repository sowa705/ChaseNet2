using System.Diagnostics;
using ChaseNet2.Transport;

namespace ChaseNet2.Tests;

public class Helpers
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Run a few update cycles asynchronously
    /// </summary>
    /// <param name="managers"></param>
    public static async Task UpdateManagers(params ConnectionManager[] managers)
    {
        for (int i = 0; i < 10; i++)
        {
            await Tick(managers);
        }
    }

    /// <summary>
    /// Update the managers until the condition holds, so a test takes as long as the exchange does
    /// instead of a fixed number of ticks. Returns false if the timeout elapsed first.
    /// </summary>
    public static Task<bool> UpdateUntil(Func<bool> condition, params ConnectionManager[] managers)
        => UpdateUntil(condition, DefaultTimeout, managers);

    public static async Task<bool> UpdateUntil(Func<bool> condition, TimeSpan timeout, params ConnectionManager[] managers)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > timeout)
            {
                return false;
            }

            await Tick(managers);
        }

        return true;
    }

    private static async Task Tick(ConnectionManager[] managers)
    {
        await Task.Delay(5);
        foreach (var manager in managers)
        {
            await manager.Update();
        }
    }
}
