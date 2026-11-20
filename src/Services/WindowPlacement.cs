namespace ChatHub.Services;

/// <summary>
/// 主窗口的持久化位置/大小。数值均为 WPF 的设备无关单位（DIP）。
/// <see cref="IsMaximized"/> 只记录窗口状态，位置/大小始终保存“还原后”的普通尺寸。
/// </summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool IsMaximized)
{
    /// <summary>窗口至少要有这么宽可见，用户才抓得住。</summary>
    public const double MinVisibleWidth = 120;

    /// <summary>窗口至少要有这么高可见（含标题栏）。</summary>
    public const double MinVisibleHeight = 48;

    /// <summary>指定屏幕范围内是否还剩下足够可见的窗口区域（用于发现显示器被移除）。</summary>
    public bool IsOnScreen(double screenLeft, double screenTop, double screenWidth, double screenHeight)
    {
        if (!IsFinite() || screenWidth <= 0 || screenHeight <= 0)
        {
            return false;
        }

        var overlapWidth = Math.Min(Left + Width, screenLeft + screenWidth) - Math.Max(Left, screenLeft);
        var overlapHeight = Math.Min(Top + Height, screenTop + screenHeight) - Math.Max(Top, screenTop);

        return overlapWidth >= MinVisibleWidth && overlapHeight >= MinVisibleHeight;
    }

    /// <summary>
    /// 把窗口挪进屏幕范围，保证标题栏和一小块内容可见；尺寸保持不变（超出屏幕时也保留原尺寸）。
    /// </summary>
    public WindowPlacement ClampInto(double screenLeft, double screenTop, double screenWidth, double screenHeight)
    {
        var minLeft = screenLeft - Width + MinVisibleWidth;
        var maxLeft = screenLeft + screenWidth - MinVisibleWidth;
        var left = maxLeft >= minLeft ? Math.Clamp(Left, minLeft, maxLeft) : screenLeft;

        var minTop = screenTop;
        var maxTop = screenTop + screenHeight - MinVisibleHeight;
        var top = maxTop >= minTop ? Math.Clamp(Top, minTop, maxTop) : screenTop;

        return this with { Left = left, Top = top };
    }

    private bool IsFinite() =>
        double.IsFinite(Left) && double.IsFinite(Top) &&
        double.IsFinite(Width) && double.IsFinite(Height) &&
        Width > 0 && Height > 0;
}
