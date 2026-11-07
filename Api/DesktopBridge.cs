namespace ApifoxHelper.Api;

/// <summary>
/// 桌面窗口向本地服务提供的本机能力。无界面模式下没有窗口，这些能力为空，页面据此隐藏对应按钮。
/// </summary>
public sealed class DesktopBridge
{
    /// <summary>弹出系统“选择文件夹”对话框；参数为初始目录（可为空），返回所选目录，取消时返回 null。</summary>
    public Func<string?, Task<string?>>? PickFolder { get; set; }
}
