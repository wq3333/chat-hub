using ChatHub.Models;
using ChatHub.Services;

namespace ChatHub.ViewModels;

/// <summary>新增 / 修改站点的对话框视图模型，负责网址合法性、重复网址与名称长度的校验。</summary>
public sealed class SiteEditViewModel : ObservableObject
{
    private readonly string _id;
    private readonly IReadOnlyList<Site> _existingSites;
    private readonly RelayCommand _okCommand;
    private string _name = string.Empty;
    private string _url = string.Empty;
    private string _validationMessage = string.Empty;

    private SiteEditViewModel(string id, string name, string url, IEnumerable<Site> existingSites)
    {
        _id = id;
        _name = name;
        _url = url;
        _existingSites = existingSites?.ToList() ?? [];
        _okCommand = new RelayCommand(() => { }, () => !HasValidationMessage);

        Validate();
        _okCommand.RaiseCanExecuteChanged();
    }

    /// <summary>新增场景：名称与网址均为空，直接进入编辑状态。</summary>
    public static SiteEditViewModel ForNew(IEnumerable<Site> existingSites, string? presetName = null, string? presetUrl = null) =>
        new(Site.NewId(), presetName ?? string.Empty, presetUrl ?? string.Empty, existingSites);

    public static SiteEditViewModel ForEdit(Site site, IEnumerable<Site> existingSites)
    {
        ArgumentNullException.ThrowIfNull(site);
        return new SiteEditViewModel(site.Id, site.Name, site.Url, existingSites);
    }

    public bool IsNew => !_existingSites.Any(s => string.Equals(s.Id, _id, StringComparison.OrdinalIgnoreCase));

    public string Title => IsNew ? "新增站点" : "修改站点";

    /// <summary>名称可留空，留空时用域名兜底。</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                Validate();
            }
        }
    }

    public string Url
    {
        get => _url;
        set
        {
            if (SetProperty(ref _url, value))
            {
                Validate();
            }
        }
    }

    /// <summary>校验失败原因；为空表示可以保存。</summary>
    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationMessage));
            }
        }
    }

    public bool HasValidationMessage => ValidationMessage.Length > 0;

    /// <summary>校验通过后的规范化网址。</summary>
    public string NormalizedUrl { get; private set; } = string.Empty;

    /// <summary>确认按钮命令（校验不通过时不可执行）。</summary>
    public RelayCommand OkCommand => _okCommand;

    /// <summary>确认保存时最终写入的名称：裁剪长度，留空则用域名兜底。</summary>
    public string ResolveFinalName() =>
        UrlNormalizer.ClampName(string.IsNullOrWhiteSpace(Name) ? UrlNormalizer.FallbackName(NormalizedUrl) : Name);

    private void Validate()
    {
        var normalized = UrlNormalizer.Normalize(Url);

        if (!string.IsNullOrWhiteSpace(Url) && normalized is null)
        {
            // 有输入但解析不出来
            NormalizedUrl = string.Empty;
            ValidationMessage = "请输入合法的 http / https 网址，例如 https://chat.deepseek.com/";
        }
        else if (normalized is not null && _existingSites.Any(other =>
                     !string.Equals(other.Id, _id, StringComparison.OrdinalIgnoreCase) &&
                     UrlNormalizer.AreEquivalent(other.Url, normalized)))
        {
            NormalizedUrl = normalized;
            ValidationMessage = "该网址已存在于站点清单中。";
        }
        else if (normalized is null)
        {
            // 还没输入网址
            NormalizedUrl = string.Empty;
            ValidationMessage = "请输入聊天网址，例如 https://chat.deepseek.com/";
        }
        else
        {
            NormalizedUrl = normalized;
            ValidationMessage = string.Empty;
        }

        _okCommand.RaiseCanExecuteChanged();
    }
}
