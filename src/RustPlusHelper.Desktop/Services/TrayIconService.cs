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
    private readonly System.Drawing.Icon _icon;
    private readonly System.Windows.Threading.Dispatcher _dispatcher;

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        // The NotifyIcon's native window is created on whichever thread constructs it (the WPF UI
        // thread, via DI). Notifications are triggered from background poll/FCM callbacks, so calls
        // into the WinForms control must be marshaled back here — WinForms controls are not thread-safe.
        _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open RustPlusHelper", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = LoadIcon();
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "RustPlusHelper",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Show(string title, string message)
    {
        if (_dispatcher.CheckAccess())
        {
            _notifyIcon.ShowBalloonTip(10_000, title, message, System.Windows.Forms.ToolTipIcon.Info);
            return;
        }

        _dispatcher.BeginInvoke(() =>
            _notifyIcon.ShowBalloonTip(10_000, title, message, System.Windows.Forms.ToolTipIcon.Info));
    }

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
        _icon.Dispose();
    }
}
