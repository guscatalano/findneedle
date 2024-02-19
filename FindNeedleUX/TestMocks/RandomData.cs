using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedleUX.TestMocks;
public class RandomData
{

    public static string GetName()
    {
        Random rnd = new Random();
        //Dictionary of strings
        string[] words2 = { "Bold", "Think", "Friend", "Pony", "Fall", "Easy" };
        //Random number from - to
        int randomNumber = rnd.Next(2000, 3000);
        //Create combination of word + number
        string randomString = $"{words2[rnd.Next(0, words2.Length)]}{randomNumber}";

        return randomString;
    }

    public static string GetRandomFilterName()
    {
        Random rnd = new Random();
        //Dictionary of strings
        string[] words1 = { "TimeRange", "Keyword", "Level", "Username", "TimeAgo", "UniqueEntries" };
        string randomString = $"{words1[rnd.Next(0, words1.Length)]}";

        return randomString;
    }
}
