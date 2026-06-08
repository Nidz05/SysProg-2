using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DrugiProjekat {
    public static class Logger {
        public enum Event {
            Time,
            Error,
            Notify,
            Request,
            Synchro,
            Network,
            Critical,
            Response,
        }

        public static void RawConsoleLine(string line) {
            lock (_lock) {
                RawConsoleLine(line, ConsoleColor.White);
            }
        }
        public static void EchoLog(Event logT, string message) {
            string formatted = Format(logT, message);
            lock (_lock) {
                if (LogPreformatted(formatted))
                    RawConsoleLine(formatted, colorCoding[logT]);
            }
        }
        public static void Log(Event logT, string message)
            => LogPreformatted(Format(logT, message));

        public static void EchoLog(HttpListenerRequest request, string additionalInformation = "")
            => EchoLog(Event.Request, FormatRequest(request, additionalInformation));

        public static void Log(HttpListenerRequest request, string additionalInformation = "")
            => Log(Event.Request, FormatRequest(request, additionalInformation));

        public static void Error(string message)
            => EchoLog(Event.Error, message);

        private static void RawConsoleLine(string line, ConsoleColor color) {
            ConsoleColor old = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(line);
            Console.ForegroundColor = old;
        }
        private static bool LogPreformatted(string formatted) {
            lock (_lock) {
                try {
                    using (StreamWriter sw = new StreamWriter(path, true)) {
                        sw.WriteLine(formatted);
                    }
                    return true;
                }
                catch (Exception e) {
                    RawConsoleLine(Format(Event.Error, "Caught exception while logging -> " + e.Message), colorCoding[Event.Error]);
                    return false;
                }
            }
        }
        private static string Format(Event logT, string message) {
            int? taskId = Task.CurrentId;
            string where = taskId.HasValue
                ? $"Task {taskId} (Thread {Thread.CurrentThread.ManagedThreadId})"
                : $"Thread {Thread.CurrentThread.ManagedThreadId}";
            return $"[{DateTime.Now}] " +
                   $"[{logT}] " +
                   $"{where}: " +
                   $"{message}";
        }

        private static string FormatRequest(HttpListenerRequest request, string additionalInformation = "")
            => request.HttpMethod + " " +
               request.Url + " from " +
               request.UserHostName + (additionalInformation == "" ? "" : " -> ") +
               additionalInformation;

        private static readonly object _lock = new();
        private const string path = "ServerLog.txt";
        private readonly static Dictionary<Event, ConsoleColor> colorCoding = new() {
            [Event.Time]     = ConsoleColor.Yellow,
            [Event.Error]    = ConsoleColor.Red,
            [Event.Notify]   = ConsoleColor.DarkGray,
            [Event.Request]  = ConsoleColor.Gray,
            [Event.Synchro]  = ConsoleColor.Cyan,
            [Event.Network]  = ConsoleColor.Blue,
            [Event.Critical] = ConsoleColor.DarkRed,
            [Event.Response] = ConsoleColor.Gray,
        };
    }
}
