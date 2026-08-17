using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RustPlusHelper.Application.Identity;
using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;
using RustPlusHelper.Application.Testing;
using RustPlusHelper.Infrastructure.Storage;
using RustPlusHelper.Infrastructure.Storage.Identity;
using RustPlusHelper.Infrastructure.Storage.Security;
using RustPlusHelper.Infrastructure.Storage.Servers;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddWpfBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif
        builder.Services.AddSingleton<IRustPlusClient, FakeRustPlusClient>();
        builder.Services.AddSingleton(new RustPlusConnectionOptions(
            "fake.invalid",
            28082,
            ulong.MaxValue - 42,
            0));
        builder.Services.AddSingleton<MapDashboardService>();
        builder.Services.AddSingleton(TimeProvider.System);

        var database = new SqliteDatabase(ApplicationDataPaths.GetDatabasePath());
        database.Initialize();
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<IServerRepository, SqliteServerRepository>();
        builder.Services.AddSingleton<IPlayerIdentityRepository, SqlitePlayerIdentityRepository>();
        builder.Services.AddSingleton<PlayerIdentityManager>();
        builder.Services.AddSingleton<ISecretProtector, WindowsDpapiSecretProtector>();
        builder.Services.AddSingleton<ISecretStore, SqliteSecretStore>();
        builder.Services.AddSingleton<ServerManager>();

        _host = builder.Build();
        _host.StartAsync().GetAwaiter().GetResult();
        _host.Services.GetRequiredService<PlayerIdentityManager>().Load();
        _host.Services.GetRequiredService<ServerManager>().Load();

        var window = new MainWindow(_host.Services);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
