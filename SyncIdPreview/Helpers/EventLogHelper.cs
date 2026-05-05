using Microsoft.Extensions.Logging;
using System;

namespace SyncIdPreview.Helpers
{
    /// <summary>
    /// Wrapper for console logging using Microsoft.Extensions.Logging.
    /// Replaces the old EventLog-based implementation with platform-independent ILogger.
    /// </summary>
    internal class EventLogHelper
    {
        private readonly ILogger<EventLogHelper> _logger;

        public EventLogHelper(ILogger<EventLogHelper> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Write a log message with color to console and structured logging.
        /// </summary>
        public void WriteLog(string message, LogLevel logLevel = LogLevel.Information, int eventId = 0)
        {
            try
            {
                WriteLogUnsafe(message, logLevel, eventId);
            }
            catch
            {
                // Silently fail - logging should never crash the app
            }
        }

        private void WriteLogUnsafe(string message, LogLevel logLevel = LogLevel.Information, int eventId = 0)
        {
            // Console coloring based on log level
            Console.ResetColor();
            switch (logLevel)
            {
                case LogLevel.Error:
                case LogLevel.Critical:
                    Console.BackgroundColor = ConsoleColor.Red;
                    Console.ForegroundColor = ConsoleColor.White;
                    break;
                case LogLevel.Warning:
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case LogLevel.Information:
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.ForegroundColor = ConsoleColor.White;
                    break;
                case LogLevel.Debug:
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    break;
                default:
                    break;
            }

            Console.WriteLine(message);
            Console.ResetColor();

            // Log to ILogger with structured logging
            if (eventId != 0)
            {
                _logger.Log(logLevel, eventId, message);
            }
            else
            {
                _logger.Log(logLevel, message);
            }
        }

        /// <summary>
        /// Legacy method for backward compatibility.
        /// Maps old integer event type codes to LogLevel (without referencing EventLogEntryType directly to avoid CA1416).
        /// </summary>
        public void WriteLog(string message, int eventType, int eventId = 0)
        {
            // eventType values from old System.Diagnostics.EventLogEntryType:
            // 1 = Error
            // 2 = Warning
            // 4 = Information
            // 8 = SuccessAudit
            // 16 = FailureAudit
            var logLevel = ConvertEventTypeToLogLevel(eventType);
            WriteLog(message, logLevel, eventId);
        }

        /// <summary>
        /// Convert old EventLogEntryType integer values to modern LogLevel.
        /// This avoids direct reference to EventLogEntryType which triggers CA1416 on non-Windows platforms.
        /// </summary>
        private LogLevel ConvertEventTypeToLogLevel(int eventType)
        {
            return eventType switch
            {
                1 => LogLevel.Error,           // EventLogEntryType.Error
                2 => LogLevel.Warning,         // EventLogEntryType.Warning
                4 => LogLevel.Information,     // EventLogEntryType.Information
                8 => LogLevel.Information,     // EventLogEntryType.SuccessAudit
                16 => LogLevel.Error,          // EventLogEntryType.FailureAudit
                _ => LogLevel.Information
            };
        }

        /// <summary>
        /// No-op methods for backward compatibility (Windows EventLog creation/deletion not supported).
        /// </summary>
        internal void CheckEventLog() { /* No longer needed - logging is handled by ILogger */ }
        internal void CreateLog() { /* No longer needed - logging is handled by ILogger */ }
        internal void DeleteLog() { /* No longer needed - logging is handled by ILogger */ }
        internal bool LogExists() => true; // Assume logging always works with ILogger
    }
}
