using System;
using System.Collections.Generic;
using System.Linq;

namespace Pulpit.Core.Config;

/// <summary>
/// 全局热键键位。字段值是键名字符串（<c>"F9"</c>），到虚拟键码的映射在 App 层。
/// </summary>
/// <remarks>
/// **L7 是这个项目最危险的一条约束**：<c>RegisterHotKey</c> 是全局独占——注册了哪个键，
/// 那个键就不再传给 WPS。误注册方向键 = 操作员再也翻不了 PPT。
/// 所以本类型的每个字段在 <see cref="Sanitize"/> 里都要过
/// <see cref="HotkeyWhitelist"/>，白名单外的值一律**拒绝并退回默认**，
/// 而不是「照配置文件办」。配置文件不是可信输入。
/// </remarks>
public sealed record HotkeyConfig
{
    public string SendZh { get; init; } = "F9";

    public string SendEn { get; init; } = "F10";

    public string PrevPage { get; init; } = "F7";

    public string NextPage { get; init; } = "F8";

    public string Clear { get; init; } = "F12";

    internal HotkeyConfig Sanitize(List<string> notes)
    {
        return this with
        {
            SendZh = Vet(SendZh, "F9", nameof(SendZh), notes),
            SendEn = Vet(SendEn, "F10", nameof(SendEn), notes),
            PrevPage = Vet(PrevPage, "F7", nameof(PrevPage), notes),
            NextPage = Vet(NextPage, "F8", nameof(NextPage), notes),
            Clear = Vet(Clear, "F12", nameof(Clear), notes),
        };
    }

    private static string Vet(string value, string fallback, string field, List<string> notes)
    {
        if (HotkeyWhitelist.IsAllowed(value))
        {
            return HotkeyWhitelist.Canonicalize(value);
        }

        notes.Add(
            $"hotkeys.{char.ToLowerInvariant(field[0]) + field[1..]}「{value}」不在允许的键位内" +
            $"（只许 {HotkeyWhitelist.AllowedList}），已退回 {fallback}。" +
            "注册 PPT 翻页键会让操作员无法翻页。");

        return fallback;
    }
}

/// <summary>
/// 允许注册为全局热键的键位白名单。
/// </summary>
/// <remarks>
/// 白名单而非黑名单，是因为「哪些键属于放映软件」这个集合远大于「哪些键属于我们」，
/// 而且会随放映软件版本变化。方向键、PgUp/PgDn、Space、Enter、Esc、B、W、F5
/// 都是 PPT 的翻页/黑屏/白屏键，不在名单内即被拒。
/// </remarks>
public static class HotkeyWhitelist
{
    private static readonly string[] Allowed = ["F7", "F8", "F9", "F10", "F12"];

    /// <summary>供报错文案使用的可读列表。</summary>
    public static string AllowedList => string.Join(" ", Allowed);

    public static IReadOnlyList<string> All => Allowed;

    public static bool IsAllowed(string? keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return false;
        }

        string trimmed = keyName.Trim();
        return Allowed.Any(a => string.Equals(a, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>把 <c>f9</c> / <c> F9 </c> 统一成 <c>F9</c>。仅对白名单内的键有意义。</summary>
    public static string Canonicalize(string keyName)
    {
        string trimmed = keyName.Trim();
        return Allowed.FirstOrDefault(
            a => string.Equals(a, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }
}
