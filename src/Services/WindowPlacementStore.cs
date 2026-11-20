using System.IO;
using System.Text;
using System.Text.Json;

namespace ChatHub.Services;

/// <summary>
/// 主窗口位置/大小/最大化状态的读写。文件损坏、缺失或磁盘不可写都不应阻止应用启动或关闭。
/// </summary>
public sealed class WindowPlacementStore
{
    private readonly string _filePath;

    public WindowPlacementStore(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath) ? AppPaths.WindowPlacementFile : filePath;
    }

    public string FilePath => _filePath;

    /// <summary>读取上次的窗口参数；没有记录或记录无效时返回 null（调用方回退到默认最大化）。</summary>
    public WindowPlacement? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_filePath, Encoding.UTF8);
            var dto = JsonSerializer.Deserialize<WindowPlacementDto>(json);
            if (dto is null)
            {
                return null;
            }

            var width = dto.Width;
            var height = dto.Height;
            if (!double.IsFinite(dto.Left) || !double.IsFinite(dto.Top) ||
                !double.IsFinite(width) || !double.IsFinite(height) ||
                width < 200 || height < 100)
            {
                return null;
            }

            return new WindowPlacement(dto.Left, dto.Top, width, height, dto.IsMaximized);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>原子写入：先写临时文件再替换，避免写到一半留下坏 JSON。</summary>
    public void Save(WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var dto = new WindowPlacementDto
        {
            Left = placement.Left,
            Top = placement.Top,
            Width = placement.Width,
            Height = placement.Height,
            IsMaximized = placement.IsMaximized,
        };

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private sealed class WindowPlacementDto
    {
        public double Left { get; set; }

        public double Top { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public bool IsMaximized { get; set; }
    }
}
