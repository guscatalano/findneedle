using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedleCoreUtils;

public enum TimeAgoUnit
{
    Second = 0,
    Minute = 1,
    Hour = 2,
    Day = 3,
}

public class ByteUtils
{


    static readonly string[] ByteSizeSuffixes =
                  { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

    public static string BytesToFriendlyString(long value, int decimalPlaces = 1)
    {
        if (decimalPlaces < 0) { throw new ArgumentOutOfRangeException("decimalPlaces"); }
        if (value < 0) { return "-" + BytesToFriendlyString(-value, decimalPlaces); }
        if (value == 0) { return string.Format("{0:n" + decimalPlaces + "} bytes", 0); }

        // mag is 0 for bytes, 1 for KB, 2, for MB, etc.
        var mag = (int)Math.Log(value, 1024);

        // 1L << (mag * 10) == 2 ^ (10 * mag) 
        // [i.e. the number of bytes in the unit corresponding to mag]
        var adjustedSize = (decimal)value / (1L << (mag * 10));

        // make adjustment when the value is large enough that
        // it would round up to 1000 or more
        if (Math.Round(adjustedSize, decimalPlaces) >= 1000)
        {
            mag += 1;
            adjustedSize /= 1024;
        }

        return string.Format("{0:n" + decimalPlaces + "} {1}",
            adjustedSize,
            ByteSizeSuffixes[mag]);
    }
}

