using System.Collections.ObjectModel;
using System.IO;
using ChatHub.Models;
using ChatHub.Services;

namespace ChatHub.ViewModels;

/// <summary>
/// 主窗口视图模型：持有站点清单、组织工具栏按钮、转发导航命令，并汇总状态栏信息。
/// WebView 池在窗口构造时通过 <see cref="AttachPool"/> 注入，以避开两者的循环依赖。
/// </summary>
public sealed class MainWindowViewModel : ObservableObject, IWebViewHostFeedback
{
    private readonly SiteStore _store;
    private WebViewPool? _pool;
    private Site? _currentSite;
    private string _statusText = "就绪";
    private string _addressText = string.Empty;
    private bool _isLoading;
    private bool _isInitialized;

    /// <summary>站点清单为空时，界面用文件名引导用户手工编辑。</summary>
    private string? _initializationError;
    private object? _activeView;

    public MainWindowViewModel(SiteStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));

        // 命令必须先就绪：LoadSites 会通过 CurrentSite 的 setter 通知命令刷新可用性。
        GoBackCommand = new RelayCommand(() => _pool?.GoBack(), () => _pool?.CanGoBack == true);
        GoForwardCommand = new RelayCommand(() => _pool?.GoForward(), () => _pool?.CanGoForward == true);
        RefreshCommand = new RelayCommand(() => _pool?.Reload(), () => _pool?.CurrentView is not null);
        GoHomeCommand = new RelayCommand(GoHome, () => _pool?.CurrentView is not null);
        SelectSiteCommand = new RelayCommand(
            parameter => SelectSite(parameter),
            parameter => parameter is Site site && !ReferenceEquals(site, CurrentSite));
        ManageSitesCommand = new RelayCommand(() => ManageSitesRequested?.Invoke());

        LoadSites();
    }

    /// <summary>界面订阅：把某个站点的 WebView2 挂到内容区。</summary>
    public event Action<Site>? SiteActivated;

    /// <summary>界面订阅：打开站点管理窗口。</summary>
    public event Action? ManageSitesRequested;

    public ObservableCollection<Site> Sites { get; } = [];

    public RelayCommand GoBackCommand { get; }

    public RelayCommand GoForwardCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand GoHomeCommand { get; }

    public RelayCommand SelectSiteCommand { get; }

    public RelayCommand ManageSitesCommand { get; }

    /// <summary>站点清单文件位置，界面在空状态与提示中展示。</summary>
    public string SitesFilePath => _store.FilePath;

    /// <summary>站点清单读写服务，站点管理窗口复用它。</summary>
    public SiteStore Store => _store;

    public Site? CurrentSite
    {
        get => _currentSite;
        private set
        {
            if (SetProperty(ref _currentSite, value))
            {
                OnPropertyChanged(nameof(HasSites));
                SelectSiteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>内容区当前承载的控件（object 类型，避免视图模型层直接依赖控件类型）。</summary>
    public object? ActiveView
    {
        get => _activeView;
        private set => SetProperty(ref _activeView, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>状态栏右侧显示的当前地址（只读，避免与站点配置出现两处不一致的地址）。</summary>
    public string AddressText
    {
        get => _addressText;
        private set => SetProperty(ref _addressText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasSites => Sites.Count > 0;

    /// <summary>WebView2 初始化失败时的提示文本；为空表示一切正常。</summary>
    public string? InitializationError
    {
        get => _initializationError;
        private set
        {
            if (SetProperty(ref _initializationError, value))
            {
                OnPropertyChanged(nameof(HasInitializationError));
            }
        }
    }

    public bool HasInitializationError => !string.IsNullOrEmpty(InitializationError);

    /// <summary>窗口构造完成后注入 WebView 池。</summary>
    public void AttachPool(WebViewPool pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        OnHistoryChanged();
    }

    /// <summary>窗口加载后调用：首次运行写入默认站点，并打开第一个站点。</summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        if (Sites.Count == 0)
        {
            StatusText = "还没有站点：点击右上角「管理站点」添加一个聊天网址。";
            return;
        }

        await SelectSiteAsync(CurrentSite ?? Sites[0], force: true);
    }

    /// <summary>重新读取站点清单（管理窗口关闭后调用，保证工具栏与文件一致）。</summary>
    public void ReloadSites()
    {
        var activeId = CurrentSite?.Id;
        var previousIds = Sites.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        LoadSites();

        // 被删除的站点要顺手释放它的 WebView2，否则渲染进程会一直留在内存里。
        foreach (var removedId in previousIds.Where(id => Sites.All(s => !string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))))
        {
            if (ReferenceEquals(CurrentSite?.Id, removedId))
            {
                CurrentSite = null;
                ActiveView = null;
            }

            _pool?.Remove(removedId);
        }

        var next = Sites.FirstOrDefault(s => string.Equals(s.Id, activeId, StringComparison.OrdinalIgnoreCase))
                   ?? Sites.FirstOrDefault();

        if (next is null)
        {
            CurrentSite = null;
            ActiveView = null;
            _pool?.SetCurrent(null);
            AddressText = string.Empty;
            StatusText = "站点列表为空：点击右上角「管理站点」添加聊天网址。";
            return;
        }

        if (!ReferenceEquals(next, CurrentSite))
        {
            _ = SelectSiteAsync(next);
        }
    }

    public void GoHome()
    {
        if (CurrentSite is { } site)
        {
            _pool?.Navigate(site.Url);
        }
    }

    public void Dispose() => _pool?.Dispose();

    // ---- IWebViewHostFeedback ----

    public void OnStatus(string message)
    {
        if (HasInitializationError)
        {
            return;
        }

        StatusText = message;
    }

    public void OnProgress(bool loading, double percent) => IsLoading = loading;

    public void OnAddressChanged(string url) => AddressText = url;

    public void OnHistoryChanged()
    {
        GoBackCommand.RaiseCanExecuteChanged();
        GoForwardCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
        GoHomeCommand.RaiseCanExecuteChanged();
    }

    public void OnInitializationFailed(string message)
    {
        InitializationError = message;
        StatusText = "WebView2 组件不可用，网页无法显示。";
        IsLoading = false;
    }

    // ---- 内部实现 ----

    private void LoadSites()
    {
        IReadOnlyList<Site> loaded;
        var skipped = 0;
        try
        {
            loaded = _store.Load(out skipped);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            loaded = [];
            StatusText = $"读取站点清单失败：{ex.Message}";
        }

        Sites.Clear();
        foreach (var site in loaded)
        {
            Sites.Add(site);
        }

        CurrentSite ??= Sites.FirstOrDefault();
        foreach (var site in Sites)
        {
            site.IsActive = ReferenceEquals(site, CurrentSite);
        }

        OnPropertyChanged(nameof(HasSites));
        SelectSiteCommand.RaiseCanExecuteChanged();

        if (skipped > 0)
        {
            StatusText = $"已跳过 {skipped} 行无效的站点数据（文件：{_store.FilePath}）。";
        }
    }

    private void SelectSite(object? parameter)
    {
        if (parameter is Site site)
        {
            _ = SelectSiteAsync(site);
        }
    }

    private async Task SelectSiteAsync(Site? site, bool force = false)
    {
        if (site is null || _pool is null)
        {
            return;
        }

        // 已经加载过的站点直接切回，不重新导航，聊天会话得以保留。
        var alreadyCached = !force && _pool.IsCached(site.Id);

        foreach (var item in Sites)
        {
            item.IsActive = ReferenceEquals(item, site);
        }

        CurrentSite = site;
        var view = _pool.GetOrCreate(site);
        ActiveView = view;
        _pool.SetCurrent(view);
        AddressText = site.Url;

        if (view is null)
        {
            StatusText = "页面组件已释放，请重启应用。";
            return;
        }

        // 视图层负责把控件挂进内容区（挂在树上之后 WebView2 才能完成初始化）。
        SiteActivated?.Invoke(site);

        if (alreadyCached)
        {
            OnHistoryChanged();
            return;
        }

        if (!await _pool.InitializeAsync(site))
        {
            // 具体失败原因已由 OnInitializationFailed / 状态回调给出。
            return;
        }

        OnHistoryChanged();
    }
}
