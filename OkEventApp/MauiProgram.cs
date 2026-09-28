using Microsoft.Extensions.Logging;
using OkEventApp.Services;
using OkEventApp.ViewModels;
using OkEventApp.Views;

namespace OkEventApp
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // 1. 注册基础服务（Singleton 全局单例）
            builder.Services.AddSingleton<LoggingService>();
            builder.Services.AddSingleton<DatabaseService>();
            builder.Services.AddSingleton<ApiService>();

            // 2. 注册业务服务
            builder.Services.AddSingleton<EventService>();

            // 3. 注册主页（MainPage）与其 ViewModel（Transient 每次使用新建）
            //    注意：MainPage 不是靠路由创建的，而是 AppShell 的 ShellContent.ContentTemplate。
            //    但 DataTemplate 创建页面时仍然从容器解析构造函数参数，
            //    所以这一行不能删 —— 删了首页会启动即崩。
            builder.Services.AddTransient<MainViewModel>();
            builder.Services.AddTransient<MainPage>();

            // 4. 注册详情页（EventDetailPage）与其 ViewModel
            builder.Services.AddTransient<EventDetailViewModel>();
            builder.Services.AddTransient<EventDetailPage>();

            // 5. 注册扫码页（ScanPage）与其 ViewModel
            // 缺了这一行，Shell 路由跳转时容器解析不出页面类型，扫码页根本打不开
            builder.Services.AddTransient<ScanViewModel>();
            builder.Services.AddTransient<ScanPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}