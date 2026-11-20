using System.IO;
using System.Net.Http;

namespace ChatHub.Services;

/// <summary>
/// 把 release 资产下载到 %LocalAppData%\ChatHub\update\win-x64.zip。
/// 先写 .part，成功后原子改名，失败/取消时删除半成品。
/// </summary>
public sealed class UpdateDownloader
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    public async Task<string> DownloadAsync(
        ReleaseInfo release,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (!Uri.TryCreate(release.DownloadUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("更新下载地址无效（只接受 https）。");
        }

        Directory.CreateDirectory(AppPaths.UpdateFolder);
        var targetPath = AppPaths.UpdateZipFile;
        var partialPath = targetPath + ".part";
        TryDelete(partialPath);

        try
        {
            using var response = await HttpClient
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? release.AssetSize;
            progress?.Report(new DownloadProgress(0, total));

            var buffer = new byte[81920];
            long received = 0;
            long lastReported = 0;

            // 两个流必须在 File.Move 之前释放：FileShare.None 打开的文件
            // 在 Windows 上无法被重命名/移动。
            await using (var source = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false))
            await using (var destination = new FileStream(
                partialPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                while (true)
                {
                    var read = await source
                        .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                        .ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    await destination
                        .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);

                    received += read;

                    // 控制刷新频率：每 256 KB 或到末尾时回报一次，避免刷爆 UI 消息队列。
                    if (progress is not null && (received - lastReported >= 256 * 1024))
                    {
                        progress.Report(new DownloadProgress(received, total));
                        lastReported = received;
                    }

                    // 已知总长度时，收满即结束：部分代理/CDN 会在最后一个字节之后
                    // 保持连接不发送 EOF，继续等待 ReadAsync 会看起来“卡在 100%”。
                    if (total is > 0 && received >= total.Value)
                    {
                        break;
                    }
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (total is > 0 && received < total.Value)
            {
                throw new IOException($"下载不完整：{received} / {total.Value} 字节。");
            }

            File.Move(partialPath, targetPath, overwrite: true);
            progress?.Report(new DownloadProgress(received, total ?? received));
            return targetPath;
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删除失败不影响后续流程，下次下载会覆盖。
        }
    }
}
