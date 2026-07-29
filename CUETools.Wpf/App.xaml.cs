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
    private IDiagnosticLog? _log;
    private AppLaunchOptions _launchOptions = new();

    internal static CUEConfig CreateWpfDefaultConfig()
    {
        var config = new CUEConfig
        {
            detectHDCD = true,
            decodeHDCD = false,
            decodeHDCDto24bit = false,
            maxAlbumArtSize = 1500,
            writeArTagsOnEncode = true,
            // Every engine embed/extract site is "embedAlbumArt || CopyAlbumArt".
            // Keep the WPF setting as the only gate.
            CopyAlbumArt = false,
        };
        config.advanced.CreateTOC = true;
        config.advanced.DetailedCTDBLog = true;
        config.advanced.coversSearch =
            CUEConfigAdvanced.CTDBCoversSearch.Extensive;
        return config;
    }

    internal static bool ApplyDefaultsV3(
        CUEConfig config,
        AppSettings settings)
    {
        if (settings.DefaultsV3Applied)
            return false;
        if (config.maxAlbumArtSize == 1000)
            config.maxAlbumArtSize = 1500;
        config.advanced.CreateTOC = true;
        config.advanced.DetailedCTDBLog = true;
        config.advanced.coversSearch =
            CUEConfigAdvanced.CTDBCoversSearch.Extensive;
        settings.DefaultsV3Applied = true;
        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // persist every user setting (engine config + app prefs) on the way out
        if (!_launchOptions.IsSecondaryDriveWindow &&
            _settingsStore != null && _config != null && _appSettings != null)
            _settingsStore.Save(_config, _appSettings);
        else if (_launchOptions.IsSecondaryDriveWindow)
            _log?.Info("settings",
                "secondary drive window closed; shared settings intentionally left unchanged");
        base.OnExit(e);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _launchOptions = AppLaunchOptions.Parse(e.Args);
        var services = new ServiceCollection();
        services.AddSingleton(_launchOptions);

        // Explicit factory: CUEConfig has a copy constructor too, and the DI container would
        // otherwise pick it and self-recurse. Force the parameterless ctor, then apply the app's
        // preferred defaults (until settings persistence lands).
        services.AddSingleton<CUEConfig>(_ => CreateWpfDefaultConfig());
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
        services.AddSingleton<PageViewModel, AdvancedViewModel>();
        services.AddSingleton<PageViewModel, ExploreViewModel>();

        services.AddSingleton<MainViewModel>();
        // MainWindow is created directly (with a fresh instance per show-retry), not resolved here.

        var provider = services.BuildServiceProvider();
        Services = provider;

        // Diagnostic log + global crash handlers first, so anything below is captured. The log is
        // privacy-safe (see DiagnosticLog): structure only, no album/artist/track names.
        var log = provider.GetRequiredService<IDiagnosticLog>();
        _log = log;
        log.Info("app", $"start clr={Environment.Version} os={Environment.OSVersion.Version} x64={Environment.Is64BitProcess} log={log.LogPath}");
        if (_launchOptions.IsSecondaryDriveWindow)
            log.Info("app",
                $"secondary drive window requested drive={_launchOptions.PreferredDrive}");
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

        // Heal a trackFilenameFormat polluted by an older build. The Naming page used to push the WPF
        // template into this ENGINE setting, but the engine speaks a different vocabulary (no
        // "%albumartist%") and this value is a per-track NAME, not a path. A stale value leaves a literal
        // token plus a folder nobody creates in the hidden-track filename, which aborts an encode - and
        // the Settings page shows the garbage. Rip and convert no longer read it (they pass explicit
        // names), so reset it whenever it is clearly not an engine per-track format.
        {
            string tf = _config.trackFilenameFormat ?? "";
            if (tf.Contains("%albumartist%") || tf.Contains("%featsuffix%") || tf.Contains("%releasedescriptor%")
                || tf.Contains("%disc%") || tf.Contains('/') || tf.Contains('\\'))
            {
                _config.trackFilenameFormat = "%tracknumber%. %title%";
                log.Info("settings", "reset a stale WPF-style trackFilenameFormat to the engine default");
            }
        }

        // Owner-default migration round 3. Move only the former app-owned cover
        // default; a non-1000 value is already a user choice. The three engine
        // advanced values previously defaulted off/Primary, so this one migration
        // deliberately adopts the new archival defaults. Choices made afterward stick.
        if (ApplyDefaultsV3(_config, _appSettings))
        {
            log.Info(
                "settings",
                $"defaults v3 applied: cover max {_config.maxAlbumArtSize}px, " +
                "TOC on, detailed CTDB on, artwork search Extensive");
        }

        // Sweep orphaned Test & Copy staging. Cleanup requires the exact owned marker and directory
        // shape, a reparse-free tree, age over one day, and acquisition of the process-held lease.
        // A lookalike name or forged old timestamp is never enough to authorize recursive deletion.
        try
        {
            var sweep = TestCopyStagingWorkspace.SweepOrphans(
                rootDirectory: null, TimeSpan.FromHours(24));
            if (sweep.Directories > 0)
                log.Info("rip",
                    $"swept {sweep.Directories} orphaned Test & Copy staging folder(s), " +
                    $"{sweep.Bytes / (1024 * 1024)} MB reclaimed");
        }
        catch (Exception ex) { log.Warn("rip", "staging sweep failed: " + ex.GetType().Name); }

        // apply the saved theme (mutates the palette brushes) before the window shows
        var theme = provider.GetRequiredService<ThemeService>();
        theme.Apply(theme.Current);

        // Publish the requested drive before page view-models are constructed. The Rip and
        // Drive & Read pages then initialize against the same hardware without a transient read
        // of the first enumerated drive.
        if (_launchOptions.PreferredDrive != '\0')
        {
            IDriveService drives = provider.GetRequiredService<IDriveService>();
            if (drives.GetDrives().Contains(_launchOptions.PreferredDrive))
                drives.SelectedDrive = _launchOptions.PreferredDrive;
            else
                log.Warn("app",
                    $"requested drive {_launchOptions.PreferredDrive}: is not attached");
        }

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
            MainWindow? window = null;
            try
            {
                window = new MainWindow(theme, provider.GetRequiredService<AppStatusService>())
                {
                    DataContext = mainVm,
                    Title = _launchOptions.IsSecondaryDriveWindow &&
                        _launchOptions.PreferredDrive != '\0'
                            ? $"CUETools 2026 - Drive {_launchOptions.PreferredDrive}:"
                            : "CUETools 2026"
                };
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
