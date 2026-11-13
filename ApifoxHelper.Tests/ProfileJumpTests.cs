using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「从任务详情跳到它绑定的配置」这一跳（第二百六十三轮，具名 pass：clarify + harden）。
/// <para>
/// <b>补的是哪个缺口</b>：任务详情顶部那行元信息写着「配置 X · Apifox Y · 在线 · 版本备注 Z」
/// （<see cref="TaskSummary.MetaText"/>），而这一行是<b>只读文字</b>——用户读到「这个任务绑在哪个配置上」，
/// 想改那个配置（换 Swagger 来源、改允许回写范围、补模块前缀）时，界面上没有任何一步能到那里：
/// 只能自己按 Ctrl+1 切到「项目设置」页，再在清单里按名字找那一行。配置一多，这一步就是
/// 「先记住名字、再在另一页找回来」，而任务详情已经把那个名字摆在眼前了。
/// </para>
/// <para>
/// <b>这一族判什么</b>：① 五件事的唯一落点（按钮文字 / 有没有落点 / 找不到时说什么 /
/// 清单没读出来时说什么 / 落到了那一行时说什么）都在 <see cref="ProfileJump"/>，界面层不就地拼；
/// ② 三处接线各在它该在的地方（任务详情摆按钮并发请求、壳把请求接到另一页、配置页按 Id 选中那一行）；
/// ③ 按钮的可用状态只有一个落点（详情区动作的单一门控）；④ 判据自己被抓得住（注入各报一处）。
/// </para>
/// <para>
/// <b>第二百六十四轮补的那一条</b>：「找不到那一行」有两种原因——清单读出来了、里面没有这一行
/// （配置已被删除），与清单<b>本身没读出来</b>（<see cref="ProfileLoadState"/> 的失败态，清单是空的）。
/// 第二百六十三轮把两种原因盖在同一句话上，用户读到「已不存在」会去新建一份配置，而真正该做的是
/// 点同一屏那颗「重试」再读一次。现在两句话各说各的原因、各给各的下一步，各有一条真值表。
/// </para>
/// <para>
/// <b>第二百六十五轮补的那一条</b>：<see cref="ProfileJump.LoadFailedStatus"/> 把用户指到那颗「重试」上，
/// 可重试读成功之后那一跳此前就<b>丢了</b>——清单回来了，用户还得自己再找一遍那一行，等于把
/// 「先记住名字、再在另一页找回来」这件事又做了一次。现在失败态下这一跳被记住（<c>ProjectsPage</c> 的
/// <c>_pendingReveal</c>），那颗「重试」读成功之后落回那一行，并由 <see cref="ProfileJump.LandedStatus"/>
/// 在状态行上给一句回执——否则用户看不出这一行被选中是「跳过来的」还是「上次留下的」。
/// 这一条判的是「那句话里的承诺 ↔ 界面上真的做到」：赋值一处、清空一处、先捕获再读、读成功后落回。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「该不该跳」——那是产品判断；也不判真机上切页之后那一行滚到哪、键盘落在哪
/// （随 G-D5 真机复核）。本仓没有 Windows 运行时，这一跳的<b>点击</b>本身在 Linux 上跑不起来，
/// 所以这里判的是「三处接线都在、且都走同一份口径」，不是「点下去真的换了页」——**那半截由
/// <c>ApifoxHelper.Smoke</c> 的「从任务详情跳过来时清单没读出来，重试读成功后落回那一行」那一段
/// 在 wine 里用真实控件跑**（模拟器，不是真机）。
/// </para>
/// </summary>
public class ProfileJumpTests
{
    private static string TasksPage => Repo.Source("Desktop", "Native", "TasksPage.cs");
    private static string ProjectsPage => Repo.Source("Desktop", "Native", "ProjectsPage.cs");
    private static string Shell => Repo.Source("Desktop", "Native", "NativeShellForm.cs");
    private static string JumpSource => Repo.Source("Desktop", "Native", "ProfileJump.cs");

    // ── 口径本身 ─────────────────────────────────────────────────────

    /// <summary>
    /// 按钮文字：非空、不带句末标点、不点名页面（它是一颗按钮，不是一句指路文案）。
    /// </summary>
    [Fact]
    public void ButtonLabel_IsAShortActionWithoutPunctuation()
    {
        Assert.False(string.IsNullOrWhiteSpace(ProfileJump.ButtonLabel));
        Assert.Equal(ProfileJump.ButtonLabel, ProfileJump.ButtonLabel.Trim());
        Assert.DoesNotContain("。", ProfileJump.ButtonLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("「", ProfileJump.ButtonLabel, StringComparison.Ordinal);
        Assert.DoesNotContain(PageNames.Projects, ProfileJump.ButtonLabel, StringComparison.Ordinal);
        Assert.DoesNotContain(PageNames.Tasks, ProfileJump.ButtonLabel, StringComparison.Ordinal);
    }

    /// <summary>
    /// 有没有落点：配置 Id 为空或全空白时没有——认不出该跳到哪一份配置时不跳，也不拿清单第一行顶替。
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("\t\n", false)]
    [InlineData("p-1", true)]
    [InlineData("  p-1  ", true)]
    public void CanJump_OnlyWhenTheProfileIdIsKnown(string? profileId, bool expected) =>
        Assert.Equal(expected, ProfileJump.CanJump(profileId));

    /// <summary>
    /// 找不到那一行时说的那句话：点名是哪一个配置、说清发生了什么、给出下一步，且页面名取自
    /// <see cref="PageNames"/>（改一处不改另一处就会指着一个不存在的页面——第二百五十七轮那一族）。
    /// </summary>
    [Fact]
    public void MissingStatus_NamesTheProfileAndTheNextStep()
    {
        var text = ProfileJump.MissingStatus("示例配置");

        Assert.Contains("示例配置", text, StringComparison.Ordinal);
        Assert.Contains("已不存在", text, StringComparison.Ordinal);
        Assert.Contains(PageNames.Projects, text, StringComparison.Ordinal);
        // 下一步要可执行：说清「在哪一页做什么」，不是一句「请检查配置」。
        Assert.Contains("新建配置", text, StringComparison.Ordinal);
        Assert.EndsWith("。", text, StringComparison.Ordinal);
        // 名字换了，那句话跟着换——不是一句写死的文案。
        Assert.NotEqual(text, ProfileJump.MissingStatus("另一个配置"));
    }

    /// <summary>
    /// 清单<b>本身没读出来</b>时说的那句话（第二百六十四轮）：说的是「这次没读出来」，不是「已不存在」，
    /// 且下一步指向同一屏那颗「重试」。
    /// <para>
    /// <b>补的是哪个缺口</b>：配置页的清单区有三种状态——有行 / 筛到零行 / 读取失败
    /// （<see cref="ProfileLoadState"/> 口径）。读取失败时清单是空的，而空的原因是磁盘上那份清单读不出来，
    /// 不是这份配置被删了。第二百六十三轮把两种原因盖在同一句话上，用户读到「已不存在（可能已被删除）」
    /// 会去新建一份配置，而真正该做的是点本页那颗「重试」再读一次——**说错原因就是把用户往另一条路上引**。
    /// </para>
    /// <para>
    /// 判的是两句话<b>各说各的原因、各给各的下一步</b>：这一条与
    /// <see cref="MissingStatus_NamesTheProfileAndTheNextStep"/> 一起构成真值表——
    /// 只判「新那句话里有『没读出来』」是不够的，两句话若退化成同一句，两条断言里必有一条红。
    /// </para>
    /// </summary>
    [Fact]
    public void LoadFailedStatus_SaysItCouldNotBeReadNotThatItIsGone()
    {
        var text = ProfileJump.LoadFailedStatus("示例配置");

        // ① 点名是哪一个配置（与 MissingStatus 同一取舍：用户要知道是哪一份）。
        Assert.Contains("示例配置", text, StringComparison.Ordinal);
        // ② 说的是「没读出来」，不是「不存在」——这是这一条存在的全部理由。
        Assert.Contains("没读出来", text, StringComparison.Ordinal);
        Assert.Contains("读取失败", text, StringComparison.Ordinal);
        Assert.DoesNotContain("已不存在", text, StringComparison.Ordinal);
        Assert.DoesNotContain("已被删除", text, StringComparison.Ordinal);
        // ③ 下一步是同一屏那颗「重试」，字面取自 ProfileLoadState（全仓只有一处「重试」）。
        Assert.Contains(ProfileLoadState.RetryLabel, text, StringComparison.Ordinal);
        Assert.EndsWith("。", text, StringComparison.Ordinal);
        // ④ 名字换了，那句话跟着换——不是一句写死的文案。
        Assert.NotEqual(text, ProfileJump.LoadFailedStatus("另一个配置"));
        // ⑤ 两句话不许退化成同一句：原因不同，下一步也不同。
        Assert.NotEqual(ProfileJump.MissingStatus("示例配置"), text);
    }

    /// <summary>
    /// 落到那一行时的回执（第二百六十五轮）：点名是哪一个配置、是一句陈述而不是指路、不点名按钮。
    /// <para>
    /// <b>为什么要有这一句</b>：落点本身是「那一行被选中、键盘在清单上」，可<b>选中</b>在配置页上还有
    /// 另一个来源——上次留下的选中（<c>RestoreSelection</c>）。两种来源在屏幕上长得一样，用户看不出
    /// 「这一行是我从任务详情跳过来要改的那一份」还是「上次停在这里的那一份」。
    /// </para>
    /// <para>
    /// <b>它不点名按钮</b>：落到那一行之后「编辑」与「连接与写入探测」都随选中启用，下一步不止一个，
    /// 指名其中一个反而把另一个藏起来——与 <see cref="ProfileJump.MissingStatus"/> 那种「只有一个下一步」
    /// 的情形不同。这一条与那两条一起构成真值表：三句话若退化成同一句，必有一条红。
    /// </para>
    /// </summary>
    [Fact]
    public void LandedStatus_ConfirmsTheLandingWithoutNamingAButton()
    {
        var text = ProfileJump.LandedStatus("示例配置");

        // ① 点名是哪一个配置（用户要知道落到了哪一份上）。
        Assert.Contains("示例配置", text, StringComparison.Ordinal);
        // ② 说的是「落到了」，不是「找不到」也不是「没读出来」。
        Assert.Contains("已定位到", text, StringComparison.Ordinal);
        Assert.DoesNotContain("已不存在", text, StringComparison.Ordinal);
        Assert.DoesNotContain("没读出来", text, StringComparison.Ordinal);
        Assert.EndsWith("。", text, StringComparison.Ordinal);
        // ③ 不点名按钮：落到那一行之后下一步不止一个（判据本体在 LandingProblems，下面那条注入验它抓得住）。
        Assert.Empty(LandingProblems(text, "示例配置"));
        // ④ 名字换了，那句话跟着换——不是一句写死的文案。
        Assert.NotEqual(text, ProfileJump.LandedStatus("另一个配置"));
        // ⑤ 三句话不许退化成同一句：三种情形、三个下一步。
        Assert.NotEqual(ProfileJump.MissingStatus("示例配置"), text);
        Assert.NotEqual(ProfileJump.LoadFailedStatus("示例配置"), text);
    }

    /// <summary>
    /// 「落到那一行」那句话<b>不点名按钮</b>的判据本体（第二百六十六轮，简报 P0-2）。
    /// <para>
    /// <b>换掉了什么</b>：此前判的是「除了配置名，句里不该再有「」」——它想判「不点名按钮」，
    /// 判法却是数引号。配置名哪天改用别的引号、或名字本身带引号字符，这条断言会以<b>错误的方式</b>
    /// 红或绿：红的时候看不出是文案错了还是判据脆，绿的时候可能句子里正指着一颗按钮。
    /// </para>
    /// <para>
    /// <b>现在判什么</b>：① 句子里不含配置页上任何一颗按钮的字面（「重试」取自
    /// <see cref="ProfileLoadState.RetryLabel"/>，其余三颗是配置页工具栏与选中态上的按钮）；
    /// ② 除配置名那一对「」之外，句子里不再有别的「」——这一条是<b>兜底</b>：将来新增一颗按钮、
    /// 名字没进上面那张表，只要它被「」括起来指路，这里仍会红。
    /// </para>
    /// <para>
    /// <b>为什么「不点名按钮」值得一条判据</b>：落到那一行之后「编辑」与「连接与写入探测」都随选中启用，
    /// 下一步不止一个；指名其中一个反而把另一个藏起来。这与 <see cref="ProfileJump.MissingStatus"/>
    /// 那种「只有一个下一步」的情形不同——那两句必须点名，这一句必须不点名。
    /// </para>
    /// </summary>
    private static List<string> LandingProblems(string text, string profileName)
    {
        var problems = new List<string>();
        // ① 配置页上真实存在的按钮字面（「重试」走常量，其余三颗是工具栏与选中态上的按钮）。
        foreach (var label in new[] { ProfileLoadState.RetryLabel, "编辑", "连接与写入探测", "新建配置" })
        {
            if (text.Contains(label, StringComparison.Ordinal))
            {
                problems.Add($"「{text}」点名了按钮「{label}」：落到那一行之后下一步不止一个，指名其中一个会把另一个藏起来。");
            }
        }

        // ② 除配置名那一对「」之外不许再有别的引号括起的内容（兜底：新增按钮没进上面那张表也抓得住）。
        var stripped = text.Replace($"「{profileName}」", "", StringComparison.Ordinal);
        if (stripped.Contains('「', StringComparison.Ordinal) || stripped.Contains('」', StringComparison.Ordinal))
        {
            problems.Add($"「{text}」里除配置名「{profileName}」之外还有别的「」：那句话是一句陈述，不该再指别的东西。");
        }

        return problems;
    }

    /// <summary>
    /// 上面那条判据<b>自己</b>被抓得住（第二百六十六轮，简报 P0-2 的假注入自验）。
    /// <para>
    /// 本仓第二百零九轮的教训是「判据自己没被跑过」比「没有判据」更贵：一条永远为真的断言
    /// 与没有断言在报告里长得一样。这里拿四发注入各喂一次——点名「重试」、点名「编辑」、
    /// 用「」指一个不在表里的按钮、以及合规的那一句（不报）。
    /// </para>
    /// </summary>
    [Fact]
    public void LandingDetector_ReportsTheBreaksItMustCatch()
    {
        // ① 点名「重试」：那句话把用户往一颗按钮上引。
        Assert.Contains(LandingProblems("已定位到配置「示例配置」，点「重试」再试一次。", "示例配置"),
            p => p.Contains(ProfileLoadState.RetryLabel, StringComparison.Ordinal));
        // ② 点名「编辑」：不带引号也算点名。
        Assert.Contains(LandingProblems("已定位到配置「示例配置」，可直接编辑。", "示例配置"),
            p => p.Contains("编辑", StringComparison.Ordinal));
        // ③ 用「」指一个不在表里的按钮：兜底那一条抓得住。
        Assert.Contains(LandingProblems("已定位到配置「示例配置」，可点「导出」继续。", "示例配置"),
            p => p.Contains("别的「」", StringComparison.Ordinal));
        // ④ 合规的那一句：一处都不报。
        Assert.Empty(LandingProblems(ProfileJump.LandedStatus("示例配置"), "示例配置"));
    }

    /// <summary>
    /// 「读出来后会定位到它」这半句是<b>承诺</b>，不是修辞（第二百六十五轮）：那句话里写了什么，
    /// 界面就得真的做到什么——否则它比不说更糟（用户以为点完重试就回到那一行，然后发现还得自己找）。
    /// <para>
    /// 判的是「承诺 ↔ 接线」两侧：① 那句话里确实有这个承诺；② 配置页在读取失败态下真的记住了这一跳；
    /// ③ 那颗「重试」读成功之后真的落回那一行；④ 落回去之后真的给回执。
    /// 只判其中一侧都会让另一侧静默失效：删掉接线而留着那句话，用户读到的是一句空话。
    /// </para>
    /// </summary>
    [Fact]
    public void LoadFailedStatus_PromiseIsBackedByThePendingJump()
    {
        // ① 那句话里写了这个承诺。
        Assert.Contains("定位到", ProfileJump.LoadFailedStatus("示例配置"), StringComparison.Ordinal);

        // ② 配置页在读取失败态那一支里记住这一跳（别处记住等于替用户跳）。
        var reveal = SourceSlice.BodyOf(ProjectsPage, "public bool RevealProfile(string profileId, string profileName)");
        const string remember = "_pendingReveal = (profileId, profileName);";
        Assert.Contains(remember, reveal, StringComparison.Ordinal);
        Assert.True(
            reveal.IndexOf("if (_loadFailed)", StringComparison.Ordinal) < reveal.IndexOf(remember, StringComparison.Ordinal)
            && reveal.IndexOf(remember, StringComparison.Ordinal) < reveal.IndexOf("ProfileJump.LoadFailedStatus(", StringComparison.Ordinal),
            "记住这一跳必须落在读取失败态那一支里（别处记住等于替用户跳）。");

        // ③ 那颗「重试」读成功之后落回那一行：先捕获再读，读成功后走同一个 RevealProfile 入口。
        var retry = SourceSlice.BodyOf(ProjectsPage, "private void RetryLoad()");
        Assert.True(
            retry.IndexOf("var pending = _pendingReveal;", StringComparison.Ordinal) < retry.IndexOf("LoadProfiles();", StringComparison.Ordinal),
            "必须先捕获再读：读成功那一支会把挂起的那一跳清掉，捕获的那一份才是用户点重试之前想找的那一份。");
        Assert.Contains("if (_loadFailed || pending is not { } target) return;", retry, StringComparison.Ordinal);
        Assert.Contains("RevealProfile(target.Id, target.Name);", retry, StringComparison.Ordinal);
        // 不另写一份选中逻辑：落回那一行走的是同一个入口。
        Assert.DoesNotContain("Ui.SelectItem(", retry, StringComparison.Ordinal);
        // 那颗「重试」按钮点下去走的就是这一条路（不是直接再读一次）。
        Assert.Contains("_emptyRetry.Click += (_, _) => RetryLoad();", ProjectsPage, StringComparison.Ordinal);

        // ④ 落回去之后给回执（否则用户看不出这一行是「跳过来的」还是「上次留下的」）。
        Assert.Contains("ProfileJump.LandedStatus(profileName)", reveal, StringComparison.Ordinal);
    }

    /// <summary>
    /// 挂起的那一跳：<b>赋值一处、清空一处</b>（第二百六十五轮）。
    /// <para>
    /// 赋值只有一处（读取失败态那一支），清空只有一处（<c>LoadProfiles</c> 读成功那一支）——
    /// 读成功即意味着这一跳要么正被「重试」那条路消费（它在调用前先捕获），要么用户是从别处读到的
    /// （切页、刷新、保存返回）。第二处赋值会让「谁记住的」说不清；第二处清空会让「重试读成功后
    /// 落不回去」变成一条只在某些路径上出现的 bug。
    /// </para>
    /// </summary>
    [Fact]
    public void PendingJump_HasOneAssignmentAndOneClear()
    {
        Assert.Single(Regex.Matches(ProjectsPage, @"_pendingReveal = \(profileId, profileName\);"));
        Assert.Single(Regex.Matches(ProjectsPage, @"_pendingReveal = null;"));

        // 清空落在「读成功」之后：失败态那一支不许清——那一跳还要留给下一次重试。
        var load = SourceSlice.BodyOf(ProjectsPage, "private void LoadProfiles()");
        Assert.Contains("_pendingReveal = null;", load, StringComparison.Ordinal);
        Assert.True(
            load.IndexOf("_loadFailed = false;", StringComparison.Ordinal) < load.IndexOf("_pendingReveal = null;", StringComparison.Ordinal),
            "清空必须落在「读成功」之后（失败态那一支不许清——那一跳还要留给下一次重试）。");
        // 失败态那一支（ShowLoadFailure）不碰它。
        var failure = SourceSlice.BodyOf(ProjectsPage, "private void ShowLoadFailure(string status)");
        Assert.DoesNotContain("_pendingReveal", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// 「跳过去时要不要清空筛选」的四格真值表（第二百六十七轮）。
    /// <para>
    /// <b>补的是哪个缺口</b>：第二百六十三轮把「那一行可能正被筛选框筛掉」接成了「先清空筛选，再找一次」，
    /// 但清空的条件写成了「筛选框里有词」——于是<b>那一行本来就在筛选结果里</b>时筛选框照样被清空。
    /// 用户敲了一个词、从任务详情跳过来改那一份配置，回来发现筛选没了、清单变回全部：白付一笔。
    /// </para>
    /// <para>
    /// 判的是四格：筛了且没找到 → 清（唯一该清的那一格）；筛了且找到了 → 不清；没筛且没找到 → 不清
    /// （本来就没筛，清空是空动作）；没筛且找到了 → 不清。四格里只有一格为真——把条件写成
    /// 「筛选框里有词」时第二格会变成「清」，这一条当场红。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void ShouldClearFilter_OnlyWhenTheRowIsHiddenByTheFilter(bool filterActive, bool foundUnderFilter, bool expected) =>
        Assert.Equal(expected, ProfileJump.ShouldClearFilter(filterActive, foundUnderFilter));

    /// <summary>
    /// 两遍查找的形状（第二百六十七轮）：<b>先找、再（按需）清、再找</b>——顺序是判据。
    /// <para>
    /// 反过来（先清再找）就是第二百六十三轮那个形状：那一行本来就在筛选结果里时筛选框照样被清空。
    /// 只判「用了 ShouldClearFilter」不够——把它挪到第一遍查找之前，条件里的 <c>found</c> 就永远是
    /// 初始值，四格真值表照样绿而行为已经退回去。所以这里判的是三件事的先后：
    /// 第一遍查找 → ShouldClearFilter → ClearListFilter → 第二遍查找。
    /// </para>
    /// </summary>
    [Fact]
    public void RevealProfile_SearchesBeforeItTouchesTheFilter()
    {
        var body = SourceSlice.BodyOf(ProjectsPage, "public bool RevealProfile(string profileId, string profileName)");
        var firstSearch = body.IndexOf("var found = SelectRow(profileId);", StringComparison.Ordinal);
        var decide = body.IndexOf("ProfileJump.ShouldClearFilter(", StringComparison.Ordinal);
        var clear = body.IndexOf("ClearListFilter();", StringComparison.Ordinal);
        // 从清空那一处往后找：第一遍那一行是「var found = SelectRow(profileId);」，它的子串也含这一句。
        var secondSearch = clear < 0 ? -1 : body.IndexOf("found = SelectRow(profileId);", clear, StringComparison.Ordinal);

        Assert.True(firstSearch >= 0, "第一遍查找不见了：那一行本来就在筛选结果里时，筛选框会被无条件清空。");
        Assert.True(decide > firstSearch, "「要不要清筛选」必须在第一遍查找之后判——否则条件里的 found 永远是初始值。");
        Assert.True(clear > decide, "清空筛选必须落在 ShouldClearFilter 那一支里。");
        Assert.True(secondSearch > clear, "清空筛选之后必须再找一次，否则那一跳落不下去。");
        // 清空筛选只有一处（唯一路径）：多一处就是「两条路各清一次」。
        Assert.Equal(1, Regex.Matches(body, @"ClearListFilter\(\);").Count);
    }

    /// <summary>
    /// 「落到了那一行」的回执<b>真的接在「找到了」那一支里</b>（第二百六十八轮，简报 P1-2）。
    /// <para>
    /// <b>补的是哪个缺口</b>：<see cref="ProfileJump.LoadFailedStatus"/> 那句「读出来后会定位到它」
    /// 已有 <see cref="LoadFailedStatus_PromiseIsBackedByThePendingJump"/> 判住接线，而
    /// <see cref="ProfileJump.LandedStatus"/> 是同一件事的回执半截——此前只判了「那句话的文案对不对」
    /// 与「<c>RevealProfile</c> 里出现过 <c>ProfileJump.LandedStatus(</c>」。
    /// <b>「出现过」不等于「接在那一支里」</b>：把它挪到「找不到」那一支之后、或挪出 <c>if (found)</c>，
    /// 上面那两条断言照样绿，而用户会在找不到那一行时读到「已定位到配置「X」」。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 那一句是 <c>SetStatus(ProfileJump.LandedStatus(profileName), …)</c> 的完整调用
    /// （不是只把文案算出来丢掉）；② 它落在 <c>if (found)</c> 之后、<c>MissingStatus</c> 之前——
    /// 顺序就是「找到了才说落到了」这件事本身。
    /// </para>
    /// </summary>
    [Fact]
    public void LandedStatus_IsWiredIntoTheFoundBranch()
    {
        var body = SourceSlice.BodyOf(ProjectsPage, "public bool RevealProfile(string profileId, string profileName)");
        Assert.Empty(RevealWiringProblems(body));
    }

    /// <summary>
    /// 「要不要清筛选」的第一个实参<b>取自 <see cref="ListFilter.IsActive"/></b>（第二百六十八轮，简报 P2-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：<see cref="ProfileJump.ShouldClearFilter"/> 的两个入参都由调用方当场算出来，
    /// 而「筛选有没有生效」这件事全仓只有一个判法（<see cref="ListFilter.IsActive"/>：去首尾空白后非空）。
    /// 调用方图省事写成 <c>!string.IsNullOrEmpty(_filter.Text)</c> 时，一个只有空格的筛选框会被当成「筛了」——
    /// 第二百六十三轮那个缺陷就以另一种形状回来了（清空一个本来就没在筛的框，用户敲的空格被吃掉）。
    /// </para>
    /// <para>
    /// 判据本体在 <see cref="RevealWiringProblems"/>，注入自验在
    /// <see cref="RevealWiringDetector_ReportsTheBreaksItMustCatch"/>。
    /// </para>
    /// </summary>
    [Fact]
    public void ShouldClearFilter_GetsTheFilterStateFromListFilter()
    {
        var body = SourceSlice.BodyOf(ProjectsPage, "public bool RevealProfile(string profileId, string profileName)");
        var call = Regex.Match(body, @"ProfileJump\.ShouldClearFilter\((?<args>[^\n]*)\)");
        Assert.True(call.Success, "RevealProfile 里没有调 ProfileJump.ShouldClearFilter：要不要清筛选没有唯一落点。");
        Assert.Equal("ListFilter.IsActive(_filter.Text)", call.Groups["args"].Value.Split(',')[0].Trim());
    }

    /// <summary>
    /// 上面两条判据<b>自己</b>被抓得住（第二百六十八轮）：拿四发注入各喂一次
    /// <see cref="RevealWiringProblems"/>——删掉回执、把回执挪到「找不到」之后、把第一个实参就地拼、
    /// 以及合规的那一份（不报）。
    /// <para>
    /// 本仓第二百零九轮的教训是「判据自己没被跑过」比「没有判据」更贵：一条永远为真的断言
    /// 与没有断言在报告里长得一样。
    /// </para>
    /// </summary>
    [Fact]
    public void RevealWiringDetector_ReportsTheBreaksItMustCatch()
    {
        var good = SourceSlice.BodyOf(ProjectsPage, "public bool RevealProfile(string profileId, string profileName)");
        Assert.Empty(RevealWiringProblems(good));

        // ① 回执那一句被删掉：那句话只剩文案，没有接线。
        var silent = good.Replace(LandedCall, "// 不给回执", StringComparison.Ordinal);
        Assert.Contains(RevealWiringProblems(silent), p => p.Contains("回执只是文案", StringComparison.Ordinal));

        // ② 回执挪到「找不到」那一支之后：找不到那一行时也会说「已定位到」。
        var reordered = good
            .Replace(LandedCall, "", StringComparison.Ordinal)
            .Replace(MissingCall, MissingCall + "\n        " + LandedCall, StringComparison.Ordinal);
        Assert.Contains(RevealWiringProblems(reordered), p => p.Contains("顺序反了", StringComparison.Ordinal));

        // ③ 第一个实参就地拼（框里有没有字）：只有空格的筛选框会被当成「筛了」。
        var inline = good.Replace(
            "ProfileJump.ShouldClearFilter(ListFilter.IsActive(_filter.Text), found)",
            "ProfileJump.ShouldClearFilter(!string.IsNullOrEmpty(_filter.Text), found)",
            StringComparison.Ordinal);
        Assert.Contains(RevealWiringProblems(inline), p => p.Contains("第一个实参", StringComparison.Ordinal));

        // ④ 合规的那一份：一处都不报（上面三条注入若把判据改成恒真，这一条会红）。
        Assert.Empty(RevealWiringProblems(good));
    }

    // ── 判据本体（纯函数：文本进、问题出） ────────────────────────────

    private const string LandedCall = "SetStatus(ProfileJump.LandedStatus(profileName), UiTokens.Muted);";
    private const string MissingCall = "SetStatus(ProfileJump.MissingStatus(profileName), UiTokens.BadText);";

    /// <summary>
    /// <c>RevealProfile</c> 的两处接线判据（第二百六十八轮）：正文进、问题清单出（空清单 = 过得去）。
    /// <para>
    /// 与 <see cref="WiringProblems"/> 同一做法：判据是纯函数，注入才喂得进去。
    /// </para>
    /// </summary>
    private static List<string> RevealWiringProblems(string reveal)
    {
        var problems = new List<string>();

        // ① 回执是完整的一次 SetStatus 调用，不是只把文案算出来。
        var found = reveal.IndexOf("if (found)", StringComparison.Ordinal);
        var call = reveal.IndexOf(LandedCall, StringComparison.Ordinal);
        var missing = reveal.IndexOf("ProfileJump.MissingStatus(", StringComparison.Ordinal);
        if (call < 0)
        {
            problems.Add("「落到了那一行」那一支没有调 SetStatus(ProfileJump.LandedStatus(...))："
                + "回执只是文案，不是接线——用户看不出这一行是跳过来的还是上次留下的。");
        }
        else if (found < 0 || call < found)
        {
            problems.Add("回执不在「找到了」那一支里：找不到那一行时也会说「已定位到」。");
        }
        else if (missing >= 0 && call > missing)
        {
            problems.Add("回执落在「找不到」那一支之后：两句话的顺序反了——先判落没落到，再说落到了什么。");
        }

        // ② 「筛选有没有生效」取自 ListFilter.IsActive，不就地拼条件。
        var args = Regex.Match(reveal, @"ProfileJump\.ShouldClearFilter\((?<args>[^\n]*)\)");
        if (!args.Success)
        {
            problems.Add("RevealProfile 没有调 ProfileJump.ShouldClearFilter：要不要清筛选没有唯一落点。");
        }
        else if (args.Groups["args"].Value.Split(',')[0].Trim() != "ListFilter.IsActive(_filter.Text)")
        {
            problems.Add("ShouldClearFilter 的第一个实参不是 ListFilter.IsActive(...) 的结果："
                + "就地拼「框里有没有字」会让只有空格的筛选框被当成「筛了」，第二百六十三轮那个缺陷换个形状回来。");
        }

        return problems;
    }

    /// <summary>
    /// 页面名不许在 <see cref="ProfileJump"/> 里写成第二份字面量：那一句里的「项目设置」必须来自
    /// <see cref="PageNames.Projects"/>。判法是「源码里不许出现带引号的页面名」——改名字时这一条会红，
    /// 而只改 <see cref="PageNames"/> 不改这里时，上面那条断言仍然绿（它比的是同一个常量）。
    /// </summary>
    [Fact]
    public void PageName_ComesFromTheSharedSource()
    {
        Assert.Contains("PageNames.Projects", JumpSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"" + PageNames.Projects + "\"", JumpSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"" + PageNames.Tasks + "\"", JumpSource, StringComparison.Ordinal);
    }

    // ── 三处接线 ─────────────────────────────────────────────────────

    /// <summary>
    /// 任务详情：按钮文字取自共享口径、摆进详情区、点击发请求，且「有没有落点」由共享口径判。
    /// </summary>
    [Fact]
    public void TaskDetail_ShowsTheButtonAndRaisesTheRequest()
    {
        Assert.Contains("Ui.Ghost(ProfileJump.ButtonLabel)", TasksPage, StringComparison.Ordinal);
        Assert.Contains("AddDetailRow(grid, _openProfileButton)", TasksPage, StringComparison.Ordinal);
        Assert.Contains("_openProfileButton.Click += (_, _) => RequestOpenProfile();", TasksPage, StringComparison.Ordinal);
        Assert.Contains("ProfileJump.CanJump(task.ProfileSnapshot.Id)", TasksPage, StringComparison.Ordinal);
        Assert.Contains("OpenProfileRequested?.Invoke(task.ProfileSnapshot.Id, task.ProfileSnapshot.Name)", TasksPage, StringComparison.Ordinal);
        // 本页不自己切页：页面之间不互相引用（切页只在壳那一处）。
        Assert.DoesNotContain("ShowProjects()", TasksPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectsPage", TasksPage, StringComparison.Ordinal);
    }

    /// <summary>
    /// 按钮的可用状态只有一个落点（详情区动作的单一门控）：<c>_openProfileButton.Enabled</c> 在整份源码里
    /// 只出现一次。第二处赋值会让「没有选中任务时入口仍可点」这类不一致重新长出来。
    /// </summary>
    [Fact]
    public void ButtonEnablement_HasASingleLandingPoint()
    {
        var hits = Regex.Matches(TasksPage, @"_openProfileButton\.Enabled\s*=").Count;
        Assert.Equal(1, hits);
        // 那一处必须在 RefreshDetailActions 里（与其余详情区入口同一份门控），不在别处。
        var body = SourceSlice.BodyOf(TasksPage, "private void RefreshDetailActions(JobInfo? job)");
        Assert.Contains("_openProfileButton.Enabled", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 壳：把任务详情的请求接到配置页的落点上——先切页（切页会重读清单），再按 Id 选中那一行。
    /// </summary>
    [Fact]
    public void Shell_WiresTheRequestToTheOtherPage()
    {
        Assert.Contains("_tasksPage.OpenProfileRequested += (profileId, profileName) =>", Shell, StringComparison.Ordinal);
        var body = SourceSlice.BodyOf(Shell, "_tasksPage.OpenProfileRequested += (profileId, profileName) =>");
        Assert.Contains("ShowProjects();", body, StringComparison.Ordinal);
        Assert.Contains("_projectsPage.RevealProfile(profileId, profileName);", body, StringComparison.Ordinal);
        // 顺序是判据：先切页再选中——反过来会在切页重读清单时把选中冲掉。
        Assert.True(
            body.IndexOf("ShowProjects();", StringComparison.Ordinal) < body.IndexOf("RevealProfile(", StringComparison.Ordinal),
            "接线里必须先切页再选中那一行。");
    }

    /// <summary>
    /// 配置页的落点：认不认得出该跳哪一份由共享口径判、选中与滚动走 <see cref="Ui.SelectItem"/> 唯一入口、
    /// 找不到时那句话取自共享口径（不就地拼）。
    /// </summary>
    [Fact]
    public void ProjectsPage_RevealsTheRowOrSaysWhyItCannot()
    {
        var body = SourceSlice.BodyOf(ProjectsPage, "public bool RevealProfile(string profileId, string profileName)");
        Assert.Contains("ProfileJump.CanJump(profileId)", body, StringComparison.Ordinal);
        // 选中与滚动走 Ui.SelectItem 唯一入口——第二百六十七轮起它在两遍查找共用的 SelectRow 里。
        Assert.Contains("Ui.SelectItem(item)", SourceSlice.BodyOf(ProjectsPage, "private bool SelectRow(string profileId)"), StringComparison.Ordinal);
        Assert.Contains("ProfileJump.MissingStatus(profileName)", body, StringComparison.Ordinal);
        // 只换选中与滚动：不打开编辑对话框、不写配置数据。
        Assert.DoesNotContain("ShowDialog(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_profiles.Save", body, StringComparison.Ordinal);
        // 筛选把那一行筛掉时才清空筛选（清空走既有重建，不另写一条读取路径）；
        // 「要不要清」由 ProfileJump.ShouldClearFilter 一处判，本页不自己写那个条件（第二百六十七轮）。
        Assert.Contains("ListFilter.IsActive(_filter.Text)", body, StringComparison.Ordinal);
        Assert.Contains("ProfileJump.ShouldClearFilter(", body, StringComparison.Ordinal);
        // 读取失败态走另一句话（第二百六十四轮）：清单没读出来时说「没读出来」，不说「已不存在」。
        Assert.Contains("if (_loadFailed)", body, StringComparison.Ordinal);
        Assert.Contains("ProfileJump.LoadFailedStatus(profileName)", body, StringComparison.Ordinal);
        // 顺序是判据：失败态那一支必须在「找不到那一行」之前——反过来，空清单会先落到 MissingStatus 上。
        Assert.True(
            body.IndexOf("ProfileJump.LoadFailedStatus(", StringComparison.Ordinal)
            < body.IndexOf("ProfileJump.MissingStatus(", StringComparison.Ordinal),
            "读取失败态必须先判：清单是空的时候，先落到 MissingStatus 就会把「读不出来」说成「已不存在」。");
    }

    // ── 判据自己被抓得住 ──────────────────────────────────────────────

    /// <summary>
    /// 上面几条接线判据<b>自己</b>被抓得住：把每一处接线各拆一次，判据必须报出自己那一处。
    /// <para>
    /// 本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两处假变异）。
    /// 这里判的是纯函数 <see cref="WiringProblems"/>：文本进、问题出。
    /// </para>
    /// </summary>
    [Fact]
    public void WiringDetector_ReportsTheBreaksItMustCatch()
    {
        var good = Wiring(TasksPage, ProjectsPage, Shell);
        Assert.Empty(WiringProblems(good.Tasks, good.Projects, good.Shell));

        // ① 按钮文字就地写死（不再取共享口径）。
        var literal = good with { Tasks = good.Tasks.Replace("Ui.Ghost(ProfileJump.ButtonLabel)", "Ui.Ghost(\"打开配置\")", StringComparison.Ordinal) };
        Assert.Contains(WiringProblems(literal.Tasks, literal.Projects, literal.Shell), p => p.Contains("ButtonLabel", StringComparison.Ordinal));

        // ② 壳没接线（请求发出去没人接）。
        var unwired = good with { Shell = good.Shell.Replace("_tasksPage.OpenProfileRequested += (profileId, profileName) =>", "// 未接线", StringComparison.Ordinal) };
        Assert.Contains(WiringProblems(unwired.Tasks, unwired.Projects, unwired.Shell), p => p.Contains("OpenProfileRequested", StringComparison.Ordinal));

        // ③ 配置页就地拼那句话（不再取共享口径）。
        var inline = good with { Projects = good.Projects.Replace("ProfileJump.MissingStatus(profileName)", "\"找不到这个配置。\"", StringComparison.Ordinal) };
        Assert.Contains(WiringProblems(inline.Tasks, inline.Projects, inline.Shell), p => p.Contains("MissingStatus", StringComparison.Ordinal));

        // ④ 读取失败态那一支被拆掉（空清单直接落到「已不存在」上）。
        var noFailureBranch = good with { Projects = good.Projects.Replace("ProfileJump.LoadFailedStatus(profileName)", "ProfileJump.MissingStatus(profileName)", StringComparison.Ordinal) };
        Assert.Contains(WiringProblems(noFailureBranch.Tasks, noFailureBranch.Projects, noFailureBranch.Shell), p => p.Contains("LoadFailedStatus", StringComparison.Ordinal));

        // ⑤ 按钮可用状态长出第二处赋值。
        var twice = good with { Tasks = good.Tasks.Replace("_gapButton.Enabled = scanReady;", "_openProfileButton.Enabled = true;\n        _gapButton.Enabled = scanReady;", StringComparison.Ordinal) };
        Assert.Contains(WiringProblems(twice.Tasks, twice.Projects, twice.Shell), p => p.Contains("单一落点", StringComparison.Ordinal));

        // ⑥ 读取失败态不再记住这一跳（「读出来后会定位到它」成了空话）。
        var forgetful = good with { Projects = good.Projects.Replace("_pendingReveal = (profileId, profileName);", "// 不记了", StringComparison.Ordinal) };
        Assert.Contains(WiringProblems(forgetful.Tasks, forgetful.Projects, forgetful.Shell), p => p.Contains("_pendingReveal", StringComparison.Ordinal));

        // ⑦ 落到那一行时不给回执（用户看不出这一行是跳过来的还是上次留下的）。
        var silent = good with { Projects = good.Projects.Replace("ProfileJump.LandedStatus(profileName)", "null", StringComparison.Ordinal) };
        Assert.Contains(WiringProblems(silent.Tasks, silent.Projects, silent.Shell), p => p.Contains("LandedStatus", StringComparison.Ordinal));

        // ⑨ 清空筛选不再看「找没找到」（退回第二百六十三轮那个形状：筛选框里有词就清）。
        var unconditional = good with { Projects = good.Projects.Replace("ProfileJump.ShouldClearFilter(ListFilter.IsActive(_filter.Text), found)", "ListFilter.IsActive(_filter.Text)") };
        Assert.Contains(WiringProblems(unconditional.Tasks, unconditional.Projects, unconditional.Shell), p => p.Contains("ShouldClearFilter", StringComparison.Ordinal));

        // ⑧ 那颗「重试」直接再读一次（挂起的那一跳没人消费）。
        var unconsumed = good with { Projects = good.Projects.Replace("_emptyRetry.Click += (_, _) => RetryLoad();", "_emptyRetry.Click += (_, _) => LoadProfiles();", StringComparison.Ordinal) };
        Assert.Contains(WiringProblems(unconsumed.Tasks, unconsumed.Projects, unconsumed.Shell), p => p.Contains("RetryLoad", StringComparison.Ordinal));
    }

    // ── 判据本体（纯函数：文本进、问题出） ────────────────────────────

    private sealed record WiringText(string Tasks, string Projects, string Shell);

    private static WiringText Wiring(string tasks, string projects, string shell) => new(tasks, projects, shell);

    /// <summary>
    /// 接线判据：三份源码进、问题清单出（空清单 = 过得去）。与 <c>SourceDriftDetector</c> 同一做法。
    /// </summary>
    private static List<string> WiringProblems(string tasks, string projects, string shell)
    {
        var problems = new List<string>();

        if (!tasks.Contains("Ui.Ghost(ProfileJump.ButtonLabel)", StringComparison.Ordinal))
            problems.Add("任务详情的按钮文字没有取自 ProfileJump.ButtonLabel（就地写死会让改一处不改另一处）。");
        if (!tasks.Contains("ProfileJump.CanJump(", StringComparison.Ordinal))
            problems.Add("任务详情没有用 ProfileJump.CanJump 判「这一跳有没有落点」。");
        if (!tasks.Contains("OpenProfileRequested?.Invoke(", StringComparison.Ordinal))
            problems.Add("任务详情没有发出 OpenProfileRequested 请求。");
        if (Regex.Matches(tasks, @"_openProfileButton\.Enabled\s*=").Count != 1)
            problems.Add("「打开配置」的可用状态不是单一落点（_openProfileButton.Enabled 赋值不止一处）。");

        if (!shell.Contains("_tasksPage.OpenProfileRequested +=", StringComparison.Ordinal))
            problems.Add("壳没有接线 _tasksPage.OpenProfileRequested（请求发出去没人接）。");
        if (!shell.Contains("_projectsPage.RevealProfile(", StringComparison.Ordinal))
            problems.Add("壳没有把请求接到配置页的 RevealProfile 落点上。");

        if (!projects.Contains("ProfileJump.CanJump(", StringComparison.Ordinal))
            problems.Add("配置页没有用 ProfileJump.CanJump 判「该跳到哪一份配置」。");
        if (!projects.Contains("ProfileJump.MissingStatus(", StringComparison.Ordinal))
            problems.Add("配置页没有用 ProfileJump.MissingStatus 说「找不到那一行」（就地拼会让两处说法分家）。");
        if (!projects.Contains("ProfileJump.LoadFailedStatus(", StringComparison.Ordinal))
            problems.Add("配置页没有用 ProfileJump.LoadFailedStatus 说「清单这次没读出来」——"
                + "读取失败态下清单是空的，落到 MissingStatus 就会把「读不出来」说成「已不存在」。");
        if (!projects.Contains("ProfileJump.ShouldClearFilter(", StringComparison.Ordinal))
            problems.Add("配置页没有用 ProfileJump.ShouldClearFilter 判「要不要清空筛选」——"
                + "条件写成「筛选框里有词」时，那一行本来就在筛选结果里也会被清空，用户白付一笔。");
        if (!projects.Contains("Ui.SelectItem(", StringComparison.Ordinal))
            problems.Add("配置页没有走 Ui.SelectItem 唯一入口选中那一行。");
        if (!projects.Contains("_pendingReveal = (profileId, profileName);", StringComparison.Ordinal))
            problems.Add("配置页在读取失败态下没有记住这一跳（_pendingReveal）——"
                + "LoadFailedStatus 那句「读出来后会定位到它」就成了空话。");
        if (!projects.Contains("ProfileJump.LandedStatus(", StringComparison.Ordinal))
            problems.Add("配置页落到那一行时没有用 ProfileJump.LandedStatus 给回执"
                + "（选中在配置页上还有「上次留下的选中」这个来源，两种来源在屏幕上长得一样）。");
        if (!projects.Contains("_emptyRetry.Click += (_, _) => RetryLoad();", StringComparison.Ordinal))
            problems.Add("失败态那颗「重试」没有走 RetryLoad（挂起的那一跳没人消费，读成功后落不回去）。");

        return problems;
    }

}
