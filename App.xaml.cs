using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Reflection;

namespace SnapForge;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = "SnapForge.SingleInstance.Mutex";
    private const string ActivateEventName = "SnapForge.SingleInstance.Activate";
    private static Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _activateWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        bool createdNew;
        _instanceMutex = new Mutex(true, InstanceMutexName, out createdNew);
        if (!createdNew)
        {
            try
            {
                using EventWaitHandle existingEvent = EventWaitHandle.OpenExisting(ActivateEventName);
                existingEvent.Set();
            }
            catch
            {
                // ignore if first instance is still initializing
            }

            Shutdown();
            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => Dispatcher.Invoke(BringMainWindowToFront),
            null,
            Timeout.Infinite,
            false);

        base.OnStartup(e);
        DisableStylusStack();
        MainWindow window = new();
        MainWindow = window;
        window.WindowState = WindowState.Minimized;
        window.Show();
        window.Hide();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        _activateWait?.Unregister(null);
        _activateEvent?.Dispose();
        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private void BringMainWindowToFront()
    {
        if (MainWindow is null)
        {
            return;
        }

        if (MainWindow is SnapForge.MainWindow panelWindow)
        {
            panelWindow.ShowFromExternalActivation();
        }
        else
        {
            if (!MainWindow.IsVisible)
            {
                MainWindow.Show();
            }

            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }
        MainWindow.Topmost = true;
        MainWindow.Topmost = false;
        MainWindow.Focus();
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        string details = e.Exception?.Message ?? "Unknown error";
        string stack = e.Exception?.StackTrace ?? string.Empty;
        if (stack.Length > 700)
        {
            stack = stack[..700];
        }

        System.Windows.MessageBox.Show(
            $"Unexpected error: {details}\n\n{stack}",
            "SnapForge",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static void DisableStylusStack()
    {
        try
        {
            _ = Tablet.TabletDevices.Count;
            object? stylusLogic = typeof(InputManager).InvokeMember("StylusLogic",
                BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                InputManager.Current,
                null);
            if (stylusLogic is null)
            {
                return;
            }

            MethodInfo? onTabletRemoved = stylusLogic.GetType().GetMethod("OnTabletRemoved", BindingFlags.Instance | BindingFlags.NonPublic);
            if (onTabletRemoved is null)
            {
                return;
            }

            while (Tablet.TabletDevices.Count > 0)
            {
                onTabletRemoved.Invoke(stylusLogic, [0u]);
            }
        }
        catch
        {
            // keep default behavior if reflection path fails
        }
    }
}

