using System;
using System.IO;

namespace UES
{
    /// <summary>
    /// Logging levels for categorizing log messages
    /// </summary>
    public enum LogLevel
    {
        /// <summary>Verbose debugging information</summary>
        Verbose,
        /// <summary>General information</summary>
        Info,
        /// <summary>Warning messages</summary>
        Warning,
        /// <summary>Error messages</summary>
        Error
    }

    /// <summary>
    /// Centralized logging system for UES
    /// Supports both console and file output with configurable verbosity
    /// AOT-compatible implementation
    /// </summary>
    public static class Logger
    {
        private static readonly object _lockObject = new object();

        /// <summary>
        /// Logs a general message
        /// </summary>
        /// <param name="message">Message to log</param>
        public static void Log(string message)
        {
            LogInfo(message);
        }

        /// <summary>
        /// Logs an error message in red
        /// </summary>
        /// <param name="message">Error message to log</param>
        public static void LogError(string message)
        {
            LogMessage(LogLevel.Error, message);
        }

        /// <summary>
        /// Logs a warning message in yellow
        /// </summary>
        /// <param name="message">Warning message to log</param>
        public static void LogWarning(string message)
        {
            LogMessage(LogLevel.Warning, message);
        }

        /// <summary>
        /// Logs an info message in green
        /// </summary>
        /// <param name="message">Info message to log</param>
        public static void LogInfo(string message)
        {
            LogMessage(LogLevel.Info, message);
        }

        /// <summary>
        /// Logs a verbose debug message (only shown when verbose logging is enabled)
        /// </summary>
        /// <param name="message">Verbose message to log</param>
        public static void LogVerbose(string message)
        {
            if (UESConfig.EnableVerboseLogging)
            {
                LogMessage(LogLevel.Verbose, message);
            }
        }

        /// <summary>
        /// Logs memory read failure messages (only shown when memory read failure logging is enabled)
        /// </summary>
        /// <param name="message">Memory read failure message to log</param>
        public static void LogMemoryReadFailure(string message)
        {
            if (!UESConfig.DisableMemoryReadFailureLogging)
            {
                LogVerbose($"Memory Read Failure: {message}");
            }
        }

        /// <summary>
        /// Logs a message with dual colors for key-value pairs
        /// </summary>
        /// <param name="key">The key part of the message</param>
        /// <param name="keyColor">Color for the key</param>
        /// <param name="value">The value part of the message</param>
        /// <param name="valueColor">Color for the value</param>
        public static void LogDualColor(string key, ConsoleColor keyColor, string value, ConsoleColor valueColor)
        {
            lock (_lockObject)
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var fullMessage = $"[UES] {key}: {value}";

                // Console output with colors
                if (UESConfig.EnableConsoleLogging)
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write($"[{timestamp}] ");
                    Console.ForegroundColor = keyColor;
                    Console.Write($"[UES] {key}: ");
                    Console.ForegroundColor = valueColor;
                    Console.WriteLine(value);
                    Console.ResetColor();
                }

                // File output (plain text)
                if (UESConfig.EnableFileLogging)
                {
                    LogToFile($"[{timestamp}] {fullMessage}");
                }
            }
        }

        /// <summary>
        /// Logs an exception with full details
        /// </summary>
        /// <param name="exception">Exception to log</param>
        /// <param name="context">Optional context message</param>
        public static void LogException(Exception exception, string? context = null)
        {
            var message = context != null 
                ? $"{context}: {exception.Message}\n{exception.StackTrace}"
                : $"{exception.Message}\n{exception.StackTrace}";
            
            LogError(message);
        }

        /// <summary>
        /// Core logging method that handles message formatting and output
        /// </summary>
        /// <param name="level">Log level</param>
        /// <param name="message">Message to log</param>
        private static void LogMessage(LogLevel level, string message)
        {
            lock (_lockObject)
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var levelStr = level.ToString().ToUpper();
                var fullMessage = $"[UES] [{levelStr}] {message}";

                // Console output with colors
                if (UESConfig.EnableConsoleLogging)
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write($"[{timestamp}] ");
                    
                    var color = level switch
                    {
                        LogLevel.Error => ConsoleColor.Red,
                        LogLevel.Warning => ConsoleColor.Yellow,
                        LogLevel.Info => ConsoleColor.Green,
                        LogLevel.Verbose => ConsoleColor.Cyan,
                        _ => ConsoleColor.White
                    };
                    
                    Console.ForegroundColor = color;
                    Console.WriteLine(fullMessage);
                    Console.ResetColor();
                }

                // File output (plain text)
                if (UESConfig.EnableFileLogging)
                {
                    LogToFile($"[{timestamp}] {fullMessage}");
                }
            }
        }

        /// <summary>
        /// Writes a message to the log file
        /// </summary>
        /// <param name="message">Message to write</param>
        private static void LogToFile(string message)
        {
            try
            {
                if (!string.IsNullOrEmpty(UESConfig.LogFilePath))
                {
                    File.AppendAllText(UESConfig.LogFilePath, message + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                // Fallback to console if file logging fails
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[UES] Failed to write to log file: {ex.Message}");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Clears the log file
        /// </summary>
        public static void ClearLogFile()
        {
            try
            {
                if (!string.IsNullOrEmpty(UESConfig.LogFilePath) && File.Exists(UESConfig.LogFilePath))
                {
                    File.WriteAllText(UESConfig.LogFilePath, string.Empty);
                    LogInfo("Log file cleared");
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to clear log file: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs the current UES configuration
        /// </summary>
        public static void LogConfiguration()
        {
            LogInfo("=== UES Configuration ===");
            LogInfo(UESConfig.GetConfigurationSummary());
            LogInfo("========================");
        }
    }
}
