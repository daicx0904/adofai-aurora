using System.IO;
using System.Windows;
using System.Windows.Threading;
using AdofaiModManager.Services;

namespace AdofaiModManager;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            HandleException(args.ExceptionObject as Exception);

        // 没人 await 的后台任务（_ = SomethingAsync()）里的异常：记下来，别让它悄悄丢掉
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            HandleException(args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppServices.Initialize();

        // 诊断日志从这一版起默认开启：每次启动写一行环境摘要（版本/系统/安装形态），
        // 用户反馈问题时直接导出日志即可，不用再教他开环境变量。
        AppPaths.StartSession(AppUpdateService.DescribeEnvironment());

        var theme = AppServices.Settings.Settings.Theme;

        // 先按主题铺好资源（向导也要用）
        ThemeService.Apply(theme);

        // 首次使用：先走一遍向导（每步都可跳过）
        if (!AppServices.Settings.Settings.HasCompletedSetup)
        {
            // 向导是此时唯一的窗口，先改成"显式退出"，
            // 否则它一关闭就会触发"最后一个窗口关闭 → 退出程序"
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var wizard = new Views.Dialogs.FirstRunWizard();
            wizard.ShowDialog();

            AppServices.Settings.Settings.HasCompletedSetup = true;
            AppServices.Settings.Save();
        }

        var window = new MainWindow();
        MainWindow = window;

        // 应用主题（并在「跟随系统」时监听系统主题变化）
        ThemeService.Apply(theme, window);

        window.Show();

        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    /// <summary>
    /// UI 线程上的未处理异常（多数来自按钮的 async void 事件里漏接的异常）。
    /// 原来只记日志、不标记已处理 → 程序直接闪退，用户点个按钮就丢了所有界面状态。
    /// 现在：记日志 + 弹窗说明，然后继续运行。
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        HandleException(args.Exception);

        // 窗口都还没起来（启动阶段就出错）时没法安全地继续，照旧退出
        if (MainWindow is null || !MainWindow.IsLoaded)
        {
            return;
        }

        args.Handled = true;

        try
        {
            MessageBox.Show(
                MainWindow,
                $"操作时发生了意外错误：{args.Exception.Message}\n\n"
                + $"详细信息已记录到日志（{AppPaths.CrashLogPath}），"
                + "可在「设置 → 诊断与反馈 → 导出诊断包」里导出后反馈。\n\n"
                + "如果界面变得不正常，建议重启一次 Aurora。",
                "Aurora",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // 弹窗失败也别再抛
        }
    }

    private static void HandleException(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        // 带环境摘要（版本 / 系统 / 安装形态）写进 crash.log，并按 1 MB 轮转
        AppPaths.AppendCrashLog(AppUpdateService.DescribeEnvironment(), exception);
    }
}
