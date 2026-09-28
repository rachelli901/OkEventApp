using System.Net.Http.Json;
using OkEventApp.Models;

namespace OkEventApp.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;
    private readonly LoggingService _logger;

    public ApiService(LoggingService logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    // 从真实/测试在线接口拉取日程列表
    public async Task<List<EventItem>> FetchRemoteEventsAsync()
    {
        // 这里使用 JSONPlaceholder 或模拟接口
        // 在实际项目中替换为公司后端的 API URL（如：https://api.okwhen.com/v1/events）
        // 提到 try 外面，两个 catch 分支都要在日志里带上它
        var url = "https://jsonplaceholder.typicode.com/posts?_limit=5";

        try
        {
            var posts = await _httpClient.GetFromJsonAsync<List<ApiPostModel>>(url);

            if (posts == null) return new List<EventItem>();

            // 将 API 返回的 DTO 转换为本地的 EventItem Model
            return posts.Select((p, index) => new EventItem
            {
                Id = p.Id.ToString(),
                Title = p.Title,
                Speaker = $"讲师 {index + 1}",
                Location = $"线上分会场 {index + 1}",
                Category = index % 2 == 0 ? "Backend" : "Cloud",
                StartTime = DateTime.Now.AddDays(index)
            }).ToList();
        }
        catch (TaskCanceledException) when (!_httpClient.Timeout.Equals(TimeSpan.Zero))
        {
            // 10s 超时属于可预期降级路径，不算异常
            await _logger.WarnAsync($"网络请求超时（{_httpClient.Timeout.TotalSeconds}s）：{url}");
            return new List<EventItem>();
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"网络请求异常：{url}", ex);
            return new List<EventItem>();
        }
    }
}

// 对应网络接口返回数据的 DTO 类
public class ApiPostModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}