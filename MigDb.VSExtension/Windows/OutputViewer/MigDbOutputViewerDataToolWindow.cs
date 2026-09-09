using MigDb.VSExtension.Services;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace MigDb.VSExtension.Windows.OutputViewer;

[VisualStudioContribution]
internal class MigDbOutputViewerDataToolWindow : ToolWindow
{
    private readonly OutputBufferService OutputBuffer;
    private MigDbOutputViewerContent? content;

    public MigDbOutputViewerDataToolWindow(OutputBufferService outputBuffer)
    {
        OutputBuffer = outputBuffer;
        Title = "MigDb Output";
    }

    /// <inheritdoc/>
    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.DocumentWell,
    };

    /// <inheritdoc/>
    public override Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        content ??= new MigDbOutputViewerContent(new MigDbOutputViewerData(OutputBuffer));

        return Task.FromResult<IRemoteUserControl>(content);
    }
}
