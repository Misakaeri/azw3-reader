using System.Windows;

namespace Azw3Reader.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 先显示启动画面，让用户立即看到反馈
        var splash = new SplashWindow();
        splash.Show();

        // 创建并显示主窗口
        MainWindow = new MainWindow();
        ((MainWindow)MainWindow).Loaded += (_, _) =>
        {
            splash.Close();
        };
        MainWindow.Show();
    }
}
