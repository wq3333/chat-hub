using System.Collections.ObjectModel;
using System.IO;
using ChatHub.Models;
using ChatHub.Services;

namespace ChatHub.ViewModels;

/// <summary>
/// 主窗口视图模型：持有站点清单、组织工具栏按钮、转发导航命令，并汇总状态栏信息。
///
/// 两种内容模式：
/// - 站点模式（ActiveTab 为空）：显示当前站点的 WebView2（由 WebViewPool 缓存）；
/// - 标签模式（ActiveTab 非空）：显示新窗口链接产生的临时标签页（由 InAppTabHost 管理，不属于站点池）。
/// 另一种页面只是从内容区卸载，不销毁，因此聊天会话始终保留。
/// </summary>
public sealed class MainWindowViewModel : ObservableObject, IWebViewHostFeedback
{
    private static readonly ObservableCollection<InAppTab> EmptyTabs = [];

    private readonly SiteStore _store;
    private WebViewRuntime? _runtime;
    private WebViewPool? _pool;
    private InAppTabHost? _tabHost;
    private Site? _currentSite;
    private InAppTab? _activeTab;
    private string _statusText = "就绪";
    private string _addressText = string.Empty;
    private bool _isLoading;
    private bool _isInitialized;
    private bool _disposed;

    /// <summary>站点清单为空时，界面用文件名引导用户手工编辑。</summary>
    private string? _initializationError;
    private object? _activeView;

    public MainWindowViewModel(SiteStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));

        // 命令必须先就绪：LoadSites 会通过 CurrentSite 的 setter 通知命令刷新可用性。
        GoBackCommand = new RelayCommand(() => _pool?.GoBack(), () => _pool?.CanGoBack == true);
        GoForwardCommand = new RelayCommand(() => _pool?.GoForward(), () => _pool?.CanGoForward == true);
        RefreshCommand = new RelayCommand(() => _pool?.Reload(), () => _pool?.HasActiveView == true);
        GoHomeCommand = new RelayCommand(
            GoHome,
            () => ActiveTab is null && CurrentSite is not null && _pool?.HasActiveView == true);
        SelectSiteCommand = new RelayCommand(
            parameter => SelectSite(parameter),
            parameter => parameter is Site site && !ReferenceEquals(site, CurrentSite));
        ManageSitesCommand = new RelayCommand(() => ManageSitesRequested?.Invoke());
        CloseTabCommand = new RelayCommand(
            parameter => CloseTab(parameter as InAppTab),
            parameter => parameter is InAppTab tab && Tabs.Contains(tab));

        LoadSites();
    }

    /// <summary>界面订阅：把某个站点的 WebView2 挂到内容区。</summary>
    public event Action<Site>? SiteActivated;

    /// <summary>界面订阅：把某个临时标签页的 WebView2 挂到内容区。</summary>
    public event Action<InAppTab>? TabActivated;

    /// <summary>界面订阅：当前没有任何站点页/标签页可显示，清空内容区。</summary>
    public event Action? ContentCleared;

    /// <summary>界面订阅：打开站点管理窗口。</summary>
    public event Action? ManageSitesRequested;

    public ObservableCollection<Site> Sites { get; } = [];

    /// <summary>临时标签页集合；由 InAppTabHost 持有，站点池与标签页互不影响。</summary>
    public ObservableCollection<InAppTab> Tabs => _tabHost?.Tabs ?? EmptyTabs;

    public RelayCommand GoBackCommand { get; }

    public RelayCommand GoForwardCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand GoHomeCommand { get; }

    public RelayCommand SelectSiteCommand { get; }

    public RelayCommand ManageSitesCommand { get; }

    public RelayCommand CloseTabCommand { get; }

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
                OnPropertyChanged(nameof(ShowEmptyState));
                SelectSiteCommand.RaiseCanExecuteChanged();
                GoHomeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>当前显示的临时标签页；为空时处于站点模式。</summary>
    public InAppTab? ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (SetProperty(ref _activeTab, value))
            {
                GoHomeCommand.RaiseCanExecuteChanged();
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

    public bool HasTabs => Tabs.Count > 0;

    /// <summary>站点和标签页都没有时才显示空状态引导，避免标签页仍在时被引导层盖住。</summary>
    public bool ShowEmptyState => !HasSites && !HasTabs;

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

    /// <summary>窗口构造完成后注入共享运行时。</summary>
    public void AttachRuntime(WebViewRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        OnHistoryChanged();
    }

    /// <summary>窗口构造完成后注入 WebView 池。</summary>
    public void AttachPool(WebViewPool pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        OnHistoryChanged();
    }

    /// <summary>窗口构造完成后注入临时标签页宿主。</summary>
    public void AttachTabHost(InAppTabHost tabHost)
    {
        _tabHost = tabHost ?? throw new ArgumentNullException(nameof(tabHost));

        _tabHost.Tabs.CollectionChanged += OnTabsCollectionChanged;
        _tabHost.TabCreated += OnTabCreated;
        _tabHost.CloseRequested += OnTabCloseRequested;
        _tabHost.TabOpenFailed += OnTabOpenFailed;

        OnPropertyChanged(nameof(Tabs));
        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(ShowEmptyState));
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
        var tabMode = ActiveTab is not null;

        LoadSites();

        // 被删除的站点要顺手释放它的 WebView2，否则渲染进程会一直留在内存里。
        foreach (var removedId in previousIds.Where(id => Sites.All(s => !string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))))
        {
            if (!tabMode && string.Equals(CurrentSite?.Id, removedId, StringComparison.OrdinalIgnoreCase))
            {
                CurrentSite = null;
                ActiveView = null;
            }

            _pool?.Remove(removedId);
        }

        var next = Sites.FirstOrDefault(s => string.Equals(s.Id, activeId, StringComparison.OrdinalIgnoreCase))
                   ?? Sites.FirstOrDefault();

        // 正在看临时标签页时，清单改动只更新工具栏与站点缓存，不抢走当前内容。
        if (tabMode)
        {
            CurrentSite = next;
            foreach (var site in Sites)
            {
                site.IsActive = false;
            }

            OnHistoryChanged();
            return;
        }

        if (next is null)
        {
            CurrentSite = null;
            ActiveView = null;
            _pool?.SetCurrent(null);
            AddressText = string.Empty;
            StatusText = "站点列表为空：点击右上角「管理站点」添加聊天网址。";
            ContentCleared?.Invoke();
            return;
        }

        if (!ReferenceEquals(next, CurrentSite))
        {
            _ = SelectSiteAsync(next);
        }
    }

    public void GoHome()
    {
        if (ActiveTab is null && CurrentSite is { } site)
        {
            _pool?.Navigate(site.Url);
        }
    }

    /// <summary>界面选中某个站点（包括从标签页点击当前站点回切）。</summary>
    public void ActivateSite(Site site)
    {
        ArgumentNullException.ThrowIfNull(site);
        _ = SelectSiteAsync(site);
    }

    /// <summary>激活一个临时标签页，并把它设为当前内容。</summary>
    public void ActivateTab(InAppTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (_tabHost is null || !_tabHost.Tabs.Contains(tab))
        {
            return;
        }

        var view = _tabHost.GetView(tab);
        if (view is null)
        {
            return;
        }

        ActiveTab = tab;

        // 标签页模式下站点工具栏取消高亮；站点页本身仍然保留在池里。
        foreach (var site in Sites)
        {
            site.IsActive = false;
        }

        foreach (var item in Tabs)
        {
            item.IsActive = ReferenceEquals(item, tab);
        }

        _runtime?.SetActiveView(view);
        AddressText = tab.Url;
        OnHistoryChanged();

        TabActivated?.Invoke(tab);
    }

    /// <summary>关闭一个临时标签页；活动页关闭后优先切到相邻标签，最后回退到站点页。</summary>
    public void CloseTab(InAppTab? tab)
    {
        if (tab is null || _tabHost is null || !_tabHost.Tabs.Contains(tab))
        {
            return;
        }

        var wasActive = ReferenceEquals(tab, ActiveTab);
        var index = _tabHost.Tabs.IndexOf(tab);

        if (wasActive)
        {
            // 先切走活动指针，避免释放控件时还在回报它的状态。
            ActiveTab = null;
            _runtime?.SetActiveView(null);
        }

        _tabHost.Remove(tab);

        if (!wasActive)
        {
            return;
        }

        if (_tabHost.Tabs.Count > 0)
        {
            ActivateTab(_tabHost.Tabs[Math.Min(index, _tabHost.Tabs.Count - 1)]);
            return;
        }

        ActivateSiteAfterTabsClosed();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _runtime?.SetActiveView(null);

        if (_tabHost is not null)
        {
            _tabHost.Tabs.CollectionChanged -= OnTabsCollectionChanged;
            _tabHost.TabCreated -= OnTabCreated;
            _tabHost.CloseRequested -= OnTabCloseRequested;
            _tabHost.TabOpenFailed -= OnTabOpenFailed;
            _tabHost.Dispose();
        }

        _pool?.Dispose();
        _runtime?.Dispose();
    }

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

    private void OnTabCreated(InAppTab tab)
    {
        // 必须在标签宿主的异步初始化前完成：先挂到内容区，WebView2 才能初始化。
        ActivateTab(tab);
    }

    private void OnTabCloseRequested(InAppTab tab) => CloseTab(tab);

    private void OnTabOpenFailed(InAppTab tab)
    {
        if (_disposed)
        {
            return;
        }

        if (!ReferenceEquals(ActiveTab, tab) && ActiveTab is not null)
        {
            return;
        }

        ActiveTab = null;
        _runtime?.SetActiveView(null);

        if (Tabs.Count > 0)
        {
            ActivateTab(Tabs[Tabs.Count - 1]);
            return;
        }

        ActivateSiteAfterTabsClosed();
    }

    private void OnTabsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    /// <summary>所有标签页都关闭后回到站点模式：优先当前站点，其次第一个站点，最后清空。</summary>
    private void ActivateSiteAfterTabsClosed()
    {
        if (CurrentSite is { } site)
        {
            _ = SelectSiteAsync(site);
            return;
        }

        if (Sites.Count > 0)
        {
            _ = SelectSiteAsync(Sites[0]);
            return;
        }

        ClearContent();
    }

    private void ClearContent()
    {
        ActiveTab = null;

        foreach (var tab in Tabs)
        {
            tab.IsActive = false;
        }

        _pool?.SetCurrent(null);
        ActiveView = null;
        AddressText = string.Empty;
        OnHistoryChanged();
        ContentCleared?.Invoke();
    }

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
        OnPropertyChanged(nameof(ShowEmptyState));
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

        // 从标签页切回站点：先退出标签模式，标签页本身保持打开。
        if (ActiveTab is not null)
        {
            ActiveTab = null;
            foreach (var tab in Tabs)
            {
                tab.IsActive = false;
            }
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
