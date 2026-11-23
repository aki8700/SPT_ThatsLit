using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using System.Reflection;

namespace ThatsLit
{
    internal static class Logger
    {
        public static void LogInfo(object data) => Log(LogLevel.Info, data);
        public static void LogDebug(object data) => Log(LogLevel.Debug, data);
        public static void LogWarning(object data) => Log(LogLevel.Warning, data);
        public static void LogError(object data) => Log(LogLevel.Error, data);

        private static void Log(LogLevel level, object data)
        {
            string methods = string.Empty;
            Type declaringType = null;

            if (level != LogLevel.Debug)
            {
                int max = GetMaxFrames(level);
                if (max > 0)
                {
                    int count = 0;
                    StackTrace stackTrace = new StackTrace();
                    for (int i = 0; i < stackTrace.FrameCount; i++)
                    {
                        var method = stackTrace.GetFrame(i)?.GetMethod();
                        if (method == null) continue;
                        Type methodType = method.DeclaringType;

                        if (methodType == typeof(Logger)) continue;

                        declaringType = declaringType ?? methodType;
                        methods += "." + method.Name;

                        count++;
                        if (count >= max) break;
                    }
                    methods = $"[{methods}]:";
                }
            }

            LastLogData = data;
            string result = $"{methods} [{data}]";
            SelectLogSource(declaringType).Log(level, result);
        }

        private static int GetMaxFrames(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug: return 0;
                case LogLevel.Info: return 1;
                case LogLevel.Warning: return 2;
                case LogLevel.Error: return 3;
                case LogLevel.Fatal: return 4;
                default: return 0;
            }
        }

        private static object _lastLogData;
        public static object LastLogData
        {
            get => Volatile.Read(ref _lastLogData);
            private set => Volatile.Write(ref _lastLogData, value);
        }

        private static readonly object _logSourcesLock = new object();

        private static ManualLogSource SelectLogSource(Type type = null)
        {
            string name = type?.Name ?? "ThatsLit";
            lock (_logSourcesLock)
            {
                if (!LogSourcesDictionary.TryGetValue(name, out var source))
                {
                    source = BepInEx.Logging.Logger.CreateLogSource(name);
                    LogSourcesDictionary.Add(name, source);
                }
                return source;
            }
        }

        private static readonly Dictionary<string, ManualLogSource> LogSourcesDictionary = new Dictionary<string, ManualLogSource>();

    }
}
