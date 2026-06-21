using System;
using System.Collections.Generic;
using System.Linq;

namespace FindNeedleUX.Services.Mcp;

/// <summary>
/// In-memory record of recent MCP client activity: a bounded ring buffer of the last commands and a
/// roll-up of which clients have connected. Lets the UI answer "who is connected and what did they run?"
/// Static because there's a single in-app MCP server. Thread-safe (locked); cheap.
/// </summary>
public static class McpActivityLog
{
    public sealed record CommandEntry(DateTime TimeUtc, string Client, string Method, string Tool, string Detail);

    public sealed record ClientInfo(string Name, DateTime FirstSeenUtc, DateTime LastSeenUtc, int Commands);

    private const int Capacity = 100;
    private static readonly object Sync = new();
    private static readonly LinkedList<CommandEntry> Recent = new();
    private static readonly Dictionary<string, (DateTime First, DateTime Last, int Count)> Clients = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised (off the UI thread) whenever a command is recorded.</summary>
    public static event Action Updated;

    public static void Record(string client, string method, string tool = null, string detail = null)
    {
        client = string.IsNullOrWhiteSpace(client) ? "unknown" : client.Trim();
        var now = DateTime.UtcNow;
        lock (Sync)
        {
            Recent.AddLast(new CommandEntry(now, client, method ?? "", tool, detail));
            while (Recent.Count > Capacity) Recent.RemoveFirst();

            if (Clients.TryGetValue(client, out var c))
                Clients[client] = (c.First, now, c.Count + 1);
            else
                Clients[client] = (now, now, 1);
        }
        try { Updated?.Invoke(); } catch { }
    }

    /// <summary>The most recent commands, newest first (default 10).</summary>
    public static IReadOnlyList<CommandEntry> RecentCommands(int count = 10)
    {
        lock (Sync)
            return Recent.Reverse().Take(Math.Max(0, count)).ToList();
    }

    /// <summary>Distinct clients seen, most-recently-active first.</summary>
    public static IReadOnlyList<ClientInfo> KnownClients()
    {
        lock (Sync)
            return Clients
                .Select(kv => new ClientInfo(kv.Key, kv.Value.First, kv.Value.Last, kv.Value.Count))
                .OrderByDescending(c => c.LastSeenUtc)
                .ToList();
    }

    /// <summary>Clients active within the given window (default 2 min) — "currently connected".</summary>
    public static IReadOnlyList<ClientInfo> ActiveClients(TimeSpan? within = null)
    {
        var cutoff = DateTime.UtcNow - (within ?? TimeSpan.FromMinutes(2));
        return KnownClients().Where(c => c.LastSeenUtc >= cutoff).ToList();
    }

    public static void ClearForTests()
    {
        lock (Sync) { Recent.Clear(); Clients.Clear(); }
    }
}
