using MigDb.VSExtension.Services;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace MigDb.VSExtension.Windows.Migration;

[VisualStudioContribution]
internal class MigDbMigrationToolWindow : ToolWindow
{
    private readonly MigDbCLIService CliService;
    private readonly OutputBufferService OutputBuffer;
    private MigDbMigrationToolWindowContent? content;

    public MigDbMigrationToolWindow(MigDbCLIService cliService, OutputBufferService outputBuffer)
    {
        CliService = cliService;
        OutputBuffer = outputBuffer;
        Title = "MigDb";
    }

    /// <inheritdoc/>
    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.Floating,
    };

    /// <inheritdoc/>
    public override Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        content ??= new MigDbMigrationToolWindowContent(Extensibility, CliService, OutputBuffer);

        return Task.FromResult<IRemoteUserControl>(content);
    }
}
