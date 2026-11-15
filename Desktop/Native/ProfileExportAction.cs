using ApifoxHelper.Profiles;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「导出配置（不含凭据）」那颗按钮的口径（第三百三十一轮，具名 pass：clarify）。
/// <para>
/// <b>补的是哪个缺口</b>：这颗按钮从第一百四十八轮起就在「高级设置」那一行里，接线也一直在
/// （<c>ProjectsPage.ProfileDialog.ExportProfile</c>），但<b>它导出的是哪一份配置</b>这件事
/// 从来没有被任何断言或冒烟段钉住过——<c>ProfileAnalysisTests</c> 判的是
/// <see cref="ProfileExport"/> 自己的文件名与字节，判不到「按钮点下去取的是磁盘上那一份，
/// 还是屏幕上正在编辑的那一份」。两种写法在源码上只差一个变量名，在用户那里差一整次导出：
/// 编辑到一半点「导出配置」，拿到的可能是屏幕上还没保存的草稿，也可能是上一次保存的版本，
/// 而状态行只说「配置已导出」——用户无从分辨。
/// </para>
/// <para>
/// <b>为什么口径是「导出已保存的那一份」</b>：导出与 REST 下载（<c>/api/profiles/{id}/export</c>）
/// 共用同一份字节（<see cref="ProfileExport"/>），而 REST 那一头读的必然是磁盘上的配置；
/// 原生这一头若导出屏幕上的草稿，同一颗按钮与同一个 URL 就会给出两份不同的文件。
/// 所以按钮取磁盘那一份，<b>并且把这件事说出来</b>：屏幕上有未保存改动时，状态行明说
/// 「屏幕上的改动还没保存，不在其中」，而不是让用户自己猜。
/// </para>
/// <para>
/// <b>为什么没有冒烟段</b>：这条路径中间隔着一个原生 <c>SaveFileDialog</c>（模态、系统对话框），
/// 冒烟器点不到它——<c>RunProfileFormLayout</c> 量的是表单行高，量不到「另存为之后写了什么字节」。
/// 所以这一条的可核对面只有两半：本文件（判定与文案，Linux 上真执行）与
/// <c>ProfileExportActionTests</c> 里那条对账门（判 <c>ProjectsPage</c> 的导出路径真的经过这里）。
/// 真机上「另存为对话框长什么样、默认文件名对不对」仍随 G-D5。
/// </para>
/// <para>
/// 本文件只放判定与文案，不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </para>
/// </summary>
public static class ProfileExportAction
{
    /// <summary>
    /// 屏幕上的配置与磁盘上那一份是否不同（= 有未保存的改动）。
    /// <para>
    /// 判法是<b>比导出的字节</b>而不是逐字段比：导出字节就是用户拿到的那份文件，
    /// 「文件会不会不一样」与「字段有没有不一样」在这里是同一个问题；逐字段比还得跟着
    /// <see cref="ProjectProfile"/> 的字段增删改一遍，改漏一处就静默判成「没有改动」。
    /// </para>
    /// </summary>
    public static bool HasUnsavedEdits(ProjectProfile onScreen, ProjectProfile saved) =>
        !ProfileExport.Render(onScreen).AsSpan().SequenceEqual(ProfileExport.Render(saved));

    /// <summary>
    /// 导出这一颗按钮要写什么、叫什么名字、写完说什么——三件事一处定，页面只负责弹另存为与写盘。
    /// <para>
    /// <paramref name="saved"/> 必须是<b>磁盘上</b>那一份（<c>ProfileStore.Get</c> 读回来的），
    /// <paramref name="onScreen"/> 是对话框里正在编辑的那一份；两者只用来判「有没有未保存改动」，
    /// <b>写出去的字节永远取自 <paramref name="saved"/></b>。
    /// </para>
    /// </summary>
    public static ExportPlan Plan(ProjectProfile saved, ProjectProfile onScreen) =>
        new(
            ProfileExport.FileName(saved),
            ProfileExport.Render(saved),
            StatusText(saved.Name, HasUnsavedEdits(onScreen, saved)));

    /// <summary>
    /// 导出成功后的状态行。两句都点名导出的<b>是哪一份</b>（配置名 + 「已保存版本」），
    /// 有未保存改动时再补一句它不在其中——状态行是用户唯一能核对「我拿到的是哪一版」的地方。
    /// </summary>
    public static string StatusText(string name, bool hasUnsavedEdits) =>
        hasUnsavedEdits
            ? $"已导出「{name}」的已保存版本（不含模型服务密钥）；屏幕上的改动还没保存，不在其中。"
            : $"已导出「{name}」的已保存版本（不含模型服务密钥）。";

    /// <summary>导出这一颗按钮的落点：文件名、字节、写完那一句（三件事同源，页面不再自己拼）。</summary>
    public readonly record struct ExportPlan(string FileName, byte[] Bytes, string StatusText);
}
