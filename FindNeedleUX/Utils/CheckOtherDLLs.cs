using System;
using System.Diagnostics;
using System.IO;

namespace FindNeedleUX.Utils;
public class CheckOtherDLLs
{
    public static void AreWeInstalledOk()
    {
        //var findNeedleHere = false;
        Directory.GetFiles(Path.GetDirectoryName(Environment.ProcessPath));

        foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
        {
            Console.WriteLine(string.Format("Module: {0}", module.FileName));
        }
    }
}
