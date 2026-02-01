using FindNeedleToolInstallers;

namespace FindNeedleUmlDsl;

/// <summary>
/// Aliases for generator classes stored in subdirectories
/// Makes them directly accessible from FindNeedleUmlDsl namespace
/// </summary>
#pragma warning disable CS0436 // Type conflicts with imported type

public class PlantUMLGenerator : PlantUML.PlantUMLGenerator
{
    public PlantUMLGenerator(IPlantUmlInstaller? installer = null) : base(installer) { }
}

public class MermaidUMLGenerator : MermaidUML.MermaidUMLGenerator
{
    public MermaidUMLGenerator(IMermaidInstaller? installer = null) : base(installer) { }
}

#pragma warning restore CS0436
