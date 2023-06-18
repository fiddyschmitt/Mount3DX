﻿using NWebDav.Server.Logging;
using System.Diagnostics;

namespace NWebDav.Server
{
    public class DebugOutputAdapter : ILoggerFactory
    {
        public ICollection<LogLevel> LogLevels { get; } = new HashSet<LogLevel>() { LogLevel.Fatal, LogLevel.Error, LogLevel.Warning, LogLevel.Info };

        private class DebugOutputLoggerAdapter : ILogger
        {
            private DebugOutputAdapter Parent { get; }
            private Type Type { get; }

            public DebugOutputLoggerAdapter(DebugOutputAdapter parent, Type type)
            {
                Parent = parent;
                Type = type;
            }

            public bool IsLogEnabled(LogLevel logLevel)
            {
                return Parent.LogLevels.Contains(logLevel);
            }

            public void Log(LogLevel logLevel, Func<string> messageFunc, Exception exception)
            {
                if (IsLogEnabled(logLevel))
                {
                    Debug.WriteLine($"{logLevel.ToString().ToUpper()}:{Type.FullName} - {messageFunc()}");
                    if (exception != null)
                    {
                        Debug.WriteLine($"- Exception: {exception.Message}");
                        Debug.WriteLine($"- StackTrace: {exception.StackTrace}");
                    }
                }
            }
        }

        public ILogger CreateLogger(Type type)
        {
            return new DebugOutputLoggerAdapter(this, type);
        }
    }
}