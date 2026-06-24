using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace FindNeedleUX;

/// <summary>
/// Custom entry point (replaces the XAML-generated Main; see DisableXamlGeneratedMain in the csproj).
/// Implements Windows App SDK single-instancing: the first launch becomes the "owner"; later launches
/// (e.g. opening a second file via "Open with → Find Needle") redirect their activation to the owner
/// and exit, so the running window is reused instead of spawning a new instance. This is the standard
/// AppLifecycle redirection pattern — RedirectActivationToAsync must be awaited off the STA Main thread.
/// </summary>
public static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (!DecideRedirection())
        {
            Microsoft.UI.Xaml.Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
        }
        return 0;
    }

    private static bool DecideRedirection()
    {
        bool isRedirect = false;
        try
        {
            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            var keyInstance = AppInstance.FindOrRegisterForKey("findneedle-main");

            if (keyInstance.IsCurrent)
            {
                // We are the owner: handle future redirected activations (2nd "Open with", etc.).
                keyInstance.Activated += OnActivated;
            }
            else
            {
                // Someone else owns the app — hand our activation (the file being opened) to them.
                isRedirect = true;
                RedirectActivationTo(activatedArgs, keyInstance);
            }
        }
        catch
        {
            // If single-instancing fails for any reason, fall back to launching normally rather than
            // refusing to start. Worst case is a second window instead of reuse.
            isRedirect = false;
        }
        return isRedirect;
    }

    private static void OnActivated(object sender, AppActivationArguments args)
    {
        if (Microsoft.UI.Xaml.Application.Current is App app)
            app.HandleActivation(args);
    }

    // RedirectActivationToAsync is async, but Main is STA — pump COM while we wait on a kernel event.
    private static IntPtr _redirectEventHandle = IntPtr.Zero;

    private static void RedirectActivationTo(AppActivationArguments args, AppInstance keyInstance)
    {
        _redirectEventHandle = CreateEvent(IntPtr.Zero, true, false, null);
        Task.Run(() =>
        {
            keyInstance.RedirectActivationToAsync(args).AsTask().Wait();
            SetEvent(_redirectEventHandle);
        });

        const uint CWMO_DEFAULT = 0;
        const uint INFINITE = 0xFFFFFFFF;
        _ = CoWaitForMultipleObjects(
            CWMO_DEFAULT, INFINITE, 1,
            new[] { _redirectEventHandle }, out _);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

    [DllImport("kernel32.dll")]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, ulong nHandles, IntPtr[] pHandles, out uint dwIndex);
}
