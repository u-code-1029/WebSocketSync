using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Windows;
using Application = System.Windows.Application;
using Wpf.Ui.Appearance;

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
                services.AddSingleton<GlobalMouseHook>();
                services.AddSingleton<TrayIconManager>();
            })
            .Build();

        HostInstance.Start();

        // Apply system theme from WPF UI
        ApplicationThemeManager.Apply(ApplicationThemeManager.GetAppTheme());

        // Initialize tray + global hook; start hidden by default
        var tray = HostInstance.Services.GetRequiredService<TrayIconManager>();
        tray.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { HostInstance?.Services.GetService<TrayIconManager>()?.Dispose(); } catch { }
        try { HostInstance?.Services.GetService<GlobalMouseHook>()?.Dispose(); } catch { }
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

public sealed class TrayIconManager : IDisposable
{
    private readonly ClientViewModel _vm;
    private readonly ClientWorker _worker;
    private readonly GlobalMouseHook _mouse;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private MainWindow? _window;
    private DateTime _lastMoveSent = DateTime.MinValue;
    private (double x, double y) _lastNorm = (-1, -1);

    // Throttle parameters: small-move suppression; Hz is configurable via settings
    private double _moveHz = 30.0;
    private double _minMoveIntervalMs = 1000.0 / 30.0;
    private const double MinDelta = 0.002; // ignore very tiny mouse jitter

    public TrayIconManager(ClientViewModel vm, ClientWorker worker, GlobalMouseHook mouse)
    {
        _vm = vm;
        _worker = worker;
        _mouse = mouse;
    }

    public void Initialize()
    {
        // Load throttle from settings
        var hz = ClientWorker.Settings.MouseMoveHz ?? 30.0;
        if (hz <= 0) hz = 30.0;
        if (hz > 240.0) hz = 240.0; // cap to sane upper limit
        _moveHz = hz;
        _minMoveIntervalMs = 1000.0 / _moveHz;

        // Create tray icon
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Client Controller"
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var showItem = new System.Windows.Forms.ToolStripMenuItem("Show Window", null, (_, __) => ShowWindow());
        var requestItem = new System.Windows.Forms.ToolStripMenuItem("Request/Release Control", null, async (_, __) => await _vm.RequestControlCommand.ExecuteAsync(null));
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit", null, (_, __) => Application.Current.Shutdown());
        menu.Items.Add(showItem);
        menu.Items.Add(requestItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, __) => ShowWindow();

        // Install global mouse hook
        _mouse.OnMouse += HandleGlobalMouse;
        _mouse.Start();
    }

    private async void HandleGlobalMouse(Shared.MouseAction action, double nx, double ny, int delta)
    {
        // Only send when this client is controller
        if (!_vm.IsController) return;

        var now = DateTime.UtcNow;
        if (action == Shared.MouseAction.Move)
        {
            if ((now - _lastMoveSent).TotalMilliseconds < _minMoveIntervalMs) return; // throttle
            if (Math.Abs(nx - _lastNorm.x) < MinDelta && Math.Abs(ny - _lastNorm.y) < MinDelta) return; // suppress micro-moves
            _lastMoveSent = now;
            _lastNorm = (nx, ny);
        }

        var msg = new Shared.MouseEventMessage(ClientWorker.Settings.ClientId ?? Environment.MachineName, action, nx, ny, action == Shared.MouseAction.Wheel ? delta : 0);
        await _worker.PublishAsync(new Shared.Envelope(Shared.MessageType.MouseEvent, msg));

        // Log to UI
        var label = action == Shared.MouseAction.Wheel ? $"Mouse {action} (you) @ ({nx:F3},{ny:F3}) d={delta}" : $"Mouse {action} (you) @ ({nx:F3},{ny:F3})";
        _vm.AddEventCommand.Execute(label);
    }

    private void ShowWindow()
    {
        if (_window == null)
        {
            _window = new MainWindow { DataContext = _vm };
            _window.Closed += (_, __) => { _window = null; };
        }
        if (_window.IsVisible)
        {
            _window.Activate();
        }
        else
        {
            _window.Show();
            _window.Activate();
        }
    }

    public void Dispose()
    {
        try
        {
            _mouse.OnMouse -= HandleGlobalMouse;
            _mouse.Stop();
        }
        catch { }
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
