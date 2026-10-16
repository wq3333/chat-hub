using ChatHub.ViewModels;

namespace ChatHub.Models;

/// <summary>一个被收藏的 AI 聊天站点。工具栏按钮与站点清单文件都以它为单位。</summary>
public sealed class Site : ObservableObject
{
    private string _name = string.Empty;
    private string _url = string.Empty;
    private bool _isActive;

    public Site(string id, string name, string url)
    {
        Id = string.IsNullOrWhiteSpace(id) ? NewId() : id;
        _name = name;
        _url = url;
    }

    /// <summary>稳定标识：WebView 池按它缓存页面，站点清单文件也存它。</summary>
    public string Id { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value);
    }

    /// <summary>是否为工具栏当前选中项，仅用于界面高亮。</summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    /// <summary>生成 8 位十六进制短 id，写入 txt 后仍保持可读。</summary>
    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    public override string ToString() => $"{Name} <{Url}>";
}
