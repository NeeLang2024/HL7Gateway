using System.Net.Sockets;
using System.Text;
using HL7Gateway.Core.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace HL7Gateway.Service.Services;

public class MllpSenderService : IMllpSenderService
{
    private readonly ILogger<MllpSenderService> _logger;

    public MllpSenderService(ILogger<MllpSenderService> logger) => _logger = logger;

    public async Task<(bool Success, string? Response, string? Error)> SendAsync(
        string host, int port, string hl7Message, int timeoutMs = 10000)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);

            await client.ConnectAsync(host, port, cts.Token);
            var stream = client.GetStream();

            var msgBytes = Encoding.UTF8.GetBytes(hl7Message);
            var frame = new byte[msgBytes.Length + 3];
            frame[0] = 0x0B;
            msgBytes.CopyTo(frame, 1);
            frame[^2] = 0x1C;
            frame[^1] = 0x0D;

            await stream.WriteAsync(frame, cts.Token);
            await stream.FlushAsync(cts.Token);

            var responseBuf = new byte[4096];
            var bytesRead = await stream.ReadAsync(responseBuf, cts.Token);

            if (bytesRead > 0)
            {
                var response = Encoding.UTF8.GetString(responseBuf, 0, bytesRead);
                var ackCode = response.Contains("MSA|AA") ? "AA" :
                              response.Contains("MSA|AE") ? "AE" :
                              response.Contains("MSA|AR") ? "AR" : "UNKNOWN";
                _logger.LogInformation("MLLP send OK to {Host}:{Port}, ACK={Ack}", host, port, ackCode);
                return (true, response, null);
            }

            return (false, null, "Empty response");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MLLP send FAILED to {Host}:{Port}: {Msg}", host, port, ex.Message);
            return (false, null, $"MLLP send error: {ex.Message}");
        }
    }
}
