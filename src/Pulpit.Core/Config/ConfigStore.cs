using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pulpit.Core.Config;

/// <summary>
/// 配置文件读写：<c>%LOCALAPPDATA%\Pulpit\config.json</c>（§7）。
/// </summary>
/// <remarks>
/// <b>本类永不抛异常给调用方。</b>§7 规定「配置缺失或字段非法时用内置默认值，
/// 并写日志，不弹窗」——所以 <see cref="Load"/> 的任何失败路径都返回默认配置
/// 外加一条说明。直播前十分钟配置文件被编辑器写坏了，程序也必须能起来。
/// </remarks>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        // 手工编辑配置文件的人会写注释和多余逗号，这两条让它们不至于让整份配置失效。
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public ConfigStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(DefaultDirectory, "config.json");
    }

    /// <summary><c>%LOCALAPPDATA%\Pulpit</c>。</summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Pulpit");

    public string FilePath { get; }

    /// <summary>
    /// 读配置。文件不存在时**写一份默认配置出来**，好让操作员有个可编辑的起点。
    /// </summary>
    /// <param name="notes">发生过的修正/退让，供调用方写日志。空表示一切正常。</param>
    public AppConfig Load(out IReadOnlyList<string> notes)
    {
        var messages = new List<string>();

        AppConfig loaded;

        if (!File.Exists(FilePath))
        {
            messages.Add($"配置文件不存在，使用内置默认值并写出一份：{FilePath}");
            loaded = new AppConfig();

            if (!TrySave(loaded, out string? saveError))
            {
                messages.Add($"默认配置写出失败（不影响运行）：{saveError}");
            }
        }
        else
        {
            loaded = ReadFile(messages);
        }

        AppConfig sanitized = loaded.Sanitize(out IReadOnlyList<string> corrections);
        messages.AddRange(corrections);

        notes = messages;
        return sanitized;
    }

    private AppConfig ReadFile(List<string> messages)
    {
        try
        {
            string json = File.ReadAllText(FilePath);

            if (string.IsNullOrWhiteSpace(json))
            {
                messages.Add($"配置文件是空的，使用内置默认值：{FilePath}");
                return new AppConfig();
            }

            AppConfig? parsed = JsonSerializer.Deserialize<AppConfig>(json, Options);

            if (parsed is null)
            {
                messages.Add($"配置文件内容为 null，使用内置默认值：{FilePath}");
                return new AppConfig();
            }

            return parsed;
        }
        catch (JsonException ex)
        {
            messages.Add($"配置文件不是合法 JSON，使用内置默认值（{ex.Message}）");
            return new AppConfig();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            messages.Add($"配置文件读不出来，使用内置默认值（{ex.Message}）");
            return new AppConfig();
        }
    }

    /// <summary>
    /// 写配置。返回 false 时 <paramref name="error"/> 为原因；调用方记日志即可，
    /// 写不进去不是停机理由。
    /// </summary>
    /// <remarks>
    /// 先写临时文件再替换：直接覆盖原文件时若进程在中途结束，
    /// 留下的是一份被截断的配置——下次启动会退回默认值，操作员的副屏设置就丢了。
    /// </remarks>
    public bool TrySave(AppConfig config, out string? error)
    {
        ArgumentNullException.ThrowIfNull(config);

        error = null;

        try
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(config, Options);
            string temporary = FilePath + ".tmp";

            File.WriteAllText(temporary, json);
            File.Move(temporary, FilePath, overwrite: true);

            return true;
        }
        catch (Exception ex) when (ex is IOException
                                     or UnauthorizedAccessException
                                     or NotSupportedException
                                     or JsonException)
        {
            error = ex.Message;
            return false;
        }
    }
}
