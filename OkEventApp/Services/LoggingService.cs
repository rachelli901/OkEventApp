using System.Text;

namespace OkEventApp.Services;

/// <summary>
/// 轻量本地日志。
/// </summary>
/// <remarks>
/// Release 包里 <c>Debug.WriteLine</c> 会被编译器丢弃，现场问题无从复现。
/// 这里把关键业务路径（网络降级、核销异常等）追加写入 AppData 下的日志文件。
/// </remarks>
public class LoggingService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _logFilePath;

    public LoggingService()
    {
        var directory = Path.Combine(FileSystem.AppDataDirectory, "logs");
        _logFilePath = Path.Combine(directory, "app.log");

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch
        {
            // 日志是旁路设施，目录创建失败不应该影响主流程
        }
    }

    /// <summary>当前日志文件路径，便于调试时在文件管理器里直接打开。</summary>
    public string LogFilePath => _logFilePath;

    public Task InfoAsync(string message) => WriteAsync("INFO", message, null);

    public Task WarnAsync(string message) => WriteAsync("WARN", message, null);

    public Task ErrorAsync(string message, Exception? exception = null) => WriteAsync("ERROR", message, exception);

    private async Task WriteAsync(string level, string message, Exception? exception)
    {
        var builder = new StringBuilder();
        builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
               .Append(" [").Append(level).Append("] ")
               .AppendLine(message);

        if (exception != null)
        {
            builder.AppendLine(exception.ToString());
        }

        try
        {
            await File.AppendAllTextAsync(_logFilePath, builder.ToString());
        }
        catch
        {
            // 写日志失败一律静默，绝不把旁路设施的错误抛回业务层
        }
    }
}
