using System;

namespace XLogger
{
    public enum LogTypes
    {
        Normal,
        Warning,
        Error,
        Fatal,
    }

    public enum LogVerbosity
    {
        Low,
        Common,
        Verbose,
        VeryVerbose,
    }

    public struct LogParam
    {
        public LogTypes LogType;
        public LogVerbosity LogVerbosity;
        public object Catagory;
        public object Content;
        public object[] Contents;
        public string BuiltContent;
    }

    public class Logger
    {
        public void Log(LogVerbosity logVerbosity, object catagory = null, object content = null, string builtContent = null)
        {
            LogParam param = default;
            param.LogType = LogTypes.Normal;
            param.LogVerbosity = logVerbosity;
            param.Catagory = catagory;
            param.Content = content;
            param.BuiltContent = builtContent;
            LogAll(param);
        }

        public void Logs(LogVerbosity logVerbosity, object catagory = null, params object[] content)
        {
            LogParam param;
            param.LogType = LogTypes.Normal;
            param.LogVerbosity = logVerbosity;
            param.Catagory = null;
            param.Content = null;
            param.Contents = content;
            param.BuiltContent = null;
            LogAll(param);
        }

        public void LogWarning(LogVerbosity logVerbosity, object catagory = null, object content = null, string builtContent = null)
        {
            LogParam param = default;
            param.LogType = LogTypes.Warning;
            param.LogVerbosity = logVerbosity;
            param.Catagory = catagory;
            param.Content = content;
            param.BuiltContent = builtContent;
            LogAll(param);
        }

        public void LogWarnings(LogVerbosity logVerbosity, object catagory = null, params object[] content)
        {
            LogParam param;
            param.LogType = LogTypes.Warning;
            param.LogVerbosity = logVerbosity;
            param.Catagory = null;
            param.Content = null;
            param.Contents = content;
            param.BuiltContent = null;
            LogAll(param);
        }

        public void LogError(LogVerbosity logVerbosity, object catagory = null, object content = null, string builtContent = null)
        {
            LogParam param = default;
            param.LogType = LogTypes.Error;
            param.LogVerbosity = logVerbosity;
            param.Catagory = catagory;
            param.Content = content;
            param.BuiltContent = builtContent;
            LogAll(param);
        }

        public void LogErrors(LogVerbosity logVerbosity, object catagory = null, params object[] content)
        {
            LogParam param;
            param.LogType = LogTypes.Error;
            param.LogVerbosity = logVerbosity;
            param.Catagory = null;
            param.Content = null;
            param.Contents = content;
            param.BuiltContent = null;
            LogAll(param);
        }

        public void LogFatal(LogVerbosity logVerbosity, object catagory = null, object content = null, string builtContent = null)
        {
            LogParam param = default;
            param.LogType = LogTypes.Fatal;
            param.LogVerbosity = logVerbosity;
            param.Catagory = catagory;
            param.Content = content;
            param.BuiltContent = builtContent;
            LogAll(param);
        }

        public void LogFatals(LogVerbosity logVerbosity, object catagory = null, params object[] content)
        {
            LogParam param;
            param.LogType = LogTypes.Fatal;
            param.LogVerbosity = logVerbosity;
            param.Catagory = null;
            param.Content = null;
            param.Contents = content;
            param.BuiltContent = null;
            LogAll(param);
        }

        public void LogAll(LogParam logParam)
        {
            if (logParam.Content == null)
            {
                logParam.Content = OnBuildContents(logParam);
            }

            if (XHelper.StringUtils.IsEmpty(logParam.BuiltContent))
            {
                logParam.BuiltContent = BuildContent(logParam);
            }

            OnLog(logParam.BuiltContent);
            if (logParam.LogType == LogTypes.Fatal)
            {
                throw new Exception(logParam.BuiltContent);
            }
        }

        private string BuildContent(LogParam param)
        {
            param.BuiltContent = OnBuildContent(param);
            param.BuiltContent = OnAfterBuildContent(param);
            return param.BuiltContent;
        }

        protected virtual string OnBuildContents(LogParam param)
        {
            return string.Join("  ", param.Contents);
        }

        protected virtual string OnBuildContent(LogParam param)
        {
            string cat = "";
            if (param.Catagory != null)
            {
                cat = $"[{param.Catagory.ToString()}]";
            }

            string time = System.DateTime.Now.ToString("HH:mm:ss:fff");

            return $"{cat}[{time}] {param.Content.ToString()}";
        }

        protected virtual string OnAfterBuildContent(LogParam param)
        {
            return param.BuiltContent;
        }

        protected virtual void OnLog(string builtContent)
        {
            Console.WriteLine(builtContent);
        }
    }
}

