using OkEventApp.ViewModels;

namespace OkEventApp.Views;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 1. 首次进入（或刷新过）才整体加载；
        //    从详情页/扫码页返回时用本地缓存校准签到状态，不重复走网络
        if (_viewModel.Events.Count == 0)
        {
            await _viewModel.LoadEventsAsync();
        }
        else
        {
            await _viewModel.SyncCheckInStateAsync();
        }

        // 2. 清空选中项（确保从详情页返回后，点击同一项仍能触发跳转）
        EventsCollectionView.SelectedItem = null;
    }
}