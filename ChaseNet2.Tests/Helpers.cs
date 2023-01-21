using ChaseNet2.Transport;

namespace ChaseNet2.Tests;

public class Helpers
{
    /// <summary>
    /// Run a few update cycles asynchronously
    /// </summary>
    /// <param name="managers"></param>
    public static async Task UpdateManagers(params ConnectionManager[] managers)
    {
        for (int i = 0; i < 200; i++)
        {
            await Task.Delay(5);
            foreach (var manager in managers)
            {
                await manager.Update();
            }
        }
    }
}