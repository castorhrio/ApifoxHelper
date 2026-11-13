using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 配置读取失败态断言（具名 pass：harden；DESIGN.md §7「禁用不得移除下一步」「空态=说明现在有什么可做 + 唯一行动按钮」）。
/// <para>
/// 此前配置页的清单区只有两种状态：没有配置、筛到零行。第三种——<b>读取失败</b>——没有：
/// <c>LoadProfiles</c> 的 catch 只往状态行写一句红字，清单区保持上一次的样子（首次进入时是整块空白），
/// 屏上没有任何可点的东西。用户看到「失败了」，下一步却要自己猜（去点工具栏的「刷新」？重开窗口？）。
/// 这与第四十二轮「禁用不得移除下一步」是同一条纪律：失败态也是空态的一种，同样欠一句「现在能做什么」
/// 和一个能点的入口。
/// </para>
/// <para>
/// 现在失败态按空态同一口径补齐：一句可操作文案（<see cref="ProfileLoadState.FailureHint"/>）+
/// 唯一行动按钮「重试」（<see cref="ProfileLoadState.RetryLabel"/>，动作回到 <c>LoadProfiles</c> 同一个入口）。
/// 对话框里的「读取项目列表」「读取分支」失败时<b>不新增第二颗按钮</b>——那颗按钮在 <c>finally</c> 里
/// 已回到可用，点它就是重试，失败文案指名它（<see cref="ProfileLoadState.RetryHint"/>）。
/// </para>
/// <para>
/// 口径单一落点在 <see cref="ProfileLoadState"/>（共享源码编入本程序集，断言比的是主工程同一份代码）；
/// 两处接线由源码切片机械检查钉住；真实控件上的「失败 → 重试 → 恢复」由冒烟器
/// <c>RunProfileLoadFailure</c> 在 Windows/wine 下走。
/// </para>
/// </summary>
public class ProfileLoadFailureTests
{
    // ---------- 口径：文案与判定 ----------

    [Fact]
    public void FailureCopy_NamesTheRetryButtonThatExists()
    {
        // 提示语指名的入口必须是界面上真实存在的那一个（第四十二轮纪律）：说明行里点名的就是那颗按钮。
        Assert.Contains(ProfileLoadState.RetryLabel, ProfileLoadState.FailureHint);
        Assert.Equal("重试", ProfileLoadState.RetryLabel);
        Assert.Equal("读取配置失败", ProfileLoadState.FailureTitle);
        // 一句话给一个下一步：失败态不提「刷新」（工具栏那颗按钮做同一件事，但并列写两个入口等于没给下一步）。
        Assert.DoesNotContain("刷新", ProfileLoadState.FailureHint);
        Assert.DoesNotContain("Esc", ProfileLoadState.FailureHint);
    }

    [Fact]
    public void FailureStatus_KeepsTheReasonAfterTheTitle()
    {
        // 状态行讲原因、清单区讲下一步：两条通道各说一件事，原因仍如实跟在标题后面。
        Assert.Equal("读取配置失败：目录不可读", ProfileLoadState.FailureStatus("目录不可读"));
    }

    [Fact]
    public void RetryVisible_OnlyInFailureState()
    {
        // 三种状态同一时刻只显示一个行动按钮：失败时给「新建配置」等于把用户往另一条路上引。
        Assert.True(ProfileLoadState.RetryVisible(failed: true));
        Assert.False(ProfileLoadState.RetryVisible(failed: false));
    }

    [Fact]
    public void PickerFailureStatus_NamesTheEntryAndAvoidsDoublePunctuation()
    {
        // 对话框不新增第二颗「重试」：失败文案指名那颗已经回到可用的读取按钮。
        Assert.Equal(
            "读取项目列表失败：命令超时；可点「读取项目列表」重试。",
            ProfileLoadState.PickerFailureStatus("读取项目列表失败：", "命令超时", "读取项目列表"));
        // 描述自带句号时不出现「。；」这种双标点。
        Assert.Equal(
            "读取分支失败：网络不可达；可点「读取分支」重试。",
            ProfileLoadState.PickerFailureStatus("读取分支失败：", "网络不可达。", "读取分支"));
        // 指引里的按钮名与界面上的按钮文字同源（两处各写一份字面就会指向一颗不存在的按钮）。
        Assert.Contains("「读取分支」", ProfileLoadState.RetryHint("读取分支"));
    }

    [Fact]
    public void FailureCopy_PassesChineseTypographyRules()
    {
        // 失败态文案与其余界面文案同一套中文排版口径（DESIGN.md §3 排版附加条款、设计验收 D10/D11）。
        var samples = new[]
        {
            ProfileLoadState.FailureTitle,
            ProfileLoadState.FailureHint,
            ProfileLoadState.RetryLabel,
            ProfileLoadState.FailureStatus("目录不可读"),
            ProfileLoadState.PickerFailureStatus("读取项目列表失败：", "命令超时", "读取项目列表"),
        };
        foreach (var text in samples)
        {
            foreach (var mark in CopyRules.HalfWidthMarks)
            {
                Assert.True(CopyRules.IndexOfHalfWidthMark(text, mark) < 0, $"「{text}」里的半角「{mark}」紧挨中文。");
            }
            Assert.True(CopyRules.IndexOfStrayColon(text) < 0, $"「{text}」里的半角冒号紧挨中文。");
            Assert.DoesNotContain("“", text);
            Assert.DoesNotContain("”", text);
            foreach (var word in CopyRules.BannedWords) Assert.DoesNotContain(word, text);
            foreach (var phrase in CopyRules.ProgressNarration) Assert.DoesNotContain(phrase, text);
        }
    }

    // ---------- 接线：口径存在但界面没接线等于没有 ----------

    [Fact]
    public void ProjectsPage_WiresRetryToTheSameLoadEntry()
    {
        var page = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        // 按钮文字取自单一落点，动作回到同一个读取入口（不复制一份读取逻辑）。
        Assert.Matches(@"(?m)^\s*private readonly Button _emptyRetry = Ui\.Ghost\(ProfileLoadState\.RetryLabel\);", page);
        // 第二百六十五轮起经一层包装 RetryLoad：先捕获挂起的那一跳、再调同一个 LoadProfiles，
        // 读成功后落回那一行（「重试回到同一个读取入口」由 LoadFailureRetryCoverageTests 判住）。
        Assert.Matches(@"(?m)^\s*_emptyRetry\.Click \+= \(_, _\) => RetryLoad\(\);", page);
        var retry = Slice(page, "private void RetryLoad()", "private void ShowLoadFailure(");
        // 包装里只调一个读取入口：再写一份读取逻辑就是「两条路各读一份」。
        Assert.Single(Regex.Matches(retry, @"\bLoadProfiles\("));
        Assert.Matches(@"(?m)^\s*emptyActions\.Controls\.Add\(_emptyRetry\);", page);
        // 三态门控：失败时只显示「重试」，另两颗按钮一起收掉。
        var refresh = Slice(page, "private void RefreshEmptyState()", "private static string?[] FilterFields(");
        Assert.Matches(@"(?m)^\s*_emptyCreate\.Visible = !_loadFailed && !filtered;", refresh);
        Assert.Matches(@"(?m)^\s*_emptyClear\.Visible = !_loadFailed && filtered;", refresh);
        Assert.Matches(@"(?m)^\s*_emptyRetry\.Visible = ProfileLoadState\.RetryVisible\(_loadFailed\);", refresh);
        Assert.Matches(@"(?m)^\s*_emptyTitle\.Text = _loadFailed \? ProfileLoadState\.FailureTitle", refresh);
        Assert.Matches(@"(?m)^\s*_emptyHint\.Text = _loadFailed \? ProfileLoadState\.FailureHint", refresh);
    }

    [Fact]
    public void ProjectsPage_FailureStateReplacesTheListAndKeepsTheReason()
    {
        var page = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        // 两条 catch 都进失败态：UserException 的文案本身就是给用户看的那一句，其余异常先脱敏。
        var load = Slice(page, "private void LoadProfiles()", "private void ShowLoadFailure(");
        Assert.Matches(@"(?m)^\s*ShowLoadFailure\(ex\.Message\);", load);
        Assert.Matches(@"(?m)^\s*ShowLoadFailure\(ProfileLoadState\.FailureStatus\(Redactor\.Redact\(ex\.Message\)\)\);", load);
        // 读成功即退出失败态（否则重试成功后仍停在失败态）。
        Assert.Matches(@"(?m)^\s*_loadFailed = false;", load);
        var failure = Slice(page, "private void ShowLoadFailure(", "private void RefreshEmptyState()");
        Assert.Matches(@"(?m)^\s*_loadFailed = true;", failure);
        // 清单不留旧行：这一屏刚证明「磁盘上的配置读不出来」，继续显示旧行等于把不可信的内容当成现状。
        Assert.Matches(@"(?m)^\s*_list\.Items\.Clear\(\);", failure);
        Assert.Matches(@"(?m)^\s*_empty\.Visible = true;", failure);
        Assert.Matches(@"(?m)^\s*_list\.Visible = false;", failure);
        // 清单空了，编辑与探测没有可作用的对象。
        Assert.Matches(@"(?m)^\s*_editButton\.Enabled = false;", failure);
        Assert.Matches(@"(?m)^\s*_probeButton\.Enabled = false;", failure);
        // 状态行仍如实给出原因，且刷新空态（三态在同一轮推导）。
        Assert.Matches(@"(?m)^\s*RefreshEmptyState\(\);", failure);
        Assert.Matches(@"(?m)^\s*SetStatus\(status\);", failure);
    }

    [Fact]
    public void ProjectsPage_DoesNotInlineFailureCopy()
    {
        // 「读取配置失败」字面只在 ProfileLoadState 里：页面出现带它的字符串字面即口径漂移（注释里的引述不算）。
        var page = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        Assert.Empty(Regex.Matches(page, "\"[^\"\\r\\n]*读取配置失败[^\"\\r\\n]*\""));
        Assert.Contains("ProfileLoadState.FailureStatus", page);
    }

    [Fact]
    public void ProfileDialog_FailureCopyNamesTheLoadButtons()
    {
        var page = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        // 按钮文字单一落点：失败文案指名的字面与按钮文字同源。
        Assert.Matches(@"(?m)^\s*private const string LoadProjectsLabel = ""读取项目列表"";", page);
        Assert.Matches(@"(?m)^\s*private const string LoadBranchesLabel = ""读取分支"";", page);
        Assert.Matches(@"(?m)^\s*_loadProjectsButton = Ui\.Ghost\(LoadProjectsLabel\);", page);
        Assert.Matches(@"(?m)^\s*_loadBranchesButton = Ui\.Ghost\(LoadBranchesLabel\);", page);
        // 四条失败分支（CLI 返回失败 / 抛异常，项目与分支各两条）都补上重试指引。
        Assert.Equal(2, Regex.Matches(page, @"ProfileLoadState\.PickerFailureStatus\(ApifoxPicker\.ProjectsFailedPrefix").Count);
        Assert.Equal(2, Regex.Matches(page, @"ProfileLoadState\.PickerFailureStatus\(ApifoxPicker\.BranchesFailedPrefix").Count);
        // 那颗按钮在 finally 里回到可用——这正是「点它就是重试」成立的前提。
        var projects = Slice(page, "private async Task LoadApifoxProjectsAsync()", "private async Task LoadApifoxBranchesAsync()");
        Assert.Matches(@"(?m)^\s*if \(!IsDisposed\) _loadProjectsButton\.Enabled = ProfileFormLayout\.ProjectListEnabled\(_editing is not null\);", projects);
        var branches = Slice(page, "private async Task LoadApifoxBranchesAsync()", "private static void FillCombo(");
        Assert.Matches(@"(?m)^\s*if \(!IsDisposed\) _loadBranchesButton\.Enabled = true;", branches);
    }

    [Fact]
    public void ProfileLoadState_IsInTheSharedCompileList()
    {
        // 口径文件必须经共享源码编入测试程序集：断言比的是主工程同一份代码，不是抄一份。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ProfileLoadState.cs", csproj);
        // 口径文件不依赖 WinForms（否则 Linux 上编不进测试程序集）。
        var source = Repo.Source("Desktop", "Native", "ProfileLoadState.cs");
        Assert.DoesNotContain("using System.Windows.Forms", source);
        Assert.DoesNotContain("using System.Drawing", source);
    }

    /// <summary>取 [start, end) 之间的源码段：把断言限定在方法体内。</summary>
    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
