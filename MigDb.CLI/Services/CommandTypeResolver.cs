using Spectre.Console.Cli;

namespace MigDb.CLI.Services;

internal sealed class TypeResolver(IServiceProvider provider) : ITypeResolver
{
    public object? Resolve(Type? type)
    {
        if (type is null)
            return null;

        return provider.GetService(type);
    }
}