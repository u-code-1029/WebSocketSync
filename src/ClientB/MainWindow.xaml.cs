using Shared;
using System.Windows;
using System.Windows.Input;
using System;
using MouseAction = Shared.MouseAction;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Client;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private DateTime _lastMouseMoveSent = DateTime.MinValue;
    private (double x, double y) _lastNorm = (-1, -1);
    private const double MinMoveIntervalMs = 16.0; // ~60 Hz

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            dynamic? vm = DataContext;
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            var result = dlg.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                vm.SyncDirectory = dlg.SelectedPath;
            }
        }
        catch { }
    }

    private static ClientWorker Worker => App.HostInstance!.Services.GetService(typeof(ClientWorker)) as ClientWorker ?? throw new InvalidOperationException();

    private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not ClientViewModel vm || !vm.IsController) return;
        var p = PointToScreen(e.GetPosition(this));
        var vx = SystemParameters.VirtualScreenLeft;
        var vy = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        var nx = Math.Clamp((p.X - vx) / vw, 0, 1);
        var ny = Math.Clamp((p.Y - vy) / vh, 0, 1);

        var now = DateTime.UtcNow;
        if ((now - _lastMouseMoveSent).TotalMilliseconds < MinMoveIntervalMs)
            return;
        if (Math.Abs(nx - _lastNorm.x) < 0.002 && Math.Abs(ny - _lastNorm.y) < 0.002)
            return;
        _lastMouseMoveSent = now;
        _lastNorm = (nx, ny);
        var msg = new MouseEventMessage(Client.ClientWorker.Settings.ClientId ?? Environment.MachineName, MouseAction.Move, nx, ny, 0);
        _ = Worker.PublishAsync(new Shared.Envelope(MessageType.MouseEvent, msg));
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ClientViewModel vm || !vm.IsController) return;
        var p = PointToScreen(e.GetPosition(this));
        var vx = SystemParameters.VirtualScreenLeft;
        var vy = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        var nx = Math.Clamp((p.X - vx) / vw, 0, 1);
        var ny = Math.Clamp((p.Y - vy) / vh, 0, 1);
        var action = e.ChangedButton == MouseButton.Left ? MouseAction.LeftDown : MouseAction.RightDown;
        var msg = new MouseEventMessage(Client.ClientWorker.Settings.ClientId ?? Environment.MachineName, action, nx, ny, 0);
        _ = Worker.PublishAsync(new Shared.Envelope(MessageType.MouseEvent, msg));
    }

    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ClientViewModel vm || !vm.IsController) return;
        var p = PointToScreen(e.GetPosition(this));
        var vx = SystemParameters.VirtualScreenLeft;
        var vy = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        var nx = Math.Clamp((p.X - vx) / vw, 0, 1);
        var ny = Math.Clamp((p.Y - vy) / vh, 0, 1);
        var action = e.ChangedButton == MouseButton.Left ? MouseAction.LeftUp : MouseAction.RightUp;
        var msg = new MouseEventMessage(Client.ClientWorker.Settings.ClientId ?? Environment.MachineName, action, nx, ny, 0);
        _ = Worker.PublishAsync(new Shared.Envelope(MessageType.MouseEvent, msg));
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not ClientViewModel vm || !vm.IsController) return;
        var p = PointToScreen(e.GetPosition(this));
        var vx = SystemParameters.VirtualScreenLeft;
        var vy = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        var nx = Math.Clamp((p.X - vx) / vw, 0, 1);
        var ny = Math.Clamp((p.Y - vy) / vh, 0, 1);
        var msg = new MouseEventMessage(Client.ClientWorker.Settings.ClientId ?? Environment.MachineName, MouseAction.Wheel, nx, ny, e.Delta);
        _ = Worker.PublishAsync(new Shared.Envelope(MessageType.MouseEvent, msg));
    }
}
