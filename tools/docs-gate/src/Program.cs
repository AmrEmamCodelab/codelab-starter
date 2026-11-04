using Starter.Tools.DocsGate.Cli;

return DocsGateCli.Run(
    arguments: args,
    output: Console.Out,
    error: Console.Error,
    readEnvironment: Environment.GetEnvironmentVariable);
