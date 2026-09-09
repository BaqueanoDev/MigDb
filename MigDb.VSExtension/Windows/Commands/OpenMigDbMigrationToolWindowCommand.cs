using MigDb.VSExtension.Windows.Migration;
using Microsoft;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using System.Diagnostics;

namespace MigDb.VSExtension.Windows.Commands;

/// <summary>
/// Command that opens (and activates) the <see cref="MigDbMigrationToolWindow"/>.
/// </summary>
[VisualStudioContribution]
internal class OpenMigDbMigrationToolWindowCommand(TraceSource traceSource) : Command
{
    private readonly TraceSource logger = Requires.NotNull(traceSource, nameof(traceSource));

    /// <inheritdoc/>
    public override CommandConfiguration CommandConfiguration => new("%MigDb.VSExtension.OpenMigDbToolWindow.DisplayName%")
    {
        Icon = new(ImageMoniker.KnownValues.ToolWindow, IconSettings.IconAndText),
        Placements = [CommandPlacement.KnownPlacements.ToolsMenu],
    };

    public override Task InitializeAsync(CancellationToken cancellationToken)
    {
        // Use InitializeAsync for any one-time setup or initialization.
        return base.InitializeAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await Extensibility.Shell().ShowToolWindowAsync<MigDbMigrationToolWindow>(activate: true, cancellationToken);
    }
}
