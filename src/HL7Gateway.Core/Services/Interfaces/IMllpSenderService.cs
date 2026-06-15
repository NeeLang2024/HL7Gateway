namespace HL7Gateway.Core.Services.Interfaces;

public interface IMllpSenderService
{
    Task<(bool Success, string? Response, string? Error)> SendAsync(string host, int port, string hl7Message, int timeoutMs = 10000);
}
