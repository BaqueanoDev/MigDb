using Spectre.Console.Cli;

namespace MigDb.CLI.Interceptors;

internal sealed class CompositeInterceptor(params ICommandInterceptor[] interceptors) : ICommandInterceptor
{
    public void Intercept(CommandContext context, CommandSettings settings)
    {
        foreach (ICommandInterceptor i in interceptors)
            i.Intercept(context, settings);
    }

    public void InterceptResult(CommandContext context, CommandSettings settings, ref int result)
    {
        foreach (ICommandInterceptor i in interceptors)
            i.InterceptResult(context, settings, ref result);
    }
}
