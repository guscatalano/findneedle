namespace FindNeedlePluginLib;

public interface IPluginSubsystemAccessor
{
    string? PlantUMLPath { get; }
    // Add more properties/methods as needed for plugin subsystem access
}

public static class PluginSubsystemAccessorProvider
{
    public static IPluginSubsystemAccessor? Accessor { get; private set; }
    public static void Register(IPluginSubsystemAccessor accessor)
    {
        Accessor = accessor;
    }
}
