using Shared;
using System.Windows;
using System.Windows.Input;
using System;
using MouseAction = Shared.MouseAction;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Wpf.Ui.Controls;

namespace Client;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += MainWindow_DataContextChanged;
    }

    private DateTime _lastMouseMoveSent = DateTime.MinValue;
    private (double x, double y) _lastNorm = (-1, -1);
    private const double MinMoveIntervalMs = 16.0; // ~60 Hz
    private DateTime _lastMouseMoveLogged = DateTime.MinValue;
    private const double MinMoveLogIntervalMs = 200.0; // reduce log spam

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
        // Disabled: global mouse hook handles capture/publish to avoid duplicates
        return;
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        return;
    }

    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        return;
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        return;
    }

    private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ClientViewModel oldVm)
        {
            try
            {
                oldVm.Events.CollectionChanged -= Events_CollectionChanged;
            }
            catch { }
        }
        if (e.NewValue is ClientViewModel newVm)
        {
            newVm.Events.CollectionChanged += Events_CollectionChanged;
        }
    }

    private void Events_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems != null && e.NewItems.Count > 0)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var last = e.NewItems[e.NewItems.Count - 1];
                    EventsList?.ScrollIntoView(last);
                }
                catch { }
            }));
        }
    }


}
