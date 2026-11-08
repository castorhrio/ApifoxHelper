namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 提示对话框的文字口径（不依赖 WinForms，以共享源码编入测试工程在 Linux 断言）。
/// 原生界面里有两处「说一件事、不需要用户做选择」的提示：「关于」（版本、资料目录、运行时）
/// 与「打开目录失败」（原因与路径）。此前两处都用系统 MessageBox（只有一个「确定」）——
/// 全原生界面的信息视图都是令牌化窗体，只有这两处是框架默认观感（DESIGN.md §1.1
/// 「禁止以框架默认观感直接交付」），属同壳不同行为；现统一经 <see cref="NoticeDialog"/> 呈现，
/// MessageBox 只留给确认（DESIGN.md §6「不可逆或破坏性动作保留原生确认对话框」）。
/// 「关于」不再显示本地服务地址：本地 HTTP 服务是过渡壳的实现能力（方案 §2.4），不是用户主产品，
/// 把地址摆在「关于」里等于把用户往浏览器引（方案 1.4「不把用户推回浏览器」）。
/// 文字只在这里拼一份，界面不就地拼写。
/// </summary>
public static class NoticeText
{
    /// <summary>「关于」的标题（与窗口标题、菜单项同一批字）。</summary>
    public const string AboutHeading = "Apifox 助手";

    /// <summary>资料目录那一行的标签：路径本身由调用方给（等宽显示）。</summary>
    public const string AboutDataDirCaption = "资料目录";

    /// <summary>「关于」的说明行：讲清资料目录里有什么、日志目录在哪（下一步在界面上真实存在的菜单里）。</summary>
    public const string AboutNote = "资料目录里保存配置、任务与报告；日志目录见「文件」菜单。";

    /// <summary>打开目录失败的标题：说结果，不说机制。</summary>
    public const string FolderErrorHeading = "无法打开目录";

    /// <summary>打开目录失败的说明行：给出下一步（重试或手动打开），不把用户推回命令行。</summary>
    public const string FolderErrorNote = "可以在「文件」菜单里重试，或手动打开上面的路径。";

    /// <summary>「关于」的正文：版本与运行时各一行（版本在前——报问题时先看的就是它）。</summary>
    public static string AboutBody(string version, string runtime) =>
        "版本 " + version + "\n.NET：" + runtime;

    /// <summary>资料目录行：标签 + 路径（路径按等宽显示，与计数、ID、路径的既有口径一致）。</summary>
    public static string AboutDataDirLine(string root) => AboutDataDirCaption + "：" + root;
}
