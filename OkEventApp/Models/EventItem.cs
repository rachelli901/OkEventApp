using CommunityToolkit.Mvvm.ComponentModel;
using SQLite;

namespace OkEventApp.Models;

/// <summary>
/// 议程模型。同时承担 SQLite 实体与绑定数据源两个职责。
/// </summary>
/// <remarks>
/// 继承 <see cref="ObservableObject"/> 实现 INPC：签到 / 收藏状态在详情页或扫码页被改写后，
/// 列表与详情页上的绑定会自行刷新，调用方不需要再手动补一次 OnPropertyChanged。
/// sqlite-net 只按属性（而非 backing field）映射列，改成带后备字段的属性不影响建表与读写。
/// </remarks>
public class EventItem : ObservableObject
{
    private string _title = string.Empty;
    private string _speaker = string.Empty;
    private string _location = string.Empty;
    private string _category = "General";
    private bool _isBookmarked;
    private bool _isCheckedIn;

    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Speaker
    {
        get => _speaker;
        set => SetProperty(ref _speaker, value);
    }

    public string Location
    {
        get => _location;
        set => SetProperty(ref _location, value);
    }

    public string Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    public DateTime StartTime { get; set; }

    public bool IsBookmarked
    {
        get => _isBookmarked;
        set => SetProperty(ref _isBookmarked, value);
    }

    public bool IsCheckedIn
    {
        get => _isCheckedIn;
        set => SetProperty(ref _isCheckedIn, value);
    }

    /// <summary>
    /// 签到状态的自述文案。列表与详情页的状态标签直接绑定它，
    /// 模型侧状态一变，文案随绑定自动跟上。
    /// </summary>
    public string CheckInStatus => IsCheckedIn ? "已签到" : "未签到";
}
