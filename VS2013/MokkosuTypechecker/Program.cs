﻿using System;
using System.IO;
using System.Linq;

namespace MokkosuTypechecker
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                var mokkosu = new Mokkosu.Main.Mokkosu();
                mokkosu.OutputVersion();
                Console.WriteLine();

                foreach (var fname in args)
                {
                    mokkosu.AddSourceFile(fname);
                }

                if (args.Length != 0)
                {
                    var name = Path.GetFileNameWithoutExtension(args.Last());
                    mokkosu.Compile(name, false);
                    Console.WriteLine(mokkosu.GetOutput());
                }
                return 0;
            }
            catch (Mokkosu.Utils.MError e)
            {
                Console.WriteLine(e.Message);
                return 1;
            }
            catch (Exception e)
            {
                Console.WriteLine("致命的なエラー:");
                Console.WriteLine(e.ToString());
                return 2;
            }
        }
    }
}
