using System.Net;
using System.Net.Sockets;

namespace EventStreamClient;

public static class SseClientFactory
{
    public static HttpClient Create(string url, Action? onConnect = default) =>
        Create(new Uri(url), HttpVersion.Version20, onConnect);

    public static HttpClient Create(string url, Version httpVersion, Action? onConnect = default) =>
        Create(new Uri(url), httpVersion, onConnect);

    public static HttpClient Create(Uri url, Version httpVersion, Action? onConnect)
    {
        var handler = new SocketsHttpHandler();
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        handler.ConnectCallback = async (context, cancellationToken) =>
        {
            onConnect?.Invoke();
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
            return new NetworkStream(socket, true);
        };

        return new HttpClient(handler)
        {
            BaseAddress = url,
            DefaultRequestVersion = httpVersion,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
    }
}
