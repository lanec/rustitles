using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WinTitles.App.ViewModels;
using WinTitles.App.Views;
using WinTitles.Core.Services;
using WinTitles.Core.Services.Subtitles;

namespace WinTitles.App;

public partial class App : Application
{
    private readonly ServiceProvider _serviceProvider;
    private Hardcodet.Wpf.TaskbarNotification.TaskbarIcon? _trayIcon;
    private static Mutex? _mutex;
    private const string MutexName = "WinTitles_SingleInstance_Mutex";

    public App()
    {
        // Single instance check
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("WinTitles is already running.\n\nCheck your system tray.", 
                "WinTitles", MessageBoxButton.OK, MessageBoxImage.Information);
            Environment.Exit(0);
            return;
        }

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddDebug();
        });

        // Core services
        services.AddSingleton<SubliminalService>();
        services.AddSingleton<FileScanner>();
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<DownloadManager>();
        services.AddSingleton<ScanService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<PlexService>();
        services.AddSingleton<PlexAuthService>();
        services.AddSingleton<PlexWebhookServer>();
        services.AddSingleton<OpenAIService>();
        services.AddHttpClient<PlexService>();
        services.AddHttpClient<PlexAuthService>();
        services.AddHttpClient<OpenSubtitlesService>();
        services.AddHttpClient<OpenAIService>();
        services.AddHttpClient<PodnapisiService>();
        
        // Subtitle providers (multi-provider aggregation like Subliminal)
        // Must be registered BEFORE DownloadQueue so DI can inject the aggregator
        services.AddSingleton<OpenSubtitlesService>();
        services.AddSingleton<PodnapisiService>();
        services.AddSingleton<ISubtitleProvider, OpenSubtitlesProvider>();
        services.AddSingleton<ISubtitleProvider, PodnapisiProvider>();
        services.AddSingleton<SubtitleAggregator>();
        
        // DownloadQueue depends on SubtitleAggregator - register after
        services.AddSingleton<DownloadQueue>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Set up global exception handling (throttled to prevent dialog spam)
        bool errorShown = false;
        
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (errorShown) return;
            errorShown = true;
            var ex = args.ExceptionObject as Exception;
            var logger = _serviceProvider.GetService<ILogger<App>>();
            logger?.LogError(ex, "Unhandled exception");
            MessageBox.Show($"Unhandled error: {ex?.Message}", 
                "WinTitles Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        
        DispatcherUnhandledException += (s, args) =>
        {
            var logger = _serviceProvider.GetService<ILogger<App>>();
            logger?.LogError(args.Exception, "UI exception");
            args.Handled = true; // Prevent crash, don't show dialog for binding errors
        };
        
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            var logger = _serviceProvider.GetService<ILogger<App>>();
            logger?.LogError(args.Exception, "Background task exception");
            args.SetObserved(); // Prevent crash
        };

        // Initialize database
        var database = _serviceProvider.GetRequiredService<DatabaseService>();
        await database.InitializeAsync();

        // Load settings
        var settings = _serviceProvider.GetRequiredService<SettingsService>();
        await settings.LoadAsync();

        // Check if subliminal is installed
        var subliminal = _serviceProvider.GetRequiredService<SubliminalService>();
        var isInstalled = await subliminal.IsInstalledAsync();
        
        var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
        logger.LogInformation("WinTitles starting. Subliminal installed: {Installed}", isInstalled);

        // Create system tray icon
        CreateTrayIcon();

        // Show main window (unless starting minimized)
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        if (!settings.Settings.StartMinimized || !settings.Settings.StartWithWindows)
        {
            mainWindow.Show();
        }

        // Auto-start Plex webhook if configured
        if (settings.Settings.Plex.Enabled && settings.Settings.Plex.AutoStartWebhook)
        {
            var vm = _serviceProvider.GetRequiredService<MainViewModel>();
            await vm.StartPlexServiceAsync();
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new Hardcodet.Wpf.TaskbarNotification.TaskbarIcon
        {
            ToolTipText = "WinTitles - Ready",
            Visibility = Visibility.Visible,
            Icon = CreateAppIcon()
        };
        
        // Left-click to show main window
        _trayIcon.TrayLeftMouseDown += (s, e) => ShowMainWindow();

        // Create context menu
        var menu = new System.Windows.Controls.ContextMenu();
        
        var showItem = new System.Windows.Controls.MenuItem { Header = "Show WinTitles" };
        showItem.Click += (s, e) => ShowMainWindow();
        menu.Items.Add(showItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var scanPlexItem = new System.Windows.Controls.MenuItem { Header = "Scan Plex Library" };
        scanPlexItem.Click += async (s, e) =>
        {
            var vm = _serviceProvider.GetRequiredService<MainViewModel>();
            await vm.ScanPlexLibraryAsync();
        };
        menu.Items.Add(scanPlexItem);

        var scanFolderItem = new System.Windows.Controls.MenuItem { Header = "Scan Folder..." };
        scanFolderItem.Click += async (s, e) =>
        {
            var vm = _serviceProvider.GetRequiredService<MainViewModel>();
            await vm.ScanFolderAsync();
        };
        menu.Items.Add(scanFolderItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (s, e) => ExitApplication();
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenu = menu;
        _trayIcon.TrayMouseDoubleClick += (s, e) => ShowMainWindow();
    }

    /// <summary>
    /// Load the app icon from embedded resource or create one programmatically.
    /// </summary>
    private static System.Drawing.Icon CreateAppIcon()
    {
        // Try to load from file first
        var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
        if (System.IO.File.Exists(iconPath))
        {
            return new System.Drawing.Icon(iconPath);
        }
        
        // Fallback: create programmatically
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        
        // Dark background circle
        using var bgBrush = new SolidBrush(Color.FromArgb(40, 42, 54));
        graphics.FillEllipse(bgBrush, 0, 0, size - 1, size - 1);
        
        // Purple border
        using var borderPen = new Pen(Color.FromArgb(189, 147, 249), 2);
        graphics.DrawEllipse(borderPen, 1, 1, size - 3, size - 3);
        
        // White "W" text
        using var font = new Font("Segoe UI", 14, System.Drawing.FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(248, 248, 242));
        
        var textSize = graphics.MeasureString("W", font);
        var x = (size - textSize.Width) / 2;
        var y = (size - textSize.Height) / 2;
        graphics.DrawString("W", font, textBrush, x, y);
        
        // Convert to icon
        var hIcon = bitmap.GetHicon();
        return System.Drawing.Icon.FromHandle(hIcon);
    }

    private void ShowMainWindow()
    {
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
        mainWindow.WindowState = WindowState.Normal;
        mainWindow.Activate();
    }

    private void ExitApplication()
    {
        _trayIcon?.Dispose();
        _serviceProvider.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _serviceProvider.Dispose();
        base.OnExit(e);
    }
}
