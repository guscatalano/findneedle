using FindNeedlePluginLib;

namespace FindPluginCore.PluginSubsystem;

public class PluginSubsystemAccessor : IPluginSubsystemAccessor
{
    private readonly findneedle.PluginSubsystem.PluginManager _pluginManager;
    public PluginSubsystemAccessor(findneedle.PluginSubsystem.PluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }
    public string? PlantUMLPath => _pluginManager.config?.PlantUMLPath;
    // Add more properties/methods as needed
}
