using System.Windows;
using System.Windows.Threading;

namespace ChatHub;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // 兜底：未处理异常一律提示，避免应用静默退出。
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                ReportFatal($"发生未处理的错误：\n{ex.Message}");
            }
        };

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            ReportFatal($"启动失败：\n{ex.Message}");
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ReportFatal($"操作失败：\n{e.Exception.Message}");
    }

    /// <summary>致命错误：弹框提示，不静默退出。</summary>
    private static void ReportFatal(string message) =>
        MessageBox.Show(message, "ChatHub", MessageBoxButton.OK, MessageBoxImage.Error);
}
