using mml2vgmIDEx64.Properties;
using Serilog;
using Serilog.Events;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace mml2vgmIDEx64
{
    public static class Log
    {
#if DEBUG
        public static bool debug = true;
#else
        public static bool debug = false;
#endif
        public static string path = "";
        public static bool consoleEchoBack = false;
        public static Action<string> dispMsg;
        private static readonly object syncObj = new object();
        private static ILogger logger = null;
        private static string loggerDirectory = "";
        private static readonly Encoding fileEncoding = new UTF8Encoding(false);
        private const int retentionDays = 30;


        public static void ForcedWrite(string msg, params object[] prm)
        {
            try
            {
                WriteCore(LogEventLevel.Information, FormatMessage(msg, prm), true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        public static void ForcedWrite(Exception e)
        {
            try
            {
                WriteCore(LogEventLevel.Error, FormatExceptionMessage(e), true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        public static void Write(string msg)
        {
            if (!debug) return;

            try
            {
                WriteCore(LogEventLevel.Debug, msg, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        public static void Shutdown()
        {
            lock (syncObj)
            {
                Serilog.Log.CloseAndFlush();
                logger = null;
                loggerDirectory = "";
            }
        }

        private static void WriteCore(LogEventLevel level, string msg, bool notifyUi)
        {
            EnsureLogger();
            logger?.Write(level, "{Message}", msg);

            if (notifyUi)
            {
                string timefmt = DateTime.Now.ToString(Resources.cntTimeFormat);
                dispMsg?.Invoke(timefmt + msg);
            }

            if (consoleEchoBack)
            {
                string timefmt = DateTime.Now.ToString(Resources.cntTimeFormat);
                Console.WriteLine(timefmt + msg);
            }
        }

        private static void EnsureLogger()
        {
            string fullPath = ResolveLogDirectory();

            lock (syncObj)
            {
                if (logger != null && string.Equals(loggerDirectory, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Directory.CreateDirectory(fullPath);
                CleanupOldLogs(fullPath);
                string rollingPath = Path.Combine(fullPath, "log-.txt");
                logger = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .WriteTo.File(
                        rollingPath,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: null,
                        outputTemplate: "{Timestamp:yyyy/MM/dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}",
                        encoding: fileEncoding,
                        shared: true)
                    .CreateLogger();

                loggerDirectory = fullPath;
                path = Path.Combine(fullPath, $"log-{DateTime.Now:yyyyMMdd}.txt");
            }
        }

        private static string ResolveLogDirectory()
        {
            if (!string.IsNullOrEmpty(Common.settingFilePath))
            {
                return Common.settingFilePath;
            }

            return Common.GetApplicationDataFolder(true);
        }

        private static void CleanupOldLogs(string fullPath)
        {
            DateTime threshold = DateTime.Now.AddDays(-retentionDays);
            string[] files = Directory.GetFiles(fullPath, "log-*.txt", SearchOption.TopDirectoryOnly);
            foreach (string file in files)
            {
                try
                {
                    if (File.GetLastWriteTime(file) < threshold)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        private static string FormatMessage(string msg, params object[] prm)
        {
            if (prm == null || prm.Length == 0) return msg;

            try
            {
                return string.Format(msg, prm);
            }
            catch (FormatException)
            {
                return $"{msg} [format error args: {string.Join(", ", prm)}]";
            }
        }

        private static string FormatExceptionMessage(Exception e)
        {
            string msg = string.Format(Resources.cntExceptionFormat, e.GetType().Name, e.Message, e.Source, e.StackTrace);
            Exception ie = e;
            while (ie.InnerException != null)
            {
                ie = ie.InnerException;
                msg += string.Format(Resources.cntInnerExceptionFormat, ie.GetType().Name, ie.Message, ie.Source, ie.StackTrace);
            }

            return msg;
        }

    }
}
