using RustPlusHelper.Application.Notifications;

namespace RustPlusHelper.Desktop.Services;

/// <summary>
/// Owns the Windows system tray icon and doubles as the <see cref="IDesktopNotifier"/> delivery
/// channel (tray balloon, not a modern Action Center toast — see AGENTS.md's notification rules for
/// why). Deliberately isolated in its own file using only fully-qualified
/// <c>System.Windows.Forms</c> names: a bare <c>using System.Windows.Forms;</c> next to WPF's own
/// <c>System.Windows</c> namespace elsewhere in this project causes ambiguous-reference errors on
/// types like <c>Application</c>/<c>MessageBox</c> that exist in both.
/// </summary>
public sealed class TrayIconService : IDesktopNotifier, IDisposable
{
    private const string IconResourceName = "RustPlusHelper.Desktop.Assets.rustplus-tray-icon.ico";

    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open RustPlusHelper", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "RustPlusHelper",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Show(string title, string message) =>
        _notifyIcon.ShowBalloonTip(10_000, title, message, System.Windows.Forms.ToolTipIcon.Info);

    private static System.Drawing.Icon LoadIcon()
    {
        using var stream = typeof(TrayIconService).Assembly.GetManifestResourceStream(IconResourceName)
            ?? throw new InvalidOperationException($"Embedded tray icon resource '{IconResourceName}' was not found.");
        return new System.Drawing.Icon(stream);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
