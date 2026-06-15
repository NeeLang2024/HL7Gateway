using System.Threading.Channels;
using HL7Gateway.Core.Entities;
using Microsoft.Extensions.Logging;

namespace HL7Gateway.Core.Services.Implementations;

public class DbLoggerProvider : ILoggerProvider
{
    private readonly Channel<SystemLogEntry> _channel;

    public DbLoggerProvider(Channel<SystemLogEntry> channel)
    {
        _channel = channel;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new DbLogger(categoryName, _channel);
    }

    public void Dispose() { }
}
