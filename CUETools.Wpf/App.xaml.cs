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
        services.AddSingleton<IReportStore, ReportStore>();

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

        var window = provider.GetRequiredService<MainWindow>();
        window.DataContext = provider.GetRequiredService<MainViewModel>();
        window.Show();
    }
}
