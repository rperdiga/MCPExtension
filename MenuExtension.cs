using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.UI.Menu;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;

namespace MCPExtension.WebView.DockingPanes;

[Export(typeof(MenuExtension))]
class MyMenuExtension : MenuExtension
{
    readonly IDockingWindowService dockingWindowService;

    [ImportingConstructor]
    public MyMenuExtension(IDockingWindowService dockingWindowService)
    {
        this.dockingWindowService = dockingWindowService;
    }

    public override IEnumerable<MenuViewModel> GetMenus()
    {
        yield return new MenuViewModel(
            caption: "MCP Server",
            action: () =>
            {
                if (CurrentApp == null)
                    throw new InvalidOperationException();

                dockingWindowService.OpenPane(AIAPIEngine.ID);
            }
        );
    }
}
