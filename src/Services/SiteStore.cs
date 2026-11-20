using System.IO;
using System.Text;
using ChatHub.Models;

namespace ChatHub.Services;

/// <summary>
/// 站点清单的持久化：纯文本（Tab 分隔）存放于 %AppData%\ChatHub\sites.txt，用户可直接用记事本查看与编辑。
/// </summary>
public class SiteStore
{
    private const string HeaderLine = "# id\tname\turl";
    private const char Separator = '\t';

    private readonly string _filePath;

    /// <param name="filePath">清单文件路径；测试可传入临时目录下的文件，避免污染真实用户数据。</param>
    public SiteStore(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath) ? AppPaths.SitesFile : filePath;
    }

    public string FilePath => _filePath;

    /// <summary>首次运行写入的默认站点。</summary>
    public static IReadOnlyList<Site> CreateDefaults() =>
    [
        new Site("deepseek", "deepseek", "https://chat.deepseek.com/"),
        new Site("chatgpt", "chatgpt", "https://chatgpt.com/"),
        new Site("claude", "claude", "https://claude.ai/new"),
        new Site("doubao", "豆包", "https://www.doubao.com/chat/"),
        new Site("bigmodel", "智谱", "https://open.bigmodel.cn/trialcenter/modeltrial/text"),
        new Site("qianwen", "千问", "https://www.qianwen.com/"),
        new Site("kimi", "kimi", "https://www.kimi.com/"),
        new Site("hy", "混元", "https://aistudio.tencent.com/?utm_source=hy"),
        new Site("minimax", "minimax", "https://agent.minimax.cn/"),
    ];

    /// <summary>
    /// 读取站点清单。
    /// 文件不存在 → 写入默认站点并返回（首次运行）；
    /// 文件存在但为空 → 同样按首次运行处理；
    /// 文件存在且只含注释/表头 → 返回空集合，不重新种子（尊重用户清空的操作）。
    /// </summary>
    /// <param name="skippedLineCount">被跳过的无效行数，用于在界面上提示。</param>
    public IReadOnlyList<Site> Load(out int skippedLineCount)
    {
        skippedLineCount = 0;

        if (!File.Exists(_filePath))
        {
            var seeded = CreateDefaults();
            Save(seeded);
            return seeded;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(_filePath, Encoding.UTF8);
        }
        catch (IOException)
        {
            // 读不到时至少保证应用能起来，界面会提示没有任何站点。
            return [];
        }

        var sites = new List<Site>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 文件里只有空白/BOM 时等同首次运行，否则用户永远看不到默认站点。
        if (lines.All(string.IsNullOrWhiteSpace))
        {
            var seeded = CreateDefaults();
            Save(seeded);
            return seeded;
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                continue; // 注释行（含表头）
            }

            var parts = line.Split(Separator);
            string? id = null;
            string? name;
            string? url;

            switch (parts.Length)
            {
                case >= 3:
                    id = parts[0].Trim();
                    name = parts[1].Trim();
                    // 网址本身可能含制表符的概率极低，但为了稳妥仍把剩余列拼回来。
                    url = string.Join(Separator, parts[2..]).Trim();
                    break;
                case 2:
                    // 用户手写时可能漏了 id，此时按 "名称 网址" 解析并自动补 id。
                    name = parts[0].Trim();
                    url = parts[1].Trim();
                    break;
                default:
                    // 整行只有一个网址，名称与 id 都自动补。
                    name = string.Empty;
                    url = parts[0].Trim();
                    break;
            }

            if (IsHeaderRow(id, name, url))
            {
                continue;
            }

            var normalizedUrl = UrlNormalizer.Normalize(url);
            if (normalizedUrl is null)
            {
                skippedLineCount++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(id) || !usedIds.Add(id))
            {
                id = Site.NewId();
                usedIds.Add(id);
            }

            var finalName = UrlNormalizer.ClampName(name);
            if (string.IsNullOrEmpty(finalName))
            {
                finalName = UrlNormalizer.FallbackName(normalizedUrl);
            }

            sites.Add(new Site(id, finalName, normalizedUrl));
        }

        return sites;
    }

    public IReadOnlyList<Site> Load() => Load(out _);

    /// <summary>原子写入：先写 .tmp 再替换，避免写入中断后留下半截清单。</summary>
    public virtual void Save(IEnumerable<Site> sites)
    {
        ArgumentNullException.ThrowIfNull(sites);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        builder.AppendLine("# ChatHub 站点清单（Tab 分隔，可直接编辑；以 # 开头的行会被忽略）");
        builder.AppendLine(HeaderLine);
        foreach (var site in sites)
        {
            builder.Append(site.Id).Append(Separator)
                   .Append(Sanitize(site.Name)).Append(Separator)
                   .Append(Sanitize(site.Url)).AppendLine();
        }

        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static bool IsHeaderRow(string? id, string? name, string? url) =>
        string.Equals(name, "name", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(url, "url", StringComparison.OrdinalIgnoreCase);

    /// <summary>制表符与换行会破坏行结构，替换为空格。</summary>
    private static string Sanitize(string? value) =>
        (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
