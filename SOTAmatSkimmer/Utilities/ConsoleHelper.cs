namespace SOTAmatSkimmer.Utilities
{
    internal static class ConsoleHelper
    {
        private static readonly object ConsoleLock = new object();

        public static void SafeWrite(string message, bool dateStamp = true, ConsoleColor? color = null, bool carriageReturnNoLineFeed = false)
        {
            lock (ConsoleLock)
            {
                if (color.HasValue)
                {
                    Console.ForegroundColor = color.Value;
                }

                Console.Write($"{(dateStamp ? (DateTime.Now.ToString("MM-dd HH:mm:ss") + ": ") : "")}{message}");

                if (carriageReturnNoLineFeed)
                {
                    Console.SetCursorPosition(0, Console.CursorTop);
                }

                if (color.HasValue)
                {
                    Console.ResetColor();
                }
            }
        }

        public static void SafeWriteLine(string message = "", bool dateStamp = true, ConsoleColor? color = null)
        {
            lock (ConsoleLock)
            {
                if (color.HasValue)
                {
                    Console.ForegroundColor = color.Value;
                }

                Console.WriteLine($"{(dateStamp ? (DateTime.Now.ToString("MM-dd HH:mm:ss") + ": ") : "")}{message}");

                if (color.HasValue)
                {
                    Console.ResetColor();
                }
            }
        }
    }
}