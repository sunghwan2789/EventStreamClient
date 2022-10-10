using System.CommandLine;
using System.Net.Http.Json;
using Serilog;

namespace EventStreamClient;

public class RawCommand : Command
{
    public RawCommand() : base("raw")
    {
        var url = DefaultOptions.Url;
        var httpVersion = DefaultOptions.HttpVersion;
        var queryPayload = DefaultOptions.QueryPayload;
        var subscriptionPayload = DefaultOptions.SubscriptionPayload;

        Add(url);
        Add(httpVersion);
        Add(queryPayload);
        Add(subscriptionPayload);
        this.SetHandler(Handle, url, httpVersion, queryPayload, subscriptionPayload);
    }

    private async Task Handle(string url, string httpVersionInput, string queryPayload, string subscriptionPayload)
    {
        var httpVersion = Version.Parse(httpVersionInput);

        var client = SseClientFactory.Create(
            url,
            httpVersion,
            onConnect: () => Console.WriteLine("make a new connection"));

        Console.WriteLine("= http query");
        {
            using var request = JsonContent.Create(new
            {
                query = queryPayload
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
                    query = queryPayload
                }),
                Version = httpVersion,
            };
            request.Headers.Add("Accept", "text/event-stream");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            Console.WriteLine(response.Version);

#if NETCOREAPP3_1_OR_GREATER
            await using var stream = await response.Content.ReadAsStreamAsync();
#else
            using var stream = await response.Content.ReadAsStreamAsync();
#endif
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
                    query = subscriptionPayload
                }),
                Version = httpVersion,
            };
            request.Headers.Add("Accept", "text/event-stream");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            Console.WriteLine(response.Version);

#if NETCOREAPP3_1_OR_GREATER
            await using var stream = await response.Content.ReadAsStreamAsync();
#else
            using var stream = await response.Content.ReadAsStreamAsync();
#endif
            using var sr = new StreamReader(stream);
            while ((await sr.ReadLineAsync()) is string line)
            {
                logger.Information(line);
            }
        })));
    }
}
