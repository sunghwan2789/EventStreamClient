using System.CommandLine;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace EventStreamClient;

public class StreamCommand : Command
{
    public StreamCommand() : base("stream")
    {
        var url = DefaultOptions.Url;
        var queryPayload = DefaultOptions.QueryPayload;

        Add(url);
        Add(queryPayload);
        this.SetHandler(Handle, url, queryPayload);
    }

    private async Task Handle(string url, string queryPayload)
    {
        var client = SseClientFactory.Create(url);

        using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty)
        {
            Content = JsonContent.Create(new
            {
                query = queryPayload
            }),
#if NETCOREAPP3_1_OR_GREATER
            Version = HttpVersion.Version20,
#endif
        };
        request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(input: "text/event-stream"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

#if NETCOREAPP3_1_OR_GREATER
        await using var stream = await response.Content.ReadAsStreamAsync();
#else
        using var stream = await response.Content.ReadAsStreamAsync();
#endif
        var reader = new StreamReader(stream, Encoding.UTF8);
        var parser = new EventStreamParser();

        var count = 0;
        var stopwatch = Stopwatch.StartNew();
        using var _ = new Timer(_ =>
        {
            var elapsed = stopwatch.Elapsed;
            Console.WriteLine($"{count / elapsed.TotalSeconds:F1} events / sec | {elapsed.TotalSeconds:F0}s elapsed | total: {count}");
        }, null, 1000, 1000);

        var eventType = string.Empty;
        var data = new StringBuilder();
        while ((await reader.ReadLineAsync()) is string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                if (data.Length > 0)
                {
                    data.Length--;
                }
                var eventStream = new EventStreamParser.EventStream
                {
                    EventType = eventType,
                    Data = data.ToString(),
                };
                eventType = string.Empty;
                data.Clear();
                count++;
                // Console.WriteLine("{0}: {1}", eventStream.EventType, eventStream.Data);
                continue;
            }

            if (line[0] == ':')
            {
                continue;
            }

            switch (line.Split(':', 2))
            {
                case ["event", var e]:
                    eventType = e;
                    break;

                case ["data", var d]:
                    if (d.StartsWith(' '))
                    {
                        d = d[1..];
                    }
                    data.AppendLine(d);
                    break;
            }
        }
    }

    private class EventStreamParser
    {
        public class EventStream
        {
            public string EventType { get; set; } = string.Empty;
            public string Data { get; set; } = string.Empty;
        }
    }
}
