using RGiesecke.DllExport;
using System;

namespace LibCoherenceCore
{
    public class Program
    {
        [DllExport]
        public static int DoWork(int v)
        {
            try
            {
                return v + 4;
            } catch (Exception e) {
                Console.WriteLine($"Exception: {e}");
            }
            return 0;
        }

        [DllExport]
        public static void Version()
        {
            Console.WriteLine("Hi");
        }
        
        [DllExport]
        public static int Version2()
        {
            return 14;
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
        }
    }
}
