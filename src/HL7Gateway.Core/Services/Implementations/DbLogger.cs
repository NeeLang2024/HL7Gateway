using System.Threading.Channels;
using HL7Gateway.Core;
using HL7Gateway.Core.Entities;
using Microsoft.Extensions.Logging;

namespace HL7Gateway.Core.Services.Implementations;

public class DbLogger : ILogger
{
    private readonly string _categoryName;
    private readonly Channel<SystemLogEntry> _channel;

    public DbLogger(string categoryName, Channel<SystemLogEntry> channel)
    {
        _categoryName = categoryName;
        _channel = channel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel)
    {
        if (logLevel >= LogLevel.Warning) return true;
        if (logLevel == LogLevel.Information &&
            (_categoryName.StartsWith("HL7Gateway.") || _categoryName.StartsWith("CoreWCF."))) return true;
        return false;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (logLevel == LogLevel.Information && !IsKeyInformation(_categoryName, message)) return;

        _channel.Writer.TryWrite(new SystemLogEntry
        {
            Level = (byte)logLevel,
            Category = _categoryName,
            Message = message,
            StackTrace = exception?.ToString(),
            CreatedAt = ChinaTime.Now,
        });
    }

    private static bool IsKeyInformation(string category, string message)
    {
        if (category.Contains("MessageProcessorService", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (category.Contains("Worker", StringComparison.OrdinalIgnoreCase) ||
            category.Contains("AdtSenderService", StringComparison.OrdinalIgnoreCase) ||
            category.Contains("MllpSenderService", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (category.Contains("MllpListenerService", StringComparison.OrdinalIgnoreCase) ||
            category.Contains("PicixConnectionListener", StringComparison.OrdinalIgnoreCase) ||
            category.Contains("Connection", StringComparison.OrdinalIgnoreCase))
        {
            if (IsRoutineReliableMessagingNoise(message))
                return false;

            return ContainsAny(message,
                "start", "stop", "listen", "connect", "disconnect", "subscribe",
                "unsubscribe", "PIC iX", "WCF", "MLLP", "ADT", "ack", "fail",
                "SOAP action", "CreateSequence", "TerminateSequence", "response",
                "session complete", "End record", "SOAP Fault");
        }

        if (category.Contains("MessageCleanupService", StringComparison.OrdinalIgnoreCase))
        {
            return ContainsAny(message, "start", "stop", "disabled", "cleanup");
        }

        return ContainsAny(message, "started", "stopped", "disabled", "failed", "error", "PIC iX", "WCF", "MLLP", "ADT");
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        return values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRoutineReliableMessagingNoise(string message)
    {
        if (message.Contains("SOAP Fault", StringComparison.OrdinalIgnoreCase))
            return false;
        if (message.Contains("Subscribe", StringComparison.OrdinalIgnoreCase))
            return false;
        if (message.Contains("CreateSequence", StringComparison.OrdinalIgnoreCase))
            return false;

        return message.Contains("AckRequested", StringComparison.OrdinalIgnoreCase)
            || message.Contains("SequenceAcknowledgement", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Received 60 bytes from PIC iX", StringComparison.OrdinalIgnoreCase)
            || message.Contains("SOAP action: http://schemas.xmlsoap.org/ws/2005/02/rm/AckRequested", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Sent WCF response", StringComparison.OrdinalIgnoreCase);
    }
}
