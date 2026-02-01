namespace FindNeedleToolInstallers;

public interface IMermaidInstaller
{
    string? GetMmdcPath();
    string? GetNodePath();
    bool IsInstalled();
}
