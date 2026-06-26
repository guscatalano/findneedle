using LogETWApp;

// Fixture-generation modes (used by tools/Fixture):
//   LogETWApp guid          → print the TraceLogging provider GUID (to enable it in a capture session)
//   LogETWApp many <count>  → emit <count> TraceLogging events as fast as possible
//   LogETWApp               → the original 10-second demo
if (args.Length > 0 && string.Equals(args[0], "guid", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine(LogSomeStuff.ProviderGuid.ToString());
    return;
}
if (args.Length > 1 && string.Equals(args[0], "many", StringComparison.OrdinalIgnoreCase)
    && int.TryParse(args[1], out var count))
{
    LogSomeStuff.LogManyFast(count);
    return;
}

LogSomeStuff.LogFor10Seconds();
