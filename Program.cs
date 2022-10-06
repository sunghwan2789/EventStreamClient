using System.Net.Http.Json;
using Serilog;

// use https://github.com/sunghwan2789/gql-playground as a server.
const string origin = "https://localhost:7271";
const string endpoint = "/graphql";
var client = new HttpClient
{
    BaseAddress = new Uri(origin),
};

Console.WriteLine("= http query");
{
    using var request = JsonContent.Create(new
    {
        query = "{ id }"
    });
    using var response = await client.PostAsync(endpoint, request);

    var result = await response.Content.ReadAsStringAsync();
    Console.WriteLine(result);
}

Console.WriteLine("= sse query");
{
    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
    {
        Content = JsonContent.Create(new
        {
            query = "{ id }"
        }),
    };
    request.Headers.Add("Accept", "text/event-stream");
    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

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

    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
    {
        Content = JsonContent.Create(new
        {
            query = "subscription { bookPublished(seed: 2) { id } }"
        }),
    };
    request.Headers.Add("Accept", "text/event-stream");
    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

    await using var stream = await response.Content.ReadAsStreamAsync();
    using var sr = new StreamReader(stream);
    while ((await sr.ReadLineAsync()) is string line)
    {
        logger.Information(line);
    }
})));
