using Pulpit.Core.Content;

namespace Pulpit.App.Views;

/// <summary>
/// 叠加层对外的全部操作面（DEVELOPMENT_PLAN §5）。
/// </summary>
/// <remarks>
/// 这个接口留在 App 层而不是 Core，是因为 <see cref="MoveToScreen"/> 需要
/// <c>System.Windows.Forms.Screen</c>——CLAUDE.md 规定 <c>Screen</c> 仅在 App 层使用。
/// </remarks>
public interface IOverlayController
{
    /// <summary>投放内容并淡入。重复调用即换内容，窗口不重建。</summary>
    void Show(DisplayContent content);

    /// <summary>清屏 = 淡出 + 内容置空。**不 Hide、不 Close**（L4）。</summary>
    void Clear();

    /// <summary>前进一页；已在末页返回 false 且不动（不循环）。</summary>
    bool NextPage();

    /// <summary>后退一页；已在首页返回 false 且不动（不循环）。</summary>
    bool PrevPage();

    void MoveToScreen(System.Windows.Forms.Screen screen);
}
