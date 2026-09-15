using System.Windows;
using System.Windows.Threading;

namespace Inklume.Desktop.Tests;

internal static class WpfTestRunner
{
    private static readonly object SyncLock = new();
    private static readonly Lazy<Thread> StaThread = new(() =>
    {
        var threadReady = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var uiThemeDict = new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark };
            var uiControlDict = new Wpf.Ui.Markup.ControlsDictionary();
            app.Resources.MergedDictionaries.Add(uiThemeDict);
            app.Resources.MergedDictionaries.Add(uiControlDict);
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Inklume.Desktop;component/Themes/Graphite/Colors.xaml")
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Inklume.Desktop;component/Themes/Brushes.xaml")
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Inklume.Desktop;component/Themes/Controls.xaml")
            });

            Dispatcher = Dispatcher.CurrentDispatcher;
            threadReady.Set();
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        threadReady.Wait();
        return thread;
    });

    public static Dispatcher Dispatcher { get; private set; } = null!;

    public static void Run(Action action)
    {
        lock (SyncLock)
        {
            _ = StaThread.Value;
            Dispatcher.Invoke(action);
        }
    }
}
