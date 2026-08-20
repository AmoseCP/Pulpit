using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Pulpit.App;

/// <summary>GitHub Releases 上的一个可用更新。</summary>
public sealed record UpdateInfo(Version Version, string AssetName, string DownloadUrl, long SizeBytes);

/// <summary>
/// 手动检查更新。§9「不联网」的 2026-08-20 修订（DEVELOPMENT_PLAN §11 第 15 条）：
/// 本类是全项目**唯一**允许发起网络请求的地方，且只能由操作员点击「检查更新」触发——
/// 绝不在启动时、后台或任何自动路径上调用。查询 GitHub Releases 的 latest，
/// 比当前版本新则可下载 Pulpit-Setup-*.exe 并启动**可见的**安装向导（不做静默替换）。
/// </summary>
internal static class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/AmoseCP/Pulpit/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Pulpit-UpdateCheck");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>当前版本，取 csproj 的 &lt;Version&gt;（InformationalVersion，去掉 +元数据）。</summary>
    internal static Version CurrentVersion { get; } = ReadCurrentVersion();

    private static Version ReadCurrentVersion()
    {
        string info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        int metadata = info.IndexOf('+');
        if (metadata >= 0)
        {
            info = info[..metadata];
        }

        return Version.TryParse(info, out Version? parsed) ? parsed : new Version(0, 0, 0);
    }

    /// <summary>返回比当前更新的版本；已是最新（或最新版没有安装包资产）返回 null。</summary>
    internal static async Task<UpdateInfo?> CheckAsync()
    {
        using HttpResponseMessage response = await Http.GetAsync(LatestReleaseApi);
        response.EnsureSuccessStatusCode();

        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = doc.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            tag = tag[1..];
        }

        if (!Version.TryParse(tag, out Version? latest) || latest <= CurrentVersion)
        {
            return null;
        }

        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (name.StartsWith("Pulpit-Setup-", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateInfo(
                    latest,
                    name,
                    asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                    asset.GetProperty("size").GetInt64());
            }
        }

        return null;
    }

    /// <summary>下载安装包到 %TEMP%，经 <paramref name="progress"/> 报告 0..1 进度，返回文件路径。</summary>
    internal static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double> progress)
    {
        string path = Path.Combine(Path.GetTempPath(), update.AssetName);

        using HttpResponseMessage response =
            await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using Stream source = await response.Content.ReadAsStreamAsync();
        await using FileStream target = File.Create(path);

        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            if (update.SizeBytes > 0)
            {
                progress.Report((double)done / update.SizeBytes);
            }
        }

        return path;
    }

    /// <summary>启动可见的安装向导。安装器（Inno CloseApplications=yes）会自行请求关闭 Pulpit。</summary>
    internal static void LaunchInstaller(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
