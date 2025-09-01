using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Windows;
using Application = System.Windows.Application;

namespace Client;

public partial class App : Application
{
    public static IHost? HostInstance { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        HostInstance = Host.CreateDefaultBuilder()
            .UseSerilog((ctx, cfg) => cfg
                .ReadFrom.Configuration(ctx.Configuration)
                .WriteTo.Console()
                .WriteTo.File("logs/client-.log", rollingInterval: RollingInterval.Day))
            .ConfigureServices((ctx, services) =>
            {
                services.AddSingleton<ClientViewModel>();
                services.Configure<ClientSettings>(ctx.Configuration.GetSection("Client"));
                services.AddSingleton<ClientWorker>();
                services.AddSingleton<FileSyncManager>();
                services.AddHostedService<HostedClientService>();
            })
            .Build();

        HostInstance.Start();

        // Set DataContext via DI
        var window = new MainWindow
        {
            DataContext = HostInstance.Services.GetRequiredService<ClientViewModel>()
        };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        HostInstance?.Dispose();
        base.OnExit(e);
    }
}

public class HostedClientService : BackgroundService
{
    private readonly ClientWorker _worker;
    public HostedClientService(ClientWorker worker) => _worker = worker;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => _worker.StartAsync(stoppingToken);
}
