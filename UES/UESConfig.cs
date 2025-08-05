using System.Diagnostics;

namespace UES
{
    /// <summary>
    /// Defines the memory access mode for UES operations
    /// </summary>
    public enum UESMemoryMode
    {
        /// <summary>Internal memory access - code is injected into target process</summary>
        Internal,
        /// <summary>External memory access - reading from external process via WinAPI</summary>
        External,
        /// <summary>Memory mode not yet configured</summary>
        NotSet
    }

    /// <summary>
    /// Centralized configuration system for UES (Unreal Engine Sharp)
    /// Controls memory access mode, process selection, and engine behavior
    /// AOT-compatible implementation using static configuration
    /// </summary>
    public static class UESConfig
    {
        /// <summary>
        /// The current memory access mode
        /// </summary>
        public static UESMemoryMode MemoryMode { get; set; } = UESMemoryMode.NotSet;

        /// <summary>
        /// Target process name for external memory mode
        /// Only used when MemoryMode is External
        /// </summary>
        public static string ExternalProcessName { get; set; } = string.Empty;

        /// <summary>
        /// Target process object for external memory mode
        /// Only used when MemoryMode is External and ExternalProcessName is not set
        /// </summary>
        public static Process? ExternalProcess { get; set; } = null;

        /// <summary>
        /// Maximum size for a single memory read operation (bytes)
        /// Used to prevent excessive memory allocation
        /// </summary>
        public static int MaxReadSize { get; set; } = 1024 * 1024; // 1MB default

        /// <summary>
        /// Maximum length for string reads from memory
        /// Used to prevent runaway string reads
        /// </summary>
        public static int MaxStringLength { get; set; } = 1024;

        /// <summary>
        /// Whether to enable verbose logging for debugging
        /// </summary>
        public static bool EnableVerboseLogging { get; set; } = false;

        /// <summary>
        /// Whether to enable console output for logging
        /// </summary>
        public static bool EnableConsoleLogging { get; set; } = true;

        /// <summary>
        /// Whether to enable file output for logging
        /// </summary>
        public static bool EnableFileLogging { get; set; } = false;

        /// <summary>
        /// Log file path when file logging is enabled
        /// </summary>
        public static string LogFilePath { get; set; } = "UES.log";

        /// <summary>
        /// Gets whether external memory mode is configured
        /// </summary>
        public static bool UseExternalMemory => MemoryMode == UESMemoryMode.External;

        /// <summary>
        /// Gets whether internal memory mode is configured
        /// </summary>
        public static bool UseInternalMemory => MemoryMode == UESMemoryMode.Internal;

        /// <summary>
        /// Validates the current configuration
        /// </summary>
        public static bool IsValid => 
            UseInternalMemory || 
            (UseExternalMemory && (!string.IsNullOrEmpty(ExternalProcessName) || ExternalProcess != null));

        /// <summary>
        /// Resets configuration to default values
        /// </summary>
        public static void Reset()
        {
            MemoryMode = UESMemoryMode.NotSet;
            ExternalProcessName = string.Empty;
            ExternalProcess = null;
            MaxReadSize = 1024 * 1024;
            MaxStringLength = 1024;
            EnableVerboseLogging = false;
            EnableConsoleLogging = true;
            EnableFileLogging = false;
            LogFilePath = "UES.log";
        }

        /// <summary>
        /// Configures UES for internal memory access
        /// </summary>
        public static void ConfigureForInternal()
        {
            MemoryMode = UESMemoryMode.Internal;
            ExternalProcessName = string.Empty;
            ExternalProcess = null;
        }

        /// <summary>
        /// Configures UES for external memory access using process name
        /// </summary>
        /// <param name="processName">Name of the target process</param>
        public static void ConfigureForExternal(string processName)
        {
            if (string.IsNullOrEmpty(processName))
                throw new ArgumentException("Process name cannot be null or empty", nameof(processName));
            
            MemoryMode = UESMemoryMode.External;
            ExternalProcessName = processName;
            ExternalProcess = null;
        }

        /// <summary>
        /// Configures UES for external memory access using process object
        /// </summary>
        /// <param name="process">Target process object</param>
        public static void ConfigureForExternal(Process process)
        {
            ArgumentNullException.ThrowIfNull(process);
            
            MemoryMode = UESMemoryMode.External;
            ExternalProcessName = string.Empty;
            ExternalProcess = process;
        }

        /// <summary>
        /// Gets a summary of the current configuration
        /// </summary>
        /// <returns>Configuration summary string</returns>
        public static string GetConfigurationSummary()
        {
            var summary = $"UES Configuration:\n";
            summary += $"  Memory Mode: {MemoryMode}\n";
            summary += $"  Valid: {IsValid}\n";
            
            if (UseExternalMemory)
            {
                if (!string.IsNullOrEmpty(ExternalProcessName))
                    summary += $"  External Process Name: {ExternalProcessName}\n";
                else if (ExternalProcess != null)
                    summary += $"  External Process: {ExternalProcess.ProcessName} (PID: {ExternalProcess.Id})\n";
                else
                    summary += $"  External Process: Not configured\n";
            }
            
            summary += $"  Max Read Size: {MaxReadSize} bytes\n";
            summary += $"  Max String Length: {MaxStringLength}\n";
            summary += $"  Verbose Logging: {EnableVerboseLogging}\n";
            summary += $"  Console Logging: {EnableConsoleLogging}\n";
            summary += $"  File Logging: {EnableFileLogging}";
            
            if (EnableFileLogging)
                summary += $" ({LogFilePath})";
            
            return summary;
        }
    }
}
