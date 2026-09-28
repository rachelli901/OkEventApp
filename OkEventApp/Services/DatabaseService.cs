using SQLite;
using OkEventApp.Models;

namespace OkEventApp.Services;

public class DatabaseService
{
    // 真正的赋值发生在首次 InitAsync，这里用 null! 跳过编译器的"字段必须初始化"检查
    private SQLiteAsyncConnection _database = null!;
    private readonly LoggingService _logger;

    public DatabaseService(LoggingService logger)
    {
        _logger = logger;
    }

    private async Task InitAsync()
    {
        if (_database != null) return;

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "okevent.db3");
        _database = new SQLiteAsyncConnection(dbPath);

        await _database.CreateTableAsync<EventItem>();
    }

    public async Task<List<EventItem>> GetEventsAsync()
    {
        await InitAsync();
        return await _database.Table<EventItem>().ToListAsync();
    }

    /// <summary>按 ID 读取单条记录，供核销后回写状态使用。</summary>
    public async Task<EventItem?> FindByIdAsync(string id)
    {
        await InitAsync();
        return await _database.Table<EventItem>().Where(e => e.Id == id).FirstOrDefaultAsync();
    }

    // 将 InsertAsync 改为 InsertOrReplaceAsync（主键存在则覆盖更新，不存在则插入）
    public async Task SaveEventsAsync(IEnumerable<EventItem> events)
    {
        await InitAsync();
        foreach (var item in events)
        {
            // InsertOrReplace 是整体覆盖，必须先把本地已落库的签到状态捞回来，
            // 否则每次网络刷新都会把用户刚签到的记录重置成「未签到」。
            var existing = await _database.Table<EventItem>()
                                          .Where(e => e.Id == item.Id)
                                          .FirstOrDefaultAsync();

            if (existing != null)
            {
                item.IsCheckedIn = existing.IsCheckedIn;
                item.IsBookmarked = existing.IsBookmarked;
            }

            await _database.InsertOrReplaceAsync(item);
        }
    }

    public async Task UpdateEventAsync(EventItem item)
    {
        await InitAsync();
        await _database.UpdateAsync(item);
        await _logger.InfoAsync($"签到状态更新：{item.Id}（{item.Title}）IsCheckedIn={item.IsCheckedIn}");
    }
}