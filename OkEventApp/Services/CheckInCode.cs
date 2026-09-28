using System.Text.RegularExpressions;

namespace OkEventApp.Services;

/// <summary>
/// 参会凭证（二维码内容）的编解码契约。
/// </summary>
/// <remarks>
/// 生成端（<see cref="ViewModels.EventDetailViewModel"/>）与解析端（<see cref="ViewModels.ScanViewModel"/>）
/// 必须共用本类。此前两处各写一份字符串拼接/解析逻辑，协议一改就漂移，扫码必然核销失败。
/// </remarks>
public static class CheckInCode
{
    /// <summary>协议标识前缀，扫码时用它判断「这张码是不是 OkEvent 的凭证」。</summary>
    public const string Scheme = "OKEEVENT:CHECKIN";

    private const char Separator = '|';

    /// <summary>完整格式：<c>OKEEVENT:CHECKIN|&lt;议程ID&gt;|&lt;议程名称&gt;|&lt;讲师&gt;</c>。</summary>
    public static string Build(string eventId, string? title = null, string? speaker = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);

        return string.Join(Separator, Scheme, eventId, title ?? string.Empty, speaker ?? string.Empty);
    }

    /// <summary>短格式：<c>OKEEVENT:CHECKIN|&lt;议程ID&gt;</c>。</summary>
    public static string Build(string eventId) => Build(eventId, null, null);

    /// <summary>
    /// 解析扫码结果。识别以下四种输入：
    /// <list type="bullet">
    /// <item>完整格式（四段）</item>
    /// <item>短格式（两段）</item>
    /// <item>早期中文格式：<c>【OkEvent 参会凭证】\n议程ID: xxx</c></item>
    /// <item>纯议程 ID（手工输入或第三方系统直接吐 ID）</item>
    /// </list>
    /// </summary>
    public static bool TryParse(string? raw, out CheckInPayload payload)
    {
        payload = default;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();

        // 1. 标准格式：OKEEVENT:CHECKIN|<id>|<title>|<speaker>
        var segments = text.Split(Separator);
        if (segments.Length == 4 &&
            segments[0] == Scheme &&
            !string.IsNullOrWhiteSpace(segments[1]))
        {
            payload = new CheckInPayload(segments[1], segments[2], segments[3]);
            return true;
        }

        // 2. 短格式：OKEEVENT:CHECKIN|<id>
        if (segments.Length == 2 &&
            segments[0] == Scheme &&
            !string.IsNullOrWhiteSpace(segments[1]))
        {
            payload = new CheckInPayload(segments[1], string.Empty, string.Empty);
            return true;
        }

        // 3. 兼容早期中文格式：从任意位置提取「议程ID: xxx」
        var legacy = Regex.Match(text, @"议程ID\s*[:：]\s*(.+)");
        if (legacy.Success && !string.IsNullOrWhiteSpace(legacy.Groups[1].Value))
        {
            payload = new CheckInPayload(legacy.Groups[1].Value.Trim(), string.Empty, string.Empty);
            return true;
        }

        // 4. 兜底：内容本身就是一个议程 ID
        payload = new CheckInPayload(text, string.Empty, string.Empty);
        return true;
    }
}

/// <summary>解析后的凭证内容。核销只信任 <see cref="EventId"/>，其余字段仅用于提示文案。</summary>
public readonly record struct CheckInPayload(string EventId, string Title, string Speaker);
