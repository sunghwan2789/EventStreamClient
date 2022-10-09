using System.CommandLine;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Serilog;

namespace EventStreamClient;

public class RawCommand : Command
{
    public RawCommand() : base("raw")
    {
        // use https://github.com/sunghwan2789/gql-playground as a server.
        var url = new Argument<string>("url", getDefaultValue: () => "https://localhost:7271/graphql");
        var http2 = new Option<bool>("--http2");

        Add(url);
        Add(http2);
        this.SetHandler(Handle, url, http2);
    }

    private async Task Handle(string url, bool http2)
    {
        var httpVersion = http2 ? HttpVersion.Version20 : HttpVersion.Version11;

        var client = CreateHttpClient(url, httpVersion);

        Console.WriteLine("= http query");
        {
            using var request = JsonContent.Create(new
            {
                query = "{ id }"
            });
            using var response = await client.PostAsync(string.Empty, request);
            Console.WriteLine(response.Version);

            var result = await response.Content.ReadAsStringAsync();
            Console.WriteLine(result);
        }

        Console.WriteLine("= sse query");
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty)
            {
                Content = JsonContent.Create(new
                {
                    query = "{ id }"
                }),
                Version = httpVersion,
            };
            request.Headers.Add("Accept", "text/event-stream");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            Console.WriteLine(response.Version);

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var sr = new StreamReader(stream);
            while ((await sr.ReadLineAsync()) is string line)
            {
                Console.WriteLine(line);
            }
        }

        Console.WriteLine("= sse subscription");
        await Task.WhenAll(Enumerable.Range(1, 2).Select(x => Task.Run(async () =>
        {
            var logger = new LoggerConfiguration()
                .WriteTo.Console(
                    outputTemplate: "{@No}] {Message:lj}{NewLine}"
                )
                .Enrich.WithProperty("No", x)
                .CreateLogger();

            using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty)
            {
                Content = JsonContent.Create(new
                {
                    query = "subscription { bookPublished(seed: 2) { id } }"
                }),
                Version = httpVersion,
            };
            request.Headers.Add("Accept", "text/event-stream");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            Console.WriteLine(response.Version);

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var sr = new StreamReader(stream);
            while ((await sr.ReadLineAsync()) is string line)
            {
                logger.Information(line);
            }
        })));
    }

    private static HttpClient CreateHttpClient(string origin, Version httpVersion)
    {
        var handler = new SocketsHttpHandler();
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        handler.ConnectCallback = async (context, cancellationToken) =>
        {
            Console.WriteLine("make a new connection");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
            return new NetworkStream(socket, true);
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri(origin),
            DefaultRequestVersion = httpVersion,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
    }
}