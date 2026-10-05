using System;
using System.IO;
using System.Text;
using System.Threading;
using XLogger;

namespace GMCore
{
    public enum GameplayLogImportance
    {
        Detail = 0,
        Important = 1,
        Warning = 2,
        Error = 3,
        Critical = 4,
    }

    public sealed class GameplayLoggerOptions
    {
        public string DirectoryPath = "logs";
        public string FilePrefix = "gameplaymachine";
        public bool WriteToConsole = true;
        public GameplayLogImportance ConsoleMinimumImportance = GameplayLogImportance.Important;
        public Action<GameplayLogImportance, string> ConsoleOutput;
    }

    /// <summary>
    /// Writes every XLogger entry to a per-process file while keeping console output focused on
    /// operationally important entries. The writer flushes each line so server crashes retain the
    /// latest diagnostics.
    /// </summary>
    public class GameplayLogger : Logger, IDisposable
    {
        private readonly object m_sync = new object();
        private readonly StreamWriter m_writer;
        private readonly GameplayLoggerOptions m_options;
        private readonly ThreadLocal<LogParam?> m_pending = new ThreadLocal<LogParam?>();
        private bool m_disposed;

        public string FilePath { get; }

        public GameplayLogger(GameplayLoggerOptions options = null)
        {
            m_options = options ?? new GameplayLoggerOptions();
            if (string.IsNullOrWhiteSpace(m_options.DirectoryPath))
                throw new ArgumentException("Log directory is required", nameof(options));
            string prefix = SanitizeFileName(string.IsNullOrWhiteSpace(m_options.FilePrefix)
                ? "gameplaymachine"
                : m_options.FilePrefix);
            string directory = Path.GetFullPath(m_options.DirectoryPath);
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory,
                $"{prefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{GetProcessId()}.log");
            m_writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew,
                FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true,
            };
            WriteDirect(GameplayLogImportance.Important, "Logging",
                $"Log started. Full log: {FilePath}");
        }

        public void Detail(object category, object content) =>
            Log(LogVerbosity.VeryVerbose, category, content);

        public void Important(object category, object content) =>
            Log(LogVerbosity.Common, category, content);

        public void Warning(object category, object content) =>
            LogWarning(LogVerbosity.Common, category, content);

        public void Error(object category, object content) =>
            LogError(LogVerbosity.Common, category, content);

        public void Critical(object category, object content) =>
            LogFatal(LogVerbosity.Common, category, content);

        protected override string OnAfterBuildContent(LogParam param)
        {
            m_pending.Value = param;
            return base.OnAfterBuildContent(param);
        }

        protected override void OnLog(string builtContent)
        {
            LogParam? pending = m_pending.Value;
            m_pending.Value = null;
            GameplayLogImportance importance = pending.HasValue
                ? ToImportance(pending.Value)
                : GameplayLogImportance.Important;
            string category = pending.HasValue
                ? pending.Value.Catagory?.ToString() ?? "GameplayMachine"
                : "GameplayMachine";
            string content = pending.HasValue
                ? BuildMessage(pending.Value, builtContent)
                : builtContent ?? string.Empty;
            WriteDirect(importance, category, content);
        }

        private static string BuildMessage(LogParam param, string fallback)
        {
            if (param.Content != null)
                return param.Content.ToString() ?? string.Empty;
            if (param.Contents == null || param.Contents.Length == 0)
                return fallback ?? string.Empty;

            var builder = new StringBuilder();
            foreach (object item in param.Contents)
            {
                if (builder.Length > 0)
                    builder.Append(' ');
                builder.Append(item);
            }
            return builder.ToString();
        }

        private void WriteDirect(GameplayLogImportance importance, string category, string content)
        {
            string line = $"{DateTimeOffset.Now:O} [{importance}] [{category}] {content}";
            lock (m_sync)
            {
                if (m_disposed)
                    return;
                m_writer.WriteLine(line);
                if (m_options.WriteToConsole && importance >= m_options.ConsoleMinimumImportance)
                {
                    if (m_options.ConsoleOutput != null)
                        m_options.ConsoleOutput(importance, line);
                    else
                        Console.WriteLine(line);
                }
            }
        }

        private static GameplayLogImportance ToImportance(LogParam param)
        {
            switch (param.LogType)
            {
                case LogTypes.Warning:
                    return GameplayLogImportance.Warning;
                case LogTypes.Error:
                    return GameplayLogImportance.Error;
                case LogTypes.Fatal:
                    return GameplayLogImportance.Critical;
                default:
                    return param.LogVerbosity <= LogVerbosity.Common
                        ? GameplayLogImportance.Important
                        : GameplayLogImportance.Detail;
            }
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value;
        }

        private static int GetProcessId()
        {
#if NETSTANDARD2_1
            return System.Diagnostics.Process.GetCurrentProcess().Id;
#else
            return Environment.ProcessId;
#endif
        }

        public void Dispose()
        {
            lock (m_sync)
            {
                if (m_disposed)
                    return;
                m_writer.WriteLine($"{DateTimeOffset.Now:O} [Important] [Logging] Log stopped.");
                m_disposed = true;
                m_writer.Dispose();
            }
            m_pending.Dispose();
        }
    }
}
