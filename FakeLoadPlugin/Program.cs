using System;
using System.Reflection;

namespace FakeLoadPlugin
{
    internal class Program
    {
        static void Main(string[] args)
        {
            LoadPlugin(args[0]);
        }

        static void LoadPlugin(string file)
        {
            Console.WriteLine("Attempting to load: " + file);
            var dll = Assembly.LoadFile(file);
            var types = dll.GetTypes();
            foreach (var type in types)
            {
                foreach (var possibleInterface in type.GetInterfaces())
                {
                    //   if(possibleInterface.FullName)
                    Console.WriteLine(possibleInterface.FullName);
                }
            }
        }
    }
}