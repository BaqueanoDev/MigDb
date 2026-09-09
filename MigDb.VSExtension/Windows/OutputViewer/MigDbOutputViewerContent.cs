using Microsoft.VisualStudio.Extensibility.UI;

namespace MigDb.VSExtension.Windows.OutputViewer;

internal sealed class MigDbOutputViewerContent(MigDbOutputViewerData dataContext) : RemoteUserControl(dataContext);
