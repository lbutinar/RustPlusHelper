using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace RustPlusHelper.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Resources.Add("services", services);
        InitializeComponent();
#if DEBUG
        Loaded += CaptureWebViewForSmokeTestAsync;
#endif
    }

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
