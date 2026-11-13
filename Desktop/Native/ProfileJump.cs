namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「从任务详情跳到它绑定的配置」这一跳的口径（不依赖 WinForms，与测试工程共享编译）。
/// <para>
/// <b>补的是哪个缺口</b>：任务详情顶部那行元信息写着「配置 X · Apifox Y · 在线 · 版本备注 Z」
/// （<see cref="TaskSummary.MetaText"/>），而这一行是<b>只读文字</b>——用户读到「这个任务绑在哪个配置上」，
/// 想改那个配置（换 Swagger 来源、改允许回写范围、补模块前缀）时，界面上没有任何一步能到那里：
/// 只能自己按 Ctrl+1 切到「项目设置」页，再在清单里按名字找那一行。配置一多，这一步就是「先记住名字、
/// 再在另一页找回来」——而任务详情已经把那个名字摆在眼前了。
/// </para>
/// <para>
/// <b>这一份是什么</b>：四件事的唯一落点——按钮文字（<see cref="ButtonLabel"/>）、
/// 「这一跳有没有落点」（<see cref="CanJump"/>）、「跳过去找不到那一行时说什么」（<see cref="MissingStatus"/>）、
/// 「跳过去时配置清单本身没读出来时说什么」（<see cref="LoadFailedStatus"/>）。
/// 界面层不就地拼这几处文字，也不自己判「配置 Id 空不空」。
/// </para>
/// <para>
/// <b>为什么「找不到」要分成两句</b>（第二百六十四轮）：配置页的清单区有三种状态——有行 / 筛到零行 /
/// <b>读取失败</b>（<see cref="ProfileLoadState"/> 口径）。前两种下「清单里没有那一行」确实等于
/// 「这份配置已不存在」；第三种下清单是空的，而空的原因是<b>磁盘上那份清单没读出来</b>，
/// 与「配置被删了」是两件事。用同一句话盖住两种原因，等于把「读不出来」说成「不存在」——
/// 用户会去新建一份配置，而真正该做的是点本页那颗「重试」再读一次。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「该不该跳」——那是产品判断；也不判真机上切页之后那一行滚到哪、
/// 键盘落在哪（随 G-D5 真机复核）。
/// </para>
/// </summary>
public static class ProfileJump
{
    /// <summary>任务详情里那颗按钮的文字（唯一落点）。</summary>
    public const string ButtonLabel = "打开配置";

    /// <summary>
    /// 这一跳有没有落点：配置 Id 为空或全空白时没有。
    /// <para>
    /// 与「宁可复制不到也不猜一格」（<see cref="ListCopy"/>）、「一行不剩则没有落点」（<see cref="ListAdvance"/>）
    /// 同一取舍：认不出该跳到哪一份配置时不跳，也不拿清单第一行顶替——跳错配置比跳不过去更难发现。
    /// </para>
    /// </summary>
    public static bool CanJump(string? profileId) => !string.IsNullOrWhiteSpace(profileId);

    /// <summary>
    /// 跳过去在配置清单里找不到那一行（配置已被删除）时，落在配置页状态行上的那句话：
    /// 说清发生了什么，并给出下一步。
    /// <para>
    /// 页面名取 <see cref="PageNames.Projects"/>——侧栏上那颗按钮上的字与这句话同源，
    /// 改一处不改另一处就会指着一个不存在的页面（第二百五十七轮那一族）。
    /// </para>
    /// <para>
    /// <b>它只管「清单读出来了、里面没有这一行」</b>：清单本身没读出来时走
    /// <see cref="LoadFailedStatus"/>——那时说「已不存在」是把「读不出来」说成「不存在」。
    /// </para>
    /// </summary>
    public static string MissingStatus(string profileName) =>
        $"这个任务绑定的配置「{profileName}」已不存在（可能已被删除）；可在「{PageNames.Projects}」页新建配置后重新创建任务。";

    /// <summary>
    /// 跳过去时配置清单<b>本身没读出来</b>（<see cref="ProfileLoadState"/> 的失败态）时，
    /// 落在配置页状态行上的那句话：说的是「这次没读出来」，不是「已不存在」。
    /// <para>
    /// <b>为什么不能复用 <see cref="MissingStatus"/></b>：读取失败时清单是空的，而空的原因是
    /// 磁盘上那份清单读不出来（目录被占用、权限不足……），不是这份配置被删了。两件事的下一步也不同——
    /// 「已不存在」的下一步是去新建一份配置，「没读出来」的下一步是点本页那颗「重试」再读一次
    /// （那颗按钮此刻就在同一屏的空态里，<see cref="ProfileLoadState.RetryVisible"/> 判它显不显示）。
    /// 说错原因会把用户往「新建一份」那条路上引，而真正该做的是再读一次。
    /// </para>
    /// <para>
    /// 按钮字面取自 <see cref="ProfileLoadState.RetryLabel"/>（全仓只有一处「重试」）：
    /// 这句话指名的入口必须是界面上真实存在的那一个（第四十二轮纪律）。
    /// </para>
    /// </summary>
    public static string LoadFailedStatus(string profileName) =>
        $"这个任务绑定的配置「{profileName}」这次没读出来（配置清单读取失败）；点「{ProfileLoadState.RetryLabel}」再读一次。";
}
