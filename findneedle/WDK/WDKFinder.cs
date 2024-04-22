using Microsoft.Win32;

namespace findneedle.WDK;
public class WDKFinder
{

    public static string NOT_FOUND_STRING = "NOTFOUND";
    public static string GetPathOfWDK()
    {
        //HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots
        //WdkBinRootVersioned C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\
        try
        {
            var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots");
            if (key == null)
            {
                return NOT_FOUND_STRING;
            }
            var ret = key.GetValue("WdkBinRootVersioned");
            if (ret == null)
            {
                return NOT_FOUND_STRING;
            }
            return ((string)ret).ToString();
        }
        catch
        {
            return NOT_FOUND_STRING;
        }
        //return "C:\\Program Files (x86)\\Windows Kits\\10\\bin\\10.0.22621.0\\";
    }

    public static string GetTraceFmtPath()
    {
        var wdk = GetPathOfWDK();
        var potentialPath = Path.Combine(wdk, "x64", "tracefmt.exe");
        if (File.Exists(potentialPath))
        {
            return potentialPath;
        }
        return NOT_FOUND_STRING;
    }
}
