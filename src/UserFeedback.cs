using System;

namespace AoE2Wide
{
    internal class UserFeedback
    {
        public static void Error(Exception e)
        {
            var normalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("An error occurred:");
            Console.WriteLine(e.Message);
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine("Exception Details:");
            Console.WriteLine(e.ToString());
            Console.ForegroundColor = normalColor;
        }

        public static void Info(string msg, string param1)
        {
            Info(string.Format(msg, param1));
        }

        public static void Info(string msg)
        {
            Console.WriteLine(msg);
        }

        public static void Trace(string msg, object param1)
        {
            Trace(string.Format(msg, param1));
        }

        public static void Trace(string msg)
        {
            var normalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(msg);
            Console.ForegroundColor = normalColor;
        }

        public static void Warning(string msg, object param1)
        {
            Warning(string.Format(msg, param1));
        }

        public static void Warning(string msg)
        {
            var normalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("Warning: ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(msg);
            Console.ForegroundColor = normalColor;
        }

        internal static void Close()
        {
            // Assumption:
            // If the console window title is equal to the executable path, exe was run by double
            // If it doesn't (eg. prefixed by c:\windows\system32\cmd.exe - c:\Progra...... )
            //  it was run from a console window (or batch file!) and shouldn't wait for a key.
            var runningFromCommandPrompt =
                !System.Windows.Forms.Application.ExecutablePath.Equals(Console.Title,
                                                                        StringComparison.InvariantCultureIgnoreCase);
            if (runningFromCommandPrompt)
                return;

            Info(@"Press any key to quit");
            while (!Console.KeyAvailable)
            {
                System.Threading.Thread.Sleep(50);
            }
        }
    }
}
