namespace OkEventApp;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(nameof(Views.EventDetailPage), typeof(Views.EventDetailPage));
        Routing.RegisterRoute(nameof(Views.ScanPage), typeof(Views.ScanPage)); // 注册扫码页路由
    }
}