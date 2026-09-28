using OkEventApp.Models;

namespace OkEventApp.Services;

public class EventService
{
    private readonly DatabaseService _databaseService;
    private readonly ApiService _apiService;
    private readonly LoggingService _logger;

    public EventService(DatabaseService databaseService, ApiService apiService, LoggingService logger)
    {
        _databaseService = databaseService;
        _apiService = apiService;
        _logger = logger;
    }

    public async Task<List<EventItem>> GetEventsAsync()
    {
        // 1. 尝试从网络 API 获取最新日程
        var remoteEvents = await _apiService.FetchRemoteEventsAsync();

        if (remoteEvents != null && remoteEvents.Count > 0)
        {
            // 保存网络最新数据到本地 SQLite 缓存（签到状态在 SaveEventsAsync 内部被保留）
            await _databaseService.SaveEventsAsync(remoteEvents);
            return remoteEvents;
        }

        // 2. 如果网络请求失败或断网，降级读取本地数据库
        var cached = await _databaseService.GetEventsAsync();
        await _logger.WarnAsync($"网络数据不可用，降级使用本地缓存 {cached.Count} 条");
        return cached;
    }

    /// <summary>
    /// 只读本地缓存，不触网。
    /// 用于「从扫码页/详情页返回主页」时校准签到状态，避免为了一个状态字段拉一次网络。
    /// </summary>
    public Task<List<EventItem>> GetCachedEventsAsync() => _databaseService.GetEventsAsync();

    public async Task UpdateEventStatusAsync(EventItem item)
    {
        await _databaseService.UpdateEventAsync(item);
    }
}