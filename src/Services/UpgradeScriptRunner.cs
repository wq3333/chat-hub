using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ChatHub.Services;

/// <summary>
/// 调用随包发布的 upgrade.bat：
/// - <see cref="CheckAsync"/> 执行 check 并把 JSON 解析成更新结果；
/// - <see cref="StartUpgrade"/> 执行 upgrade（不等待，应用随后自行退出）。
/// </summary>
public sealed class UpgradeScriptRunner
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(60);

    public UpgradeScriptRunner(string? scriptPath = null, string? installDirectory = null)
    {
        ScriptPath = Path.GetFullPath(scriptPath ?? Path.Combine(AppContext.BaseDirectory, "upgrade.bat"));
        InstallDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(installDirectory ?? AppContext.BaseDirectory));
    }

    public string ScriptPath { get; }

    public string InstallDirectory { get; }

    /// <summary>运行 upgrade.bat check，返回 release 信息或可展示的错误。</summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ScriptPath))
        {
            return UpdateCheckResult.Failed($"未找到升级脚本：{ScriptPath}");
        }

        var startInfo = CreateStartInfo("check");
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                return UpdateCheckResult.Failed("无法启动升级检查进程。");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return UpdateCheckResult.Failed($"无法启动升级检查进程：{ex.Message}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return UpdateCheckResult.Failed(
                cancellationToken.IsCancellationRequested
                    ? "已取消检查更新。"
                    : $"检查更新超时（{CheckTimeout.TotalSeconds:0} 秒）。");
        }

        string stdout;
        string stderr;
        try
        {
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            stdout = string.Empty;
            stderr = ex.Message;
        }

        var parsed = UpdateChecker.ParseCheckResponse(stdout);

        // check 的成功与失败 JSON 都走 stdout；结构完整时优先用它。
        if (parsed.Success || LooksLikeJson(stdout))
        {
            return parsed;
        }

        // 脚本没能产出 JSON（例如 PowerShell 不可用）时退回 stderr / 退出码。
        var detail = string.IsNullOrWhiteSpace(stderr) ? parsed.Error : stderr.Trim();
        return UpdateCheckResult.Failed(
            string.IsNullOrWhiteSpace(detail)
                ? $"检查更新失败（退出码 {process.ExitCode}）。"
                : detail);
    }

    /// <summary>
    /// 启动 upgrade 流程。脚本会等待本进程退出后替换安装目录，因此这里不等待、
    /// 也不重定向输出；调用方随后应立即关闭应用。
    /// </summary>
    public void StartUpgrade(string zipPath)
    {
        if (!File.Exists(ScriptPath))
        {
            throw new FileNotFoundException($"未找到升级脚本：{ScriptPath}", ScriptPath);
        }

        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException($"未找到更新包：{zipPath}", zipPath);
        }

        var startInfo = CreateStartInfo("upgrade");
        startInfo.Environment["CHATHUB_UPGRADE_ZIP"] = Path.GetFullPath(zipPath);
        startInfo.Environment["CHATHUB_INSTALL_DIR"] = InstallDirectory;
        startInfo.Environment["CHATHUB_UPGRADE_LOG"] = AppPaths.UpgradeLogFile;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动升级进程。");
    }

    private ProcessStartInfo CreateStartInfo(string mode) =>
        new()
        {
            // cmd 的命令行解析要求整个 bat 路径再包一层引号："bat" 后接模式。
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            Arguments = $"/c \"\"{ScriptPath}\" {mode}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = InstallDirectory,
        };

    private static bool LooksLikeJson(string? value)
    {
        var trimmed = (value ?? string.Empty).TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // 进程可能刚好自行退出，忽略。
        }
    }
}
