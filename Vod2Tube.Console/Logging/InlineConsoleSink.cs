using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace Vod2Tube.Console.Logging
{
    /// <summary>
    /// A Serilog sink that writes Verbose/Trace-level events in-place on the console
    /// (using a carriage-return to overwrite the current line) so that frequent progress
    /// updates don't scroll the terminal.  All other log levels are written normally with
    /// a trailing newline.
    /// </summary>
    internal sealed class InlineConsoleSink : ILogEventSink
    {
        private readonly ITextFormatter _formatter;
        private readonly object _lock = new object();
        private bool _lastWasProgress;

        public InlineConsoleSink(ITextFormatter formatter)
        {
            _formatter = formatter;
        }

        public void Emit(LogEvent logEvent)
        {
            bool isProgress = logEvent.Level == LogEventLevel.Verbose;

            var writer = new StringWriter();
            _formatter.Format(logEvent, writer);
            string text = writer.ToString();

            lock (_lock)
            {
                if (isProgress)
                {
                    try
                    {
                        // Overwrite the current console line without scrolling.
                        // Trim trailing newline/CR from the formatter output, pad to clear
                        // any leftover characters from a longer previous line, then use \r
                        // to stay on the same line.
                        string line = text.TrimEnd('\r', '\n');
                        int padWidth = System.Console.IsOutputRedirected
                            ? 0
                            : Math.Max(0, System.Console.WindowWidth - 1);
                        string padded = line.Length < padWidth
                            ? line + new string(' ', padWidth - line.Length)
                            : line;
                        System.Console.Write("\r" + padded);
                        _lastWasProgress = true;
                    }
                    catch (System.IO.IOException)
                    {
                        // Fall back to a normal write if the console APIs are unavailable.
                        System.Console.Write(text);
                        _lastWasProgress = false;
                    }
                    catch (System.InvalidOperationException)
                    {
                        // Fall back to a normal write if the console APIs are unavailable.
                        System.Console.Write(text);
                        _lastWasProgress = false;
                    }
                }
                else
                {
                    if (_lastWasProgress)
                    {
                        // Move to a fresh line so the progress text doesn't get corrupted.
                        System.Console.WriteLine();
                        _lastWasProgress = false;
                    }
                    System.Console.Write(text);
                }
            }
        }
    }
}
