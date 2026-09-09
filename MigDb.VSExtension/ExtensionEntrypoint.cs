
using MigDb.VSExtension.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace MigDb.VSExtension;

/// <summary>
/// Extension entrypoint for the VisualStudio.Extensibility extension.
/// </summary>
[VisualStudioContribution]
internal class ExtensionEntrypoint : Extension
{
    /// <inheritdoc/>
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
                id: "MigDb.VSExtension.f1b1ec3b-5376-4511-9a61-28df612d01b1",
                version: ExtensionAssemblyVersion,
                publisherName: "Rodrigo Sanchez",
                displayName: "MigDb Tool",
                description: "Manage database migrations")
        {
            DotnetTargetVersions = [DotnetTarget.Custom("net10.0")],
        },
    };

    /// <inheritdoc />
    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);

        serviceCollection.AddSingleton<MigDbCLIService>();
        serviceCollection.AddSingleton<OutputBufferService>();
    }
}
