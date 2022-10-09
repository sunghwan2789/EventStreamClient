using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using EventStreamClient;

new CommandLineBuilder(new RootCommand()
{
    new RawCommand(),
    new DecodeCommand(),
})
    .UseDefaults()
    .Build()
    .Invoke(args);
