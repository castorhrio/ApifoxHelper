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
/// <b>这一份是什么</b>：五件事的唯一落点——按钮文字（<see cref="ButtonLabel"/>）、
/// 「这一跳有没有落点」（<see cref="CanJump"/>）、「跳过去找不到那一行时说什么」（<see cref="MissingStatus"/>）、
/// 「跳过去时配置清单本身没读出来时说什么」（<see cref="LoadFailedStatus"/>）、
/// 「落到了那一行时说什么」（<see cref="LandedStatus"/>）。
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
/// <b>为什么「没读出来」之后还要有第三句</b>（第二百六十五轮）：<see cref="LoadFailedStatus"/> 把用户
/// 指到那颗「重试」上，可重试读成功之后，那一跳此前就<b>丢了</b>——清单回来了，用户却还得自己再找一遍
/// 那一行，等于把「先记住名字、再在另一页找回来」这件事又做了一次。现在失败态下这一跳被记住
/// （<c>ProjectsPage</c> 的挂起字段），重试读成功之后落回那一行，并由 <see cref="LandedStatus"/>
/// 在状态行上确认「落到了」——否则用户看不出这一行被选中是「跳过来的」还是「上次留下的」。
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
    /// <para>
    /// <b>「读出来后会定位到它」这半句是承诺，不是修辞</b>（第二百六十五轮）：配置页在失败态下把这一跳
    /// 记下来，那颗「重试」读成功之后落回那一行（见 <see cref="LandedStatus"/>）。这句话里写了什么，
    /// 界面就得真的做到什么——否则它比不说更糟：用户会以为点完重试就回到那一行，然后发现还得自己找。
    /// </para>
    /// </summary>
    public static string LoadFailedStatus(string profileName) =>
        $"这个任务绑定的配置「{profileName}」这次没读出来（配置清单读取失败）；点「{ProfileLoadState.RetryLabel}」再读一次，读出来后会定位到它。";

    /// <summary>
    /// 这一跳真的落到了那一行时，落在配置页状态行上的那句话（第二百六十五轮）。
    /// <para>
    /// <b>为什么要有这一句</b>：落点本身是「那一行被选中、键盘在清单上」，可<b>选中</b>这件事在配置页上
    /// 还有另一个来源——上次留下的选中（<c>RestoreSelection</c>）。两种来源在屏幕上长得一样，
    /// 用户看不出「这一行是我从任务详情跳过来要改的那一份」还是「上次停在这里的那一份」。
    /// 状态行说一句「已定位到配置「X」」，这一跳才算有回执。
    /// </para>
    /// <para>
    /// <b>它不点名按钮</b>：落到那一行之后，本页的「编辑」与「连接与写入探测」都随选中启用
    /// （<c>RefreshDetailActions</c> 那一族），下一步不止一个，指名其中一个反而把另一个藏起来。
    /// 与 <see cref="MissingStatus"/> 那种「只有一个下一步」的情形不同。
    /// </para>
    /// </summary>
    public static string LandedStatus(string profileName) => $"已定位到配置「{profileName}」。";

    /// <summary>
    /// 跳过去时<b>要不要清空筛选框</b>（第二百六十七轮）：先在当前筛选下找那一行，找到了就不动筛选框。
    /// <para>
    /// <b>补的是哪个缺口</b>：第二百六十三轮把「那一行可能正被筛选框筛掉」接成了「先清空筛选，再找一次」，
    /// 但清空的条件写成了「筛选框里有词」——于是<b>那一行本来就在筛选结果里</b>时，筛选框照样被清空。
    /// 用户敲了一个词、从任务详情跳过来改那一份配置，回来发现筛选没了、清单变回全部：
    /// 他付了一笔代价（重新敲一遍关键词），而这一跳根本不需要他付。
    /// </para>
    /// <para>
    /// <b>规则</b>：清空筛选是「那一行不在屏上」时才付的代价——<paramref name="filterActive"/> 为假
    /// （本来就没筛）或 <paramref name="foundUnderFilter"/> 为真（那一行就在筛选结果里）时都不清。
    /// 两个入参都是调用方当场算出来的事实，不是「大概」：筛选有没有生效由
    /// <see cref="ListFilter.IsActive"/> 判，那一行在不在屏上由第一遍查找的结果给。
    /// </para>
    /// <para>
    /// <b>为什么这条规则要单独落一处</b>：它是「跳过去」这件事里唯一一处会<b>改用户输入</b>的动作
    /// （筛选框里的词是用户敲的），而它此前只活在 <c>ProjectsPage.RevealProfile</c> 的一句 <c>if</c> 里。
    /// 落成纯函数之后，四种组合（筛没筛 × 找没找到）在 Linux 上就能逐格判，不必等真机。
    /// </para>
    /// </summary>
    public static bool ShouldClearFilter(bool filterActive, bool foundUnderFilter) =>
        filterActive && !foundUnderFilter;
}
