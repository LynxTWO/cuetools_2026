using System.Windows;
using CUETools.Processor;
using CUETools.Wpf.Services;
using CUETools.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CUETools.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        // Explicit factory: CUEConfig has a copy constructor too, and the DI container would
        // otherwise pick it and self-recurse. Force the parameterless ctor, then apply the app's
        // preferred defaults (until settings persistence lands).
        services.AddSingleton<CUEConfig>(_ =>
        {
            var c = new CUEConfig();
            c.detectHDCD = true;            // flag HDCD discs by default
            c.decodeHDCD = false;           // but do not alter the audio unless asked
            c.decodeHDCDto24bit = false;    // 24-bit HDCD output off by default
            c.maxAlbumArtSize = 1000;       // embed/extract art up to 1000 px
            c.writeArTagsOnEncode = true;   // AccurateRip tags in the files by default
            return c;
        });
        services.AddSingleton<AppSettings>();
        services.AddSingleton<IDriveService, DriveService>();
        services.AddSingleton<IRipService, RipService>();
        services.AddSingleton<IVerifyService, VerifyService>();
        services.AddSingleton<IConvertService, ConvertService>();
        services.AddSingleton<IReportStore, ReportStore>();
        services.AddSingleton<IHistoryStore, HistoryStore>();
        services.AddSingleton<IAlbumArtService, AlbumArtService>();
        services.AddSingleton<IDiagnosticLog, DiagnosticLog>();
        services.AddSingleton<ThemeService>();

        // Nav destinations, in display order. Registered as PageViewModel so MainViewModel
        // receives them as one ordered collection.
        services.AddSingleton<PageViewModel, RipViewModel>();
        services.AddSingleton<PageViewModel, VerifyViewModel>();
        services.AddSingleton<PageViewModel, ConvertViewModel>();
        services.AddSingleton<PageViewModel, QueueViewModel>();
        services.AddSingleton<PageViewModel, ReportViewModel>();
        services.AddSingleton<PageViewModel, DriveViewModel>();
        services.AddSingleton<PageViewModel, SettingsViewModel>();
        services.AddSingleton<PageViewModel, ExploreViewModel>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        var provider = services.BuildServiceProvider();

        // Diagnostic log + global crash handlers first, so anything below is captured. The log is
        // privacy-safe (see DiagnosticLog): structure only, no album/artist/track names.
        var log = provider.GetRequiredService<IDiagnosticLog>();
        log.Info("app", $"start clr={Environment.Version} os={Environment.OSVersion.Version} x64={Environment.Is64BitProcess} log={log.LogPath}");
        DispatcherUnhandledException += (s, e) => { log.Error("crash.ui", "unhandled UI-thread exception (kept alive)", e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (s, e) => log.Error("crash.fatal", "unhandled exception (terminating)", e.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => { log.Error("crash.task", "unobserved task exception", e.Exception); e.SetObserved(); };

        // apply the saved theme (mutates the palette brushes) before the window shows
        var theme = provider.GetRequiredService<ThemeService>();
        theme.Apply(theme.Current);

        var window = provider.GetRequiredService<MainWindow>();
        window.DataContext = provider.GetRequiredService<MainViewModel>();

        // Windows' DWrite / font-cache layer can intermittently throw FileNotFoundException during the
        // very first text layout. If that escapes Show() it aborts startup and leaves the process
        // running with no visible window (the app looks like it "did nothing"). Retry the show + layout
        // a few times - the font cache resolves on a later attempt - and bring the window to the front.
        bool shown = false;
        for (int attempt = 1; attempt <= 5 && !shown; attempt++)
        {
            try
            {
                if (!window.IsVisible) window.Show();
                window.UpdateLayout();
                window.Activate();
                shown = true;
                log.Info("app", $"window shown (attempt {attempt})");
            }
            catch (Exception ex)
            {
                log.Warn("app", $"window show/layout failed (attempt {attempt}): {ex.GetType().Name} - retrying");
                System.Threading.Thread.Sleep(220);
            }
        }
        if (!shown) log.Error("app", "window did not show after retries", null);
    }
}
