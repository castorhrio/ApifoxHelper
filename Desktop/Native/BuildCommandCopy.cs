namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 定向构建输出对话框里「命令」行的口径（不依赖 WinForms、与测试工程单一编译）。
/// <para>
/// <b>为什么要有这个文件</b>：这一屏的说明行写着「完整输出可在本机终端重新执行查看」——
/// 指着的下一步是「把那条命令拿到终端里再跑一遍」。而命令行此前是 <see cref="Ui.Mono"/> 那个
/// <c>Label</c>：不可选中、不可复制，用户只能照着屏幕一个字一个字敲（命令里带 API 项目的完整路径，
/// 动辄几十个字符）。同一屏的输出尾部早就是可框选的只读框——<b>同一屏里两种行为</b>，
/// 按本仓一贯的判据（第五十五、五十七、五十九轮那条）判定为缺口，不是偏好。
/// </para>
/// <para>
/// <b>与提示对话框、完整错误对话框同一族</b>：那两屏各有一条「说明行指着的下一步在界面上没有落点」
/// 的缺口，分别由 <see cref="NoticeCopy"/>（第一百八十三、一百九十一轮）与
/// <see cref="StatusLineText.CopyFullTextButton"/>（第一百九十七轮）补上。这一屏是同一族里剩下的
/// 最后一处，做法也照抄那一族：<b>两条路并存</b>——整行一键交出走按钮（一次点击、不挑范围），
/// 只挑其中一段走框选（按钮做不到）。
/// </para>
/// <para>
/// <b>交出去的是命令本身，不是屏幕上那一行</b>：屏幕上那一行前面有「命令：」这个标签，
/// 而用户要粘进终端的是标签后面那一段。标签与命令因此分成两个成员（<see cref="Line"/> 拼显示行、
/// <see cref="Target"/> 给要交出去的文字），界面不就地拼、也不就地裁。
/// </para>
/// <para>
/// <b>没有命令时不给按钮</b>：尚未构建、读取失败、记录里没有 <c>command</c> 字段时，
/// 一颗点了什么也不会发生的按钮不该出现（与 <see cref="NoticeCopy.Target"/> 同一判据）。
/// </para>
/// </summary>
public static class BuildCommandCopy
{
    /// <summary>复制按钮的文字（全仓唯一落点：界面不就地拼写）。四个字，与「关闭」同字数。</summary>
    public const string Label = "复制命令";

    /// <summary>命令行的标签前缀（显示行由 <see cref="Line"/> 拼，界面不就地拼）。</summary>
    public const string LinePrefix = "命令：";

    /// <summary>记录里没有命令时显示行里的占位（与构建状态行「—」同一写法）。</summary>
    public const string Missing = "—";

    /// <summary>
    /// 说明行：讲清这块框是什么（输出尾部）、以及「重新执行」这一步在界面上落在哪颗按钮上。
    /// 说明行指着的动作必须在界面上真有落点，否则用户读完还得自己找（与 <see cref="NoticeCopy"/>
    /// 对「手动打开上面的路径」同一纪律）。长度与句数由 <c>CaptionBrevityTests</c> 判。
    /// </summary>
    public const string Note =
        "输出尾部为最近 60 行，敏感值已脱敏；按「复制命令」把命令交给剪贴板，粘到本机终端即可重新执行查看完整输出。";

    /// <summary>
    /// 屏幕上那一行：标签 + 命令。记录里没有命令（或只有空白）时给占位，不留一格空白——
    /// 空白既不是信息也不是缺省，用户看不出是「没跑过」还是「这一格坏了」。
    /// </summary>
    public static string Line(string? command) =>
        LinePrefix + (string.IsNullOrWhiteSpace(command) ? Missing : command);

    /// <summary>
    /// 这一次要交给剪贴板的文字：命令原样返回（不裁剪、不改写、不带「命令：」标签——粘进终端的就是
    /// 屏幕上标签后面那一段）；没有给、或给的是空白时返回 <c>null</c>，此时<b>不出现复制按钮</b>。
    /// </summary>
    public static string? Target(string? command) =>
        string.IsNullOrWhiteSpace(command) ? null : command;
}
