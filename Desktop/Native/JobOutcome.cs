namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 作业到达终态之后，状态行该说的那一句（第三百七十八轮，具名 pass：clarify）。
/// <para>
/// <b>补的是哪个缺口</b>：任务页的作业（源码扫描、规则提取、模型生成、传播核对、定向构建、远端检查、
/// 同步执行）跑在后台，进度与结果此前只写在扫描区那一行固定占位的进度标签上
/// （<c>TasksPage.ShowJobState</c>：<c>「{Kind}结束（{State}），正在刷新。」</c> 与
/// <c>「{Kind}失败：{Error} 正在刷新。」</c>）。而作业一结束，<c>OnTick</c> 立刻 <c>LoadTasks()</c>，
/// 那一次刷新把进度标签<b>藏起来</b>——于是「失败原因」在屏上只活了不到一个轮询周期（500ms），
/// 随后连同一整行一起消失。用户按了「开始扫描」、回来只看到进度行没了，屏上没有任何一句话说这一次
/// 是成功、失败还是被取消；失败时更糟：原因没了，只剩源码区那一格状态文字。
/// </para>
/// <para>
/// <b>本文件只放文案与色调</b>（不依赖 WinForms、与测试工程共享编译，真值表在 Linux 真执行）：
/// 给作业的种类、终态与错误原因，答「状态行该说哪一句」与「用哪一种色调」。
/// 触达状态行的那一步收在 <c>TasksPage.ShowJobOutcome</c> 一处——它是「作业结束之后说结果」的唯一落点，
/// 记下作业 Id（<c>ShowJobState</c> 一处）与读回终态（<c>JobRunner.Get</c>）也各只写一次。
/// </para>
/// <para>
/// <b>为什么读回终态而不是在进度标签里留一句</b>：进度标签是「正在做什么」的通道，作业结束之后它就该收起
/// （那一行固定占位，留着等于屏上一直写着「正在刷新」）。结果属于状态行——那一行本来就是「刚才那件事
/// 怎么样了」的通道（读取失败、筛选计数、拒绝原因都在那里）。
/// </para>
/// <para>
/// <b>为什么失败原因原样给出、不补句号</b>：服务层的拒绝文案自带全角句号，而脱敏后的异常消息不一定有；
/// 统一补一次就会出现两个句号，统一去掉又会改掉服务层写好的那一句。原样给出，缺的那一半由
/// 「原因未记录。」这一支兜住（<c>JobRunner</c> 的失败分支一定写 <c>Error</c>，这一支是防御）。
/// </para>
/// </summary>
public static class JobOutcome
{
    /// <summary>
    /// 作业结束之后状态行该说的那一句；还在跑、或状态认不出时返回 <c>null</c>（不猜、不编一句「已完成」）。
    /// <para>
    /// 三种终态各一句：成功说「完成」、失败说「失败：原因」、取消说「已取消」——取消是用户自己的动作，
    /// 结果符合预期，不写成失败（与 <c>AttentionCue</c> 对 canceled 不提醒同一口径）。
    /// </para>
    /// </summary>
    public static string? FinishedText(string kind, string state, string? error) => state switch
    {
        "succeeded" => $"{kind}完成。",
        "failed" => $"{kind}失败：" + (string.IsNullOrWhiteSpace(error) ? "原因未记录。" : error),
        "canceled" => $"{kind}已取消。",
        _ => null,
    };

    /// <summary>
    /// 这一句的语义色调（界面按设计令牌映射为文字颜色）：只有失败是 bad；
    /// 成功与取消都是 muted——成功不需要强调色（强调色只出现在主行动、焦点与品牌资产），
    /// 取消更不是故障。颜色只作辅助信号，含义由文字本身承载（设计验收 D3 双通道）。
    /// </summary>
    public static string Tone(string state) => state == "failed" ? "bad" : "muted";
}
