using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace findneedle.WDK;
public class WDKFinder
{
    public static string GetPathOfWDK()
    {
        //HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots
        //WdkBinRootVersioned C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\
        try
        {
            var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots");
            if (key == null)
            {
                throw new Exception("oh no!");
            }
            var ret = key.GetValue("WdkBinRootVersioned");
            if(ret == null)
            {
                throw new Exception("oh no!!");
            }
            return ((string)ret).ToString();
        } catch
        {
            throw new Exception("Failed to find WDK");
        }
        //return "C:\\Program Files (x86)\\Windows Kits\\10\\bin\\10.0.22621.0\\";
    }

    public static string GetTraceFmtPath()
    {
        return Path.Combine(GetPathOfWDK(), "tracefmt.exe");
    }
}
