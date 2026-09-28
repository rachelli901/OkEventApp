using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OkEventApp.Models;
using OkEventApp.Services;
using QRCoder;

namespace OkEventApp.ViewModels;

[QueryProperty(nameof(Item), "Event")]
public partial class EventDetailViewModel : ObservableObject
{
    private readonly EventService _eventService;

    // 用 partial property（而不是字段）声明，规避 MVVMTK0045：
    // 字段式生成的代码在 WinRT 场景下不 AOT 兼容
    [ObservableProperty]
    public partial EventItem? Item { get; set; }

    [ObservableProperty]
    public partial ImageSource? QrCodeImage { get; set; }

    public EventDetailViewModel(EventService eventService)
    {
        _eventService = eventService;
    }

    private EventItem? _subscribedItem;

    partial void OnItemChanged(EventItem? value)
    {
        // 换页时先解绑旧对象，避免详情页已销毁还收到模型的属性通知
        if (_subscribedItem != null)
        {
            _subscribedItem.PropertyChanged -= OnItemPropertyChanged;
        }

        _subscribedItem = value;

        if (value != null)
        {
            // 模型的签到状态一变，下面这些派生属性要跟着重算
            value.PropertyChanged += OnItemPropertyChanged;

            // 二维码内容走 CheckInCode 契约，与 ScanViewModel 的解析规则同源
            GenerateQRCode(value);
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanCheckIn));
        OnPropertyChanged(nameof(CheckInButtonText));
    }

    /// <summary>是否已签到。决定签到按钮是否可点。</summary>
    public bool CanCheckIn => Item is { IsCheckedIn: false };

    /// <summary>签到按钮文案。已签到时改为「已完成签到」，避免用户重复点击。</summary>
    public string CheckInButtonText => Item is { IsCheckedIn: true } ? "本次议程已完成签到" : "签到并加入我的日程";

    private void GenerateQRCode(EventItem item)
    {
        // 格式化的参会凭证文本：手机扫出来能看懂，ScanViewModel 也能精确解析出议程 ID
        var checkInText = CheckInCode.Build(item.Id, item.Title, item.Speaker);

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(checkInText, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        byte[] qrCodeAsPngByteGraphic = qrCode.GetGraphic(20);

        QrCodeImage = ImageSource.FromStream(() => new MemoryStream(qrCodeAsPngByteGraphic));
    }

    [RelayCommand]
    private async Task CheckInAsync()
    {
        if (Item == null) return;

        if (Item.IsCheckedIn)
        {
            if (Shell.Current != null)
            {
                await Shell.Current.DisplayAlertAsync("已签到", $"「{Item.Title}」此前已完成签到，无需重复操作。", "确定");
            }
            return;
        }

        Item.IsCheckedIn = true;
        await _eventService.UpdateEventStatusAsync(Item);

        // 无需再手动 OnPropertyChanged：EventItem 自带 INPC，
        // Item.IsCheckedIn 的变更会顺着绑定路径通知到页面上的所有绑定。

        if (Shell.Current != null)
        {
            await Shell.Current.DisplayAlertAsync("签到成功", $"您已成功签到：{Item.Title}", "确定");
        }
    }
}