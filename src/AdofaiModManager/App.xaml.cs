using System.IO;
using System.Windows;
using AdofaiModManager.Services;

namespace AdofaiModManager;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, args) => HandleException(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            HandleException(args.ExceptionObject as Exception);
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
