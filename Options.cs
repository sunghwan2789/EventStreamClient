using System.CommandLine;

namespace EventStreamClient;

public static class DefaultOptions
{
    // use https://github.com/sunghwan2789/gql-playground as a server.
    public static readonly Argument<string> Url = new("url", getDefaultValue: () => "https://localhost:7271/graphql");
    public static readonly Option<string> HttpVersion = new Option<string>("--http", getDefaultValue: () => "2.0")
        .FromAmong("1.1", "2.0");
}
