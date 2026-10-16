using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using ChatHub.Models;
using ChatHub.Services;

namespace ChatHub.ViewModels;

/// <summary>
/// 站点管理窗口的视图模型：增 / 改 / 查（关键字过滤）/ 删。
/// 约定：内存集合始终与 sites.txt 保持一致——写盘失败就回滚内存改动。
/// </summary>
public sealed class SiteManagerViewModel : ObservableObject
{
    private readonly SiteStore _store;
    private string _filterText = string.Empty;
    private string _statusText = string.Empty;

    public SiteManagerViewModel(SiteStore store, ObservableCollection<Site> sites)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Sites = sites ?? throw new ArgumentNullException(nameof(sites));

        SitesView = new CollectionViewSource { Source = Sites }.View;
        SitesView.Filter = MatchesFilter;

        AddCommand = new RelayCommand(Add);
        EditCommand = new RelayCommand(Edit, () => SelectedSite is not null);
        DeleteCommand = new RelayCommand(Delete, () => SelectedSite is not null);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
    }

    public ObservableCollection<Site> Sites { get; }

    /// <summary>带过滤的视图，界面 DataGrid 绑定它。</summary>
    public ICollectionView SitesView { get; }

    public RelayCommand AddCommand { get; }

    public RelayCommand EditCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public RelayCommand CloseCommand { get; }

    /// <summary>由视图注入：打开编辑对话框，返回用户是否确认。</summary>
    public Func<SiteEditViewModel, bool>? ShowEditDialog { get; set; }

    /// <summary>由视图注入：删除前确认，返回用户是否同意。</summary>
    public Func<string, bool>? ConfirmDelete { get; set; }

    /// <summary>由视图注入：显示错误提示。</summary>
    public Action<string>? ShowError { get; set; }

    /// <summary>由视图注入：请求关闭窗口。</summary>
    public Action? RequestClose { get; set; }

    public Site? SelectedSite { get; set; }

    /// <summary>查询条件：按名称或网址做不区分大小写的包含匹配。</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                SitesView.Refresh();
                UpdateStatus();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public void OnSelectionChanged(Site? site)
    {
        SelectedSite = site;
        EditCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
    }

    public void UpdateStatus()
    {
        StatusText = FilterText.Length == 0
            ? $"共 {Sites.Count} 个站点"
            : $"匹配 {CountVisible()} / 共 {Sites.Count} 个站点";
    }

    public void Add()
    {
        var editor = SiteEditViewModel.ForNew(Sites);
        if (ShowEditDialog?.Invoke(editor) != true)
        {
            return;
        }

        var site = new Site(Site.NewId(), editor.ResolveFinalName(), editor.NormalizedUrl);
        Sites.Add(site);
        if (!TryPersist())
        {
            Sites.Remove(site);
        }

        UpdateStatus();
    }

    public void Edit()
    {
        if (SelectedSite is not { } target)
        {
            return;
        }

        var editor = SiteEditViewModel.ForEdit(target, Sites);
        if (ShowEditDialog?.Invoke(editor) != true)
        {
            return;
        }

        var originalName = target.Name;
        var originalUrl = target.Url;
        target.Name = editor.ResolveFinalName();
        target.Url = editor.NormalizedUrl;

        if (!TryPersist())
        {
            target.Name = originalName;
            target.Url = originalUrl;
        }

        UpdateStatus();
    }

    public void Delete()
    {
        if (SelectedSite is not { } target)
        {
            return;
        }

        if (ConfirmDelete?.Invoke(target.Name) != true)
        {
            return;
        }

        var index = Sites.IndexOf(target);
        Sites.Remove(target);
        if (!TryPersist())
        {
            Sites.Insert(Math.Min(index, Sites.Count), target);
        }

        UpdateStatus();
    }

    private bool TryPersist()
    {
        try
        {
            _store.Save(Sites);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError?.Invoke($"保存站点清单失败：{ex.Message}\n文件位置：{_store.FilePath}\n本次改动已撤销。");
            return false;
        }
    }

    private bool MatchesFilter(object item)
    {
        if (FilterText.Length == 0)
        {
            return true;
        }

        if (item is not Site site)
        {
            return false;
        }

        return site.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            || site.Url.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

    private int CountVisible()
    {
        var count = 0;
        foreach (var _ in SitesView)
        {
            count++;
        }

        return count;
    }
}
