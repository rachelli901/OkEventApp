using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OkEventApp.Services;

namespace OkEventApp.ViewModels;

/// <summary>
/// 扫码核销。相机事件在页面 code-behind 里转发成本ViewModel的命令，
/// 去重、协议解析、核销、提示全部收敛在这里，页面不再持有业务状态。
/// </summary>
public partial class ScanViewModel : ObservableObject
{
    private readonly EventService _eventService;
    private readonly LoggingService _logger;

    // 相机回调是高频事件（每帧都触发），用信号量保证同一时刻只处理一张码
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ScanViewModel(EventService eventService, LoggingService logger)
    {
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>是否正在处理某张二维码。为 true 时相机暂停取景，避免连续弹窗。</summary>
    [ObservableProperty]
    public partial bool IsProcessing { get; set; }

    [RelayCommand]
    private async Task ProcessScanAsync(string scannedResult)
    {
        if (string.IsNullOrWhiteSpace(scannedResult)) return;

        if (!await _gate.WaitAsync(0))
        {
            return; // 上一张还没处理完，丢弃这一帧
        }

        IsProcessing = true;

        try
        {
            if (!CheckInCode.TryParse(scannedResult, out var payload))
            {
                await Shell.Current.DisplayAlertAsync("无效二维码", "这不是 OkEvent 的参会凭证二维码。", "确定");
                return;
            }

            var events = await _eventService.GetEventsAsync();
            var targetEvent = events.FirstOrDefault(e => e.Id == payload.EventId);

            if (targetEvent == null)
            {
                await Shell.Current.DisplayAlertAsync(
                    "无效二维码",
                    $"未找到议程 ID 为 {payload.EventId} 的日程，请确认该凭证是否属于当前会议。",
                    "确定");
                return;
            }

            if (targetEvent.IsCheckedIn)
            {
                await Shell.Current.DisplayAlertAsync(
                    "重复核销",
                    $"「{targetEvent.Title}」已经签到过了，无需重复核销。",
                    "确定");
                await Shell.Current.GoToAsync("..");
                return;
            }

            targetEvent.IsCheckedIn = true;
            await _eventService.UpdateEventStatusAsync(targetEvent);

            await Shell.Current.DisplayAlertAsync("扫码成功", $"已成功核销签到：\n{targetEvent.Title}", "确定");
            await Shell.Current.GoToAsync(".."); // 返回上一页
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"扫码核销失败，原始内容：{scannedResult}", ex);
            await Shell.Current.DisplayAlertAsync("核销失败", "签到过程中出现异常，请稍后重试。", "确定");
        }
        finally
        {
            IsProcessing = false;
            _gate.Release();
        }
    }
}
