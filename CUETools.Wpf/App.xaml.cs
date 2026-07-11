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
        // otherwise pick it and self-recurse. Force the parameterless ctor.
        services.AddSingleton<CUEConfig>(_ => new CUEConfig());
        services.AddSingleton<IDriveService, DriveService>();
        services.AddSingleton<IRipService, RipService>();
        services.AddSingleton<IVerifyService, VerifyService>();
        services.AddSingleton<IConvertService, ConvertService>();
        services.AddSingleton<IReportStore, ReportStore>();
        services.AddSingleton<IHistoryStore, HistoryStore>();
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

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        var provider = services.BuildServiceProvider();

        // apply the saved theme (mutates the palette brushes) before the window shows
        var theme = provider.GetRequiredService<ThemeService>();
        theme.Apply(theme.Current);

        var window = provider.GetRequiredService<MainWindow>();
        window.DataContext = provider.GetRequiredService<MainViewModel>();
        window.Show();
    }
}
