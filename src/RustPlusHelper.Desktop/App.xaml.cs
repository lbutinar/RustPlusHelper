using System.Reflection;
using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusHelper.Application.Diagnostics;
using RustPlusHelper.Application.Identity;
using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.Notifications;
using RustPlusHelper.Application.Pairing;
using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Servers;
using RustPlusHelper.Application.Testing;
using RustPlusHelper.Desktop.Services;
using RustPlusHelper.Infrastructure.Map;
using RustPlusHelper.Infrastructure.RustPlus;
using RustPlusHelper.Infrastructure.Storage;
using RustPlusHelper.Infrastructure.Storage.Diagnostics;
using RustPlusHelper.Infrastructure.Storage.Identity;
using RustPlusHelper.Infrastructure.Storage.Logging;
using RustPlusHelper.Infrastructure.Storage.Map;
using RustPlusHelper.Infrastructure.Storage.RustPlus;
using RustPlusHelper.Infrastructure.Storage.Security;
using RustPlusHelper.Infrastructure.Storage.Servers;
using RustPlusHelper.Infrastructure.Storage.Sqlite;

namespace RustPlusHelper.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private MainWindow? _mainWindow;
    private bool _exitRequested;
    private bool _isOsShuttingDown;
    private Microsoft.Win32.PowerModeChangedEventHandler? _powerModeChangedHandler;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Set before any window exists: minimize-to-tray relies on Closing being reliably
        // cancellable without an incidental window-close path ever shutting the app down early.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var builder = Host.CreateApplicationBuilder();
        var logsDirectory = ApplicationDataPaths.GetLogsDirectory();
        builder.Logging.AddProvider(new FileLoggerProvider(logsDirectory, TimeProvider.System));
        builder.Services.AddWpfBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif
        builder.Services.AddSingleton<IRustPlusClient, FakeRustPlusClient>();
        builder.Services.AddSingleton<IRustPlusClientFactory, RustPlusApiClientFactory>();
        builder.Services.AddSingleton<RustPlusSavedConnectionResolver>();
        builder.Services.AddSingleton(serviceProvider => new RustPlusConnectionManager(
            serviceProvider.GetRequiredService<RustPlusSavedConnectionResolver>(),
            serviceProvider.GetRequiredService<IRustPlusClientFactory>(),
            serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(RustPlusPollingOptions.Default);
        builder.Services.AddSingleton<RustPlusLiveSessionManager>();
        builder.Services.AddSingleton(new RustPlusConnectionOptions(
            "fake.invalid",
            28082,
            ulong.MaxValue - 42,
            0));
        builder.Services.AddSingleton<MapDashboardService>();
        builder.Services.AddSingleton<IMapTopologyProvider, RustMapTopologyProvider>();
        builder.Services.AddSingleton<IMapTopologyDiscovery>(_ =>
            new RustMapCacheDiscovery(WindowsSteamRustInstallLocator.FindInstallations()));
        builder.Services.AddSingleton<MapTopologyManager>();
        builder.Services.AddSingleton<IMapFilePicker, WindowsMapFilePicker>();
        builder.Services.AddSingleton<IStartupRegistration, WindowsStartupRegistration>();
        builder.Services.AddSingleton(TimeProvider.System);

        var database = new SqliteDatabase(ApplicationDataPaths.GetDatabasePath());
        database.Initialize();
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<IServerRepository, SqliteServerRepository>();
        builder.Services.AddSingleton<IMapCacheRepository, SqliteMapCacheRepository>();
        builder.Services.AddSingleton<IMapTopologyRepository, SqliteMapTopologyRepository>();
        builder.Services.AddSingleton<ICompanionEventRepository, SqliteCompanionEventRepository>();
        builder.Services.AddSingleton<IMovementTrailRepository, SqliteMovementTrailRepository>();
        builder.Services.AddSingleton<ISavedCameraRepository, SqliteSavedCameraRepository>();
        builder.Services.AddSingleton<IPersonalMapPinRepository, SqlitePersonalMapPinRepository>();
        builder.Services.AddSingleton<IPairedEntityRepository, SqlitePairedEntityRepository>();
        builder.Services.AddSingleton<IPlayerIdentityRepository, SqlitePlayerIdentityRepository>();
        builder.Services.AddSingleton<PlayerIdentityManager>();
        builder.Services.AddSingleton<ISecretProtector, WindowsDpapiSecretProtector>();
        builder.Services.AddSingleton<ISecretStore, SqliteSecretStore>();
        builder.Services.AddSingleton<IApplicationSecretStore, SqliteApplicationSecretStore>();
        builder.Services.AddSingleton<ServerManager>();
        builder.Services.AddSingleton<IRustPlusPairingProvider, RustPlusApiPairingProvider>();
        builder.Services.AddSingleton<RustPlusPairingManager>();
        builder.Services.AddSingleton<RustPlusEntityPairingManager>();
        builder.Services.AddSingleton<IRustPlusAlarmListenerProvider, RustPlusApiAlarmListenerProvider>();
        builder.Services.AddSingleton<RustPlusAlarmNotificationListener>();
        builder.Services.AddSingleton<NotificationPreferencesStore>();
        builder.Services.AddSingleton<TrayIconService>();
        builder.Services.AddSingleton<IDesktopNotifier>(sp => sp.GetRequiredService<TrayIconService>());
        builder.Services.AddSingleton<NotificationDispatcher>();

        builder.Services.AddSingleton<IHealthCheck>(new DatabaseHealthCheck(database));
        builder.Services.AddSingleton<IHealthCheck>(sp => new SecretProtectorHealthCheck(sp.GetRequiredService<ISecretProtector>()));
        builder.Services.AddSingleton<IHealthCheck, WebView2HealthCheck>();
        var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        builder.Services.AddSingleton(sp => new DiagnosticsExportService(
            sp.GetServices<IHealthCheck>(),
            sp.GetRequiredService<IServerRepository>(),
            sp.GetRequiredService<TimeProvider>(),
            appVersion,
            logsDirectory));
        builder.Services.AddSingleton<IDiagnosticsExportFilePicker, WindowsDiagnosticsExportFilePicker>();
        builder.Services.AddSingleton<IEventExportFilePicker, WindowsEventExportFilePicker>();

        _host = builder.Build();
        _host.StartAsync().GetAwaiter().GetResult();

        // Each Load() below does its own local SQLite read and is independent of the others (none
        // reads state the other writes), so running them concurrently instead of back-to-back turns
        // this part of startup into the slowest single read instead of the sum of all five, shrinking
        // how long the process shows no window before Show() further down.
        var playerIdentity = _host.Services.GetRequiredService<PlayerIdentityManager>();
        var serverManager = _host.Services.GetRequiredService<ServerManager>();
        var pairingManager = _host.Services.GetRequiredService<RustPlusPairingManager>();
        var entityPairingManager = _host.Services.GetRequiredService<RustPlusEntityPairingManager>();
        var alarmListener = _host.Services.GetRequiredService<RustPlusAlarmNotificationListener>();
        Task.WhenAll(
            Task.Run(playerIdentity.Load),
            Task.Run(serverManager.Load),
            Task.Run(pairingManager.Load),
            Task.Run(entityPairingManager.Load),
            Task.Run(alarmListener.Load)).GetAwaiter().GetResult();

        _host.Services.GetRequiredService<NotificationDispatcher>(); // materializes and wires subscriptions

        PurgeStaleCompanionEvents();
        PurgeStaleMovementTrails();

        var trayIcon = _host.Services.GetRequiredService<TrayIconService>();
        trayIcon.OpenRequested += (_, _) => ShowMainWindow();
        trayIcon.ExitRequested += (_, _) =>
        {
            _exitRequested = true;
            Shutdown();
        };

        SessionEnding += (_, _) => _isOsShuttingDown = true;

        _powerModeChangedHandler = (_, args) =>
        {
            if (args.Mode != Microsoft.Win32.PowerModes.Resume)
            {
                return;
            }

            _host.Services.GetRequiredService<RustPlusLiveSessionManager>().RequestRefresh();
            _host.Services.GetRequiredService<RustPlusAlarmNotificationListener>().RequestReconnect();
        };
        Microsoft.Win32.SystemEvents.PowerModeChanged += _powerModeChangedHandler;

        var window = new MainWindow(_host.Services);
        _mainWindow = window;
        MainWindow = window;
        window.Closing += HandleMainWindowClosing;
        window.Show();
    }

    private void HandleMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exitRequested || _isOsShuttingDown)
        {
            return;
        }

        // Minimize to tray instead of exiting — background monitoring keeps running.
        e.Cancel = true;
        _mainWindow?.Hide();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void PurgeStaleCompanionEvents()
    {
        var repository = _host!.Services.GetRequiredService<ICompanionEventRepository>();
        var timeProvider = _host.Services.GetRequiredService<TimeProvider>();
        repository.PurgeOlderThan(timeProvider.GetUtcNow() - RustPlusLiveSessionManager.EventRetentionAge);
    }

    private void PurgeStaleMovementTrails()
    {
        var repository = _host!.Services.GetRequiredService<IMovementTrailRepository>();
        var timeProvider = _host.Services.GetRequiredService<TimeProvider>();
        repository.PurgeOlderThan(timeProvider.GetUtcNow() - RustPlusLiveSessionManager.MovementTrailRetentionAge);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_powerModeChangedHandler is not null)
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= _powerModeChangedHandler;
        }

        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();

            // Route through IAsyncDisposable when available: several singletons here (e.g.
            // RustPlusAlarmNotificationListener) implement both IDisposable and IAsyncDisposable, and
            // their synchronous Dispose() blocks on an in-flight background task that may be sleeping
            // through a retry backoff. The generic host's ServiceProvider prefers DisposeAsync when a
            // service supports it, so disposing asynchronously here avoids blocking this (UI) thread
            // any longer than the async path already does.
            if (_host is IAsyncDisposable asyncDisposableHost)
            {
                asyncDisposableHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            else
            {
                _host.Dispose();
            }
        }

        base.OnExit(e);
    }
}
