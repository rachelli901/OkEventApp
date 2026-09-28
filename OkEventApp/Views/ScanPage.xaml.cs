using Microsoft.Maui.ApplicationModel;
using OkEventApp.ViewModels;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace OkEventApp.Views;

public partial class ScanPage : ContentPage
{
    private readonly ScanViewModel _viewModel;

    public ScanPage(ScanViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 只在进入时「查状态」。不在 OnAppearing 里直接 RequestAsync ——
        // Android 上此时 Activity 还没完全恢复，会静默返回 Unknown 且不弹系统框。
        if (!IsMobilePlatform())
        {
            RequestPermissionButton.IsVisible = false;
            return;
        }

        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        RequestPermissionButton.IsVisible = status != PermissionStatus.Granted;
    }

    /// <summary>真正弹出系统授权框的入口，由按钮点击触发（而非页面生命周期）。</summary>
    private async void OnRequestPermissionClicked(object? sender, EventArgs e)
    {
        if (!IsMobilePlatform()) return;

        var status = await Permissions.RequestAsync<Permissions.Camera>();

        if (status != PermissionStatus.Granted)
        {
            RequestPermissionButton.IsVisible = true;
            await Shell.Current.DisplayAlertAsync(
                "无法启动相机",
                "扫码签到需要相机权限。若要重新授权，请到系统设置中开启后再次进入本页，或到议程详情页手动签到。",
                "确定");
            return;
        }

        RequestPermissionButton.IsVisible = false;
    }

    private static bool IsMobilePlatform() =>
        OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst();

    private void CameraReader_Loaded(object? sender, EventArgs e)
    {
        if (sender is CameraBarcodeReaderView cameraView)
        {
            cameraView.Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormat.QrCode,
                AutoRotate = true
            };
        }
    }

    private void CameraReader_BarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var firstResult = e.Results?.FirstOrDefault();
        if (firstResult == null) return;

        // 只做「相机事件 → ViewModel 命令」的转发，去重与核销逻辑都在 ScanViewModel 里
        MainThread.BeginInvokeOnMainThread(() =>
            _viewModel.ProcessScanCommand.Execute(firstResult.Value));
    }
}
