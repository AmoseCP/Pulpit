using System;
using System.IO;
using System.Reflection;
using Pulpit.App.Diagnostics;
using Pulpit.Core.Config;

namespace Pulpit.App.Data;

/// <summary>
/// 把随包嵌入的 <c>bible_cuv.db</c> 铺到 <c>%LOCALAPPDATA%\Pulpit\</c>（M6）。
/// </summary>
/// <remarks>
/// <para><b>为什么嵌成资源而不是放在 exe 旁边</b>：单文件发布
/// （<c>PublishSingleFile</c>）时 exe 旁边就没有别的文件了，而 SQLite 需要一个
/// 真实的文件路径，不能从内存流打开。嵌入 + 首次运行解出，开发运行与单文件发布
/// 走的是**同一条**路径，不用维护两套查找逻辑。</para>
/// <para><b>是否需要重新解出</b>：比对文件长度。换了新版经文库（长度必然不同）
/// 会自动覆盖；长度一致就直接用现成的，省掉每次启动 7MB 的写盘。
/// 用长度而不是哈希，是因为哈希要把 7MB 读两遍，而这个库不是攻击面。</para>
/// </remarks>
internal static class DatabaseProvisioner
{
    /// <summary>嵌入资源的逻辑名。在 csproj 里用 <c>LogicalName</c> 显式钉住。</summary>
    private const string ResourceName = "Pulpit.App.Assets.bible_cuv.db";

    private const string FileName = "bible_cuv.db";

    /// <summary>
    /// 确保本地有一份可用的经文库，返回它的路径。
    /// 失败时返回 null 并给出原因——**不抛异常**，经文库不可用不该阻止启动
    /// （自由文本仍然能投）。
    /// </summary>
    internal static string? EnsureLocalCopy(out string? error)
    {
        error = null;

        string targetDirectory = ConfigStore.DefaultDirectory;
        string target = Path.Combine(targetDirectory, FileName);

        Assembly assembly = typeof(DatabaseProvisioner).Assembly;

        try
        {
            using Stream? resource = assembly.GetManifestResourceStream(ResourceName);

            if (resource is null)
            {
                // 资源没嵌进去（构建配置出问题）。若本地已经有一份就还能用。
                if (File.Exists(target))
                {
                    AppLog.Warn($"嵌入资源 {ResourceName} 不存在，改用已有的 {target}。");
                    return target;
                }

                error = $"随包的经文库资源 {ResourceName} 不存在，且本地也没有 {target}。";
                return null;
            }

            if (File.Exists(target) && new FileInfo(target).Length == resource.Length)
            {
                return target;
            }

            Directory.CreateDirectory(targetDirectory);

            // 先写临时文件再替换：解出中途失败不会留下半个库把下次启动也带坏。
            string temporary = target + ".tmp";

            using (FileStream output = File.Create(temporary))
            {
                resource.CopyTo(output);
            }

            File.Move(temporary, target, overwrite: true);

            AppLog.Info($"经文库已解出到 {target}（{resource.Length / 1024} KB）。");
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 解不出来但本地有旧的，就用旧的——能跑比不能跑好。
            if (File.Exists(target))
            {
                AppLog.Warn($"经文库解出失败（{ex.Message}），改用已有的 {target}。");
                return target;
            }

            error = $"经文库无法就位：{ex.Message}";
            return null;
        }
    }
}
