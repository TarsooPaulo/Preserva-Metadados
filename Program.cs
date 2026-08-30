using System;
using Avalonia;
using Avalonia.ReactiveUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PreservaMetadados.Services;
using PreservaMetadados.ViewModels;

namespace PreservaMetadados;

class Program
{
    // Avalonia configuration, don't remove; also used by visual designer.
    public static IHost? AppHost { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs in
    // this method: prefer the manual Steps to reproduce issue.
    public static void Main(string[] args)
    {
        AppHost = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Register services
                services.AddSingleton<FileMetadataService>();
                services.AddSingleton<FileTransferService>();
                services.AddSingleton<MtpDeviceService>();
                services.AddSingleton<WifiTransferService>();
                services.AddSingleton<LogService>();
                
                // Register ViewModels
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<SettingsViewModel>();
            })
            .Build();

        try
        {
            // Start the host
            AppHost.Start();
            
            // Run Avalonia application
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
            throw;
        }
        finally
        {
            AppHost?.StopAsync().Wait();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}