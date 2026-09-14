using System.Net.Sockets;
using System.Text;
using Rfid.Application.Contracts;

namespace Rfid.Api.Background;


/// <summary>Sends ZPL to a network label printer over the raw 9100 port.</summary>
public class RawPrinterClient : IPrinterClient
{
    public async Task SendAsync(string host, int port, string zpl, CancellationToken ct)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(host, port, timeout.Token);
        await using var stream = client.GetStream();
        var bytes = Encoding.UTF8.GetBytes(zpl);
        await stream.WriteAsync(bytes, timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }
}
