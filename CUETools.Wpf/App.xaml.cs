using System.Windows;
using CUETools.Processor;
using CUETools.Wpf.Services;
using CUETools.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CUETools.Wpf;

public partial class App : Application
{
    /// <summary>The DI container, for view code-behind that opens dialogs (encoder settings).</summary>
    public static System.IServiceProvider? Services { get; private set; }

    private SettingsStore? _settingsStore;
    private CUEConfig? _config;
    private AppSettings? _appSettings;

    protected override void OnExit(ExitEventArgs e)
    {
        // persist every user setting (engine config + app prefs) on the way out
        if (_settingsStore != null && _config != null && _appSettings != null)
            _settingsStore.Save(_config, _appSettings);
        base.OnExit(e);
    }

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
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<EncoderCatalog>();
        services.AddSingleton<AppStatusService>();
        services.AddSingleton<CUETools.Wpf.Accuracy.DriveCalibrationStore>();
        services.AddSingleton<CUETools.Wpf.Accuracy.DriveCalibrationService>();
        services.AddSingleton<CUETools.Wpf.Accuracy.VerifyHistoryStore>(_ => new CUETools.Wpf.Accuracy.VerifyHistoryStore());
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
        services.AddSingleton<PageViewModel, NamingViewModel>();
        services.AddSingleton<PageViewModel, ExploreViewModel>();

        services.AddSingleton<MainViewModel>();
        // MainWindow is created directly (with a fresh instance per show-retry), not resolved here.

        var provider = services.BuildServiceProvider();
        Services = provider;

        // Diagnostic log + global crash handlers first, so anything below is captured. The log is
        // privacy-safe (see DiagnosticLog): structure only, no album/artist/track names.
        var log = provider.GetRequiredService<IDiagnosticLog>();
        log.Info("app", $"start clr={Environment.Version} os={Environment.OSVersion.Version} x64={Environment.Is64BitProcess} log={log.LogPath}");
        DispatcherUnhandledException += (s, e) => { log.Error("crash.ui", "unhandled UI-thread exception (kept alive)", e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (s, e) => log.Error("crash.fatal", "unhandled exception (terminating)", e.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => { log.Error("crash.task", "unobserved task exception", e.Exception); e.SetObserved(); };

        // restore the user's saved settings (engine config + app prefs) before anything reads them
        _settingsStore = provider.GetRequiredService<SettingsStore>();
        _config = provider.GetRequiredService<CUEConfig>();
        _appSettings = provider.GetRequiredService<AppSettings>();
        _settingsStore.Load(_config, _appSettings);

        // register externally-obtainable codecs (Musepack, OptimFROG) and, ONCE per profile, apply
        // the archival default modes (max compression lossless, sweet-spot lossy); after that every
        // encoder-mode choice is the user's
        var catalog = provider.GetRequiredService<EncoderCatalog>();
        catalog.EnsureRegistered(_config);
        if (!_appSettings.ArchivalDefaultsApplied)
        {
            catalog.ApplyArchivalDefaults(_config);
            _appSettings.ArchivalDefaultsApplied = true;
        }
        // one-time owner-default migration round 2: a settings file written before these defaults
        // existed carries the stale engine values (300 px covers, AR tags off). Shift once; after
        // that the user's own choices stick.
        if (!_appSettings.DefaultsV2Applied)
        {
            if (_config.maxAlbumArtSize == 300) _config.maxAlbumArtSize = 1000;
            _config.writeArTagsOnEncode = true;
            _appSettings.DefaultsV2Applied = true;
            log.Info("settings", $"defaults v2 applied: cover max {_config.maxAlbumArtSize}px, AR tags on encode on");
        }

        // apply the saved theme (mutates the palette brushes) before the window shows
        var theme = provider.GetRequiredService<ThemeService>();
        theme.Apply(theme.Current);

        // A broken system font-cache entry for a specific family makes DWrite throw FileNotFoundException
        // when WPF first lays out text in it, which aborts the whole window (the app then runs with no
        // visible window - looks like it "did nothing"). Probe each themed font up front; if one is
        // unusable on this machine, swap its app resource for a standard fallback that renders. Must run
        // before any window is built so StaticResource lookups pick up the replacement.
        System.Windows.Media.FontFamily FirstUsableFont(string key, params string[] fallbacks)
        {
            var candidates = new System.Collections.Generic.List<System.Windows.Media.FontFamily>();
            if (Resources[key] is System.Windows.Media.FontFamily cur) candidates.Add(cur);
            foreach (var f in fallbacks) candidates.Add(new System.Windows.Media.FontFamily(f));
            candidates.Add(new System.Windows.Media.FontFamily("Segoe UI"));   // last resort (proven usable)
            foreach (var fam in candidates)
            {
                try
                {
                    var tb = new System.Windows.Controls.TextBlock { Text = "AaGg0123 .-:", FontFamily = fam, FontSize = 18 };
                    tb.Measure(new Size(600, 600));
                    tb.Arrange(new Rect(tb.DesiredSize));
                    return fam;   // this family lays out on this machine
                }
                catch (Exception ex) { log.Warn("app", $"font '{fam.Source}' for '{key}' unusable: {ex.GetType().Name}"); }
            }
            return new System.Windows.Media.FontFamily("Segoe UI");
        }
        Resources["Serif"] = FirstUsableFont("Serif", "Georgia, serif", "Cambria");
        Resources["Mono"] = FirstUsableFont("Mono", "Consolas", "Courier New");
        log.Info("app", $"fonts: serif={(Resources["Serif"] as System.Windows.Media.FontFamily)?.Source} mono={(Resources["Mono"] as System.Windows.Media.FontFamily)?.Source}");

        // Even so, keep a fresh-window retry for any residual transient glitch: a half-initialised
        // window does not recover, so build a NEW one each attempt. Guard ShutdownMode so discarding a
        // failed window cannot exit the app.
        var mainVm = provider.GetRequiredService<MainViewModel>();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        bool shown = false;
        for (int attempt = 1; attempt <= 6 && !shown; attempt++)
        {
            MainWindow window = null;
            try
            {
                window = new MainWindow(theme, provider.GetRequiredService<AppStatusService>()) { DataContext = mainVm };
                window.Show();
                window.UpdateLayout();     // force the deferred layout so a font glitch throws here, caught
                window.Activate();
                shown = true;
                log.Info("app", $"window shown (attempt {attempt})");
            }
            catch (Exception ex)
            {
                // include the exception so a font/layout failure on a user's machine is diagnosable
                log.Error("app", $"window show attempt {attempt} failed - fresh retry", ex);
                try { window?.Close(); } catch { }
                System.Threading.Thread.Sleep(250);
            }
        }
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        if (!shown)
        {
            // no window will ever close, so nothing would trigger shutdown - do not linger invisibly
            log.Error("app", "window did not show after retries - exiting", null);
            Shutdown(1);
        }
    }
}
