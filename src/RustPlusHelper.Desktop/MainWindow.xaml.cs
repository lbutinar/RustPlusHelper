using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace RustPlusHelper.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Resources.Add("services", services);
        InitializeComponent();
        SourceInitialized += PlaceOnLargestDisplay;
#if DEBUG
        Loaded += CaptureWebViewForSmokeTestAsync;
#endif
    }

    private void PlaceOnLargestDisplay(object? sender, EventArgs e)
    {
        var workArea = GetDisplayWorkAreas()
            .OrderByDescending(area => (long)area.Width * area.Height)
            .FirstOrDefault();
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return;
        }

        const double workAreaUsage = 0.9;
        var width = Math.Max(1, (int)Math.Round(workArea.Width * workAreaUsage));
        var height = Math.Max(1, (int)Math.Round(workArea.Height * workAreaUsage));
        var left = workArea.Left + ((workArea.Width - width) / 2);
        var top = workArea.Top + ((workArea.Height - height) / 2);
        var windowHandle = new WindowInteropHelper(this).Handle;

        if (!SetWindowPos(
                windowHandle,
                IntPtr.Zero,
                left,
                top,
                width,
                height,
                SetWindowPosFlags.NoActivate | SetWindowPosFlags.NoZOrder))
        {
            System.Diagnostics.Debug.WriteLine("The initial window bounds could not be applied.");
        }
    }

    private static IReadOnlyList<PixelBounds> GetDisplayWorkAreas()
    {
        var workAreas = new List<PixelBounds>();
        MonitorEnumerationCallback callback = (monitor, _, _, _) =>
        {
            var monitorInfo = new MonitorInfo
            {
                Size = (uint)Marshal.SizeOf<MonitorInfo>()
            };

            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                workAreas.Add(new PixelBounds(
                    monitorInfo.WorkArea.Left,
                    monitorInfo.WorkArea.Top,
                    monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left,
                    monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top));
            }

            return true;
        };

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            System.Diagnostics.Debug.WriteLine("Connected displays could not be enumerated.");
        }

        GC.KeepAlive(callback);
        return workAreas;
    }

    private delegate bool MonitorEnumerationCallback(
        IntPtr monitor,
        IntPtr deviceContext,
        IntPtr monitorBounds,
        IntPtr state);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clippingRectangle,
        MonitorEnumerationCallback callback,
        IntPtr state);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        SetWindowPosFlags flags);

    [Flags]
    private enum SetWindowPosFlags : uint
    {
        NoZOrder = 0x0004,
        NoActivate = 0x0010
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRectangle MonitorArea;
        public NativeRectangle WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private readonly record struct PixelBounds(int Left, int Top, int Width, int Height);

#if DEBUG
    private async void CaptureWebViewForSmokeTestAsync(object sender, RoutedEventArgs e)
    {
        var outputPath = Environment.GetEnvironmentVariable("RUSTPLUSHELPER_UI_CAPTURE_PATH");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        try
        {
            await blazorWebView.WebView.EnsureCoreWebView2Async();
            await Task.Delay(TimeSpan.FromSeconds(8));

            var section = Environment.GetEnvironmentVariable("RUSTPLUSHELPER_UI_CAPTURE_SECTION");
            var readySelector = ".app-shell";
            if (string.Equals(section, "Servers", StringComparison.OrdinalIgnoreCase))
            {
                var navigated = await blazorWebView.WebView.CoreWebView2.ExecuteScriptAsync(
                    "(() => { const button = [...document.querySelectorAll('.nav-item')].find(item => item.textContent.includes('Servers')); if (!button) return false; button.click(); return true; })()");
                if (!string.Equals(navigated, "true", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The Servers navigation item was not available.");
                }

                readySelector = ".servers-page";
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var isReady = await blazorWebView.WebView.CoreWebView2.ExecuteScriptAsync(
                $"Boolean(document.querySelector('{readySelector}'))");
            if (!string.Equals(isReady, "true", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The Blazor application did not reach its rendered shell.");
            }

            if (string.Equals(
                    Environment.GetEnvironmentVariable("RUSTPLUSHELPER_UI_CAPTURE_LIVE_TEST"),
                    "1",
                    StringComparison.Ordinal))
            {
                var started = await blazorWebView.WebView.CoreWebView2.ExecuteScriptAsync(
                    "(() => { const button = document.querySelector('[data-testid=\"test-server-connection\"]'); if (!button) return false; button.click(); return true; })()");
                if (!string.Equals(started, "true", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A saved server connection test was not available.");
                }

                var completed = false;
                for (var attempt = 0; attempt < 70; attempt++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                    var testState = await blazorWebView.WebView.CoreWebView2.ExecuteScriptAsync(
                        "(() => { const result = document.querySelector('[data-testid=\"connection-test-result\"]'); if (!result || result.classList.contains('connection-progress')) return 'waiting'; return 'complete'; })()");
                    if (string.Equals(testState, "\"complete\"", StringComparison.Ordinal))
                    {
                        completed = true;
                        break;
                    }
                }

                if (!completed)
                {
                    throw new TimeoutException("The saved server connection test did not finish within 35 seconds.");
                }
            }

            var layerToToggle = Environment.GetEnvironmentVariable("RUSTPLUSHELPER_UI_CAPTURE_LAYER");
            if (!string.IsNullOrWhiteSpace(layerToToggle))
            {
                var encodedLayer = System.Text.Json.JsonSerializer.Serialize(layerToToggle);
                var toggled = await blazorWebView.WebView.CoreWebView2.ExecuteScriptAsync(
                    $"(() => {{ const label = [...document.querySelectorAll('.layer-row')].find(item => item.querySelector('strong')?.textContent.trim() === {encodedLayer}); const input = label?.querySelector('input'); if (!input || input.disabled) return false; input.checked = true; input.dispatchEvent(new Event('change', {{ bubbles: true }})); return input.checked; }})()");
                if (!string.Equals(toggled, "true", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The requested map layer was not available for UI capture.");
                }
            }

            if (int.TryParse(
                    Environment.GetEnvironmentVariable("RUSTPLUSHELPER_UI_CAPTURE_HOLD_SECONDS"),
                    out var holdSeconds)
                && holdSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(holdSeconds, 60)));
            }

            if (!string.IsNullOrWhiteSpace(layerToToggle))
            {
                var encodedLayer = System.Text.Json.JsonSerializer.Serialize(layerToToggle);
                var remainsEnabled = await blazorWebView.WebView.CoreWebView2.ExecuteScriptAsync(
                    $"(() => {{ const label = [...document.querySelectorAll('.layer-row')].find(item => item.querySelector('strong')?.textContent.trim() === {encodedLayer}); return label?.querySelector('input')?.checked === true; }})()");
                if (!string.Equals(remainsEnabled, "true", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The requested map layer did not remain enabled during UI capture.");
                }
            }

            await using var output = File.Create(fullPath);
            await blazorWebView.WebView.CoreWebView2.CapturePreviewAsync(
                CoreWebView2CapturePreviewImageFormat.Png,
                output);
            Close();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"UI smoke capture failed: {exception.Message}");
            System.Windows.Application.Current.Shutdown(1);
        }
    }
#endif
}
