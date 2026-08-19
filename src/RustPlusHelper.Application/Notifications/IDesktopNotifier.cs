namespace RustPlusHelper.Application.Notifications;

/// <summary>Application-owned boundary for showing a desktop notification — keeps
/// <c>System.Windows.Forms.NotifyIcon</c> out of the Application layer entirely.</summary>
public interface IDesktopNotifier
{
    void Show(string title, string message);
}
