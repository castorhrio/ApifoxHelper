namespace ApifoxHelper.Desktop.Native;

using ApifoxHelper.Tasks;

/// <summary>
/// 任务详情顶部两行元信息的口径（docs/DESIGN.md §6「任务详情元信息」）：
/// 第一行讲这个任务绑在什么配置与目标上、代码版本是什么，第二行讲它检查哪些接口。
/// 两行此前都在 <c>TasksPage</c> 里就地拼字符串，于是各有一处说错话的地方：
/// <list type="bullet">
/// <item>范围行只按「模块 + 路径前缀」拼，<c>EndpointKeys</c>（第二十八轮补的第三个范围维度，
/// 新建任务对话框的「指定接口」）完全不在其中——一个只用指定接口圈定范围的任务显示成
/// 「范围：API 项目全部接口」，把「只检查这几个接口」说成「检查全部接口」。
/// 这与 <see cref="TaskStore.InScope"/> 的真实判定相悖：只有路径前缀与指定接口都为空才是全部接口。
/// 说错话不是偏好问题——用户据此判断这次扫描覆盖了什么，报告与同步范围都跟着它理解。</item>
/// <item>版本备注（<c>SourceVersionNote</c>，第二十八轮补的任务级输入，报告里「未绑定代码版本」
/// 那句话的数据来源）在原生任务详情里从不显示：填了看不到，报告里出现的那句话在界面上找不到出处。</item>
/// </list>
/// 现收成这一份口径（不依赖 WinForms、与测试工程共享编译，真值表在 Linux 真执行）：
/// 范围行按三个维度如实列出（模块、路径前缀、指定接口个数），**「全部接口」的条件与
/// <see cref="TaskStore.InScope"/> 同一份**——路径前缀与指定接口都为空；模块名只是选范围时用的名字
/// （<see cref="TaskStore.Create"/> 把它的前缀展开进 <c>PathPrefixes</c>），一个没有配前缀的模块
/// 不缩小任何范围，此时如实说「API 项目全部接口」，不拿模块名冒充范围；
/// 元信息行补上版本备注，留空时与报告同一句话「未绑定代码版本」。
/// 只改这两行文字——不改任务数据、不改 <see cref="TaskStore.InScope"/> 的范围判定、
/// 不改扫描与同步的任何口径，服务层一行未动。
/// </summary>
public static class TaskSummary
{
    /// <summary>范围行的前缀（这一行讲的是「检查哪些接口」）。</summary>
    public const string RangePrefix = "范围：";

    /// <summary>三个范围维度都为空时的那句话——与 <see cref="TaskStore.InScope"/> 的「全部接口」同一条件。</summary>
    public const string AllEndpointsLabel = "API 项目全部接口";

    /// <summary>版本备注留空时的那句话：与报告里「未绑定代码版本」逐字一致，界面与报告不各说一套。</summary>
    public const string NoVersionNoteLabel = "未绑定代码版本";

    /// <summary>Apifox 项目未绑定时的占位（与配置页、任务列表同一取值）。</summary>
    public const string UnboundProjectLabel = "未绑定";

    public const string ModuleLabel = "模块";
    public const string PrefixLabel = "路径前缀";
    public const string EndpointLabel = "指定接口";

    /// <summary>各维度之间的分隔（与元信息行同一份，界面不就地拼分隔符）。</summary>
    public const string PartSeparator = " · ";

    /// <summary>
    /// 元信息行：配置、Apifox 目标、在线/离线、版本备注。
    /// 版本备注排在最后——它讲的是「这次检查的是哪一版代码」，与前三项（绑在什么配置与目标上）分开读。
    /// </summary>
    public static string MetaText(CheckTask task) =>
        $"配置 {task.ProfileSnapshot.Name}{PartSeparator}Apifox {(task.ProfileSnapshot.Apifox.ProjectId ?? UnboundProjectLabel)}" +
        $"{PartSeparator}{(task.OfflineMode ? "离线模式" : "在线")}{PartSeparator}版本备注 {VersionNote(task)}";

    /// <summary>
    /// 范围行：按三个维度如实列出，三者皆空才说「API 项目全部接口」。
    /// 指定接口只给个数——这一行是概览，逐个列出会把两行文字撑成一段清单；
    /// 具体是哪几个接口在任务数据里，扫描结果与检查结果按同一份范围判定呈现。
    /// </summary>
    public static string RangeText(CheckTask task) => RangePrefix + string.Join(PartSeparator, RangeParts(task));

    /// <summary>
    /// 范围行的各段（顺序固定：模块、路径前缀、指定接口）。
    /// 模块与前缀会同时出现——<see cref="TaskStore.Create"/> 把模块映射展开进路径前缀，
    /// 前缀才是真正参与匹配的那一份，模块名是用户选范围时用的名字，两者都如实列出。
    /// **「全部接口」只看路径前缀与指定接口**（与 <see cref="TaskStore.InScope"/> 同一条件）：
    /// 两者都为空时范围就是全部接口，此时不列模块名——列了等于把「检查全部接口」说成「只检查这个模块」。
    /// </summary>
    public static IReadOnlyList<string> RangeParts(CheckTask task)
    {
        if (task.PathPrefixes.Count == 0 && task.EndpointKeys.Count == 0) return [AllEndpointsLabel];
        var parts = new List<string>();
        if (task.ModuleNames.Count > 0) parts.Add(ModuleLabel + " " + string.Join("、", task.ModuleNames));
        if (task.PathPrefixes.Count > 0) parts.Add(PrefixLabel + " " + string.Join(" ", task.PathPrefixes));
        if (task.EndpointKeys.Count > 0) parts.Add(EndpointLabel + " " + task.EndpointKeys.Count + " 个");
        return parts;
    }

    /// <summary>
    /// 版本备注：去首尾空白；留空（含全空白）时给「未绑定代码版本」。
    /// 与 <see cref="TaskStore.Create"/> 的落盘口径同一件事——那边把空白交回配置级备注，
    /// 到这里仍为空就是真的没有绑定版本，报告与界面说同一句话。
    /// </summary>
    public static string VersionNote(CheckTask task) =>
        string.IsNullOrWhiteSpace(task.SourceVersionNote) ? NoVersionNoteLabel : task.SourceVersionNote.Trim();
}
