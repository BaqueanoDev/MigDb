using MigDb.VSExtension.Services;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.UI;

namespace MigDb.VSExtension.Windows.Migration;

internal class MigDbMigrationToolWindowContent(VisualStudioExtensibility extensibility, MigDbCLIService migDbCLIService, OutputBufferService outputBuffer)
    : RemoteUserControl(dataContext: new MigDbMigrationToolWindowData(extensibility, migDbCLIService, outputBuffer))
{
    /// <inheritdoc/>
    public override async Task ControlLoadedAsync(CancellationToken cancellationToken)
    {
        await base.ControlLoadedAsync(cancellationToken);

        // Apparently RemoteUserControls constructors are sync
        // doint some init things here avoids slowing down the window opening thread
        if (DataContext is MigDbMigrationToolWindowData data)
        {
            data.SetCLISetupState();
            await data.LoadProjectListAsync(cancellationToken);

            if (!migDbCLIService.IsSetup)
                return;

            await data.LoadMigrationDirectoryListAsync(cancellationToken);
        }
    }
}
