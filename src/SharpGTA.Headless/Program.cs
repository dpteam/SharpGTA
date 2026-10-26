using System;

namespace SharpGTA.Headless
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            return HeadlessApplication.Run(args, Console.Out, Console.Error);
        }
    }
}
