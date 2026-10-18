using System.Drawing;

namespace HyperXBatteryTray;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;

    public TrayApplicationContext()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = Application.ProductName,
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Status: Inicializando...");
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Configurações");
        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Sair");
        exitItem.Click += (_, _) => ExitApplication();

        menu.Items.Add(exitItem);

        return menu;
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}