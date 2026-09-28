using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OkEventApp.Models;
using OkEventApp.Services;

namespace OkEventApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly EventService _eventService;
    private readonly LoggingService _logger;
    private List<EventItem> _allEvents = new(); // 保存原始完整列表

    // 绑定给 UI 的列表数据，支持动态更新通知
    public ObservableCollection<EventItem> Events { get; } = new();

    // 使用 partial property 语法，解决 WinRT AOT 兼容性警告
    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public MainViewModel(EventService eventService, LoggingService logger)
    {
        _eventService = eventService;
        _logger = logger;
    }

    [RelayCommand]
    public async Task LoadEventsAsync()
    {
        IsRefreshing = true;
        try
        {
            Events.Clear();
            _allEvents = await _eventService.GetEventsAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("加载日程失败", ex);
            await Shell.Current.DisplayAlertAsync("加载失败", "日程加载出现异常，请稍后重试。", "确定");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    // 当 SearchText 发生变化时由源生成器自动调用的钩子函数
    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// 以本地缓存为准校准签到 / 收藏状态。
    /// </summary>
    /// <remarks>
    /// 扫码核销发生在另一个 ViewModel 实例里，内存中的对象与列表里的不是同一引用，
    /// 返回主页时这两边会不一致。这里只回填有差异的字段：
    /// 因为 <see cref="EventItem"/> 自带 INPC，列表上绑定的状态标签会就地刷新，
    /// 既不用整体重载，用户的滚动位置和搜索条件也不会丢。
    /// </remarks>
    public async Task SyncCheckInStateAsync()
    {
        var cached = await _eventService.GetCachedEventsAsync();
        var changed = false;

        foreach (var cachedItem in cached)
        {
            var current = _allEvents.FirstOrDefault(e => e.Id == cachedItem.Id);
            if (current == null) continue;

            if (current.IsCheckedIn != cachedItem.IsCheckedIn ||
                current.IsBookmarked != cachedItem.IsBookmarked)
            {
                current.IsCheckedIn = cachedItem.IsCheckedIn;
                current.IsBookmarked = cachedItem.IsBookmarked;
                changed = true;
            }
        }

        if (changed)
        {
            ApplyFilter();
        }
    }

    // 根据关键字过滤议程列表
    private void ApplyFilter()
    {
        Events.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allEvents
            : _allEvents.Where(e => e.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                    e.Speaker.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                    e.Location.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var item in filtered)
        {
            Events.Add(item);
        }
    }

    [RelayCommand]
    public async Task GoToDetailAsync(EventItem item)
    {
        if (item == null) return;

        // 使用 Shell 路由跳转并传递选中的议程数据
        await Shell.Current.GoToAsync(nameof(Views.EventDetailPage), new Dictionary<string, object>
        {
            { "Event", item }
        });
    }

    [RelayCommand]
    public async Task GoToScanAsync()
    {
        await Shell.Current.GoToAsync(nameof(Views.ScanPage));
    }
}