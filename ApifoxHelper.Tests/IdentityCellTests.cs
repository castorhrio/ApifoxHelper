using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 标识格的等宽口径（docs/DESIGN.md §3「等宽栈：计数、进度、ID、路径、差异文本必须等宽」、
/// 功能清单设计验收 D5「比例字体下跳宽即不合规」）。
/// <para>
/// <b>补的是哪个缺口</b>（第二百二十八轮，具名 pass：typeset）：计数格在第二百零五轮已收进
/// <c>Ui.CountCell</c>，而<b>标识格</b>（接口身份「GET /路径」、远端 ID、Apifox 项目 ID、源码路径）
/// 一直是各屏就地设一次——十三处各写一句 <c>.Font = UiTokens.Mono.Value</c>，而
/// <c>ContractsDialog</c> 的首列（接口身份）与 <c>ProjectsPage</c> 的「Apifox 项目」ID 列
/// <b>一处都没设</b>：同一类信息在另八张清单上等宽、在这两屏上是比例字体，属同壳不同行为。
/// 后果与计数格同一条（§6.2 #5 给的理由是行为不是观感）：比例字体下「9」与「10」宽度不同，
/// 两次扫描之间数字一变整列左右抖动，用户没法用余光比较同一列的两个数。
/// </para>
/// <para>
/// <b>形状先定再逐屏接</b>（DESIGN §8 那一格原话是「先定『标识格由谁设』再逐屏接，不要在工厂外再开第五种落法」）：
/// 字体只经 <c>Ui.IdentityCell</c> 两个重载出厂——新建一格并交出它、把已经建好的那一格（含
/// <c>SubItems[0]</c>，即行自身的文字）交给工厂。界面代码不再就地写 <c>.Font = UiTokens.Mono.Value</c>。
/// </para>
/// <para>
/// <b>不进工厂的一支</b>（由最后一条钉住理由，不是漏掉）：<c>EvidenceDialog</c> 的「位置」那一行是
/// <c>Label</c> 而不是清单格（它讲的是这一屏的证据在哪，不是某一行的标识），走 <c>Ui.Mono</c> 那一支。
/// </para>
/// </summary>
public class IdentityCellTests
{
    private static string UiSource => Repo.Source("Desktop", "Native", "Ui.cs");

    private static string NativeFile(string name) => Repo.Source("Desktop", "Native", name);

    /// <summary>
    /// 工厂本体：只交出「等宽字体」这一件事，不碰文字、不碰颜色。
    /// <para>
    /// <b>两个重载、一个落点</b>（第二百二十九轮修正）：第二百二十八轮这条门写的是
    /// <c>Assert.Single</c>——它把「工厂只有一个」读成了「方法只有一个」，而工厂本来就是两个重载
    /// （新建一格 / 交出已建好的那一格），于是这条门从落地那一刻起就是红的。判据改成
    /// 「恰好两个重载，且第二个只做转发」：真正要钉的是「等宽字体只在一处赋值」，
    /// 那件事由 <see cref="FactoryBody"/> 里那一句 <c>sub.Font = UiTokens.Mono.Value;</c> 判住。
    /// </para>
    /// </summary>
    [Fact]
    public void IdentityCell_IsTheOnlyFactoryAndFormatsNoText()
    {
        Assert.Equal(2, Regex.Matches(UiSource, @"static ListViewItem\.ListViewSubItem IdentityCell\(").Count);
        // 第二个重载只转发：新建一格之后立刻交给第一个重载，不自己再设一次字体。
        Assert.Contains("IdentityCell(row.SubItems.Add(text));", UiSource, StringComparison.Ordinal);
        var body = FactoryBody();
        Assert.Contains("sub.Font = UiTokens.Mono.Value;", body, StringComparison.Ordinal);
        // 文字是调用方算好交进来的：工厂不格式化、不发明占位符（「未绑定」「—」这类占位各屏自己那一份）。
        Assert.False(Regex.IsMatch(body, @"ToString\(|""—""|""未绑定""|PadLeft"),
            "Ui.IdentityCell 里出现了取数或占位逻辑——标识格的文字口径在各屏自己那一份，工厂只管字体。");
    }

    /// <summary>
    /// 就地设标识格字体这条路不得复活：<c>Desktop/Native</c> 里给清单格设等宽字体的地方只剩工厂一处。
    /// 判据是形状——<c>.Font = UiTokens.Mono.Value</c> 在界面源码里只许出现在 <c>Ui.cs</c>（工厂本体）
    /// 与 <c>EvidenceDialog</c> 那一行 <c>Label</c>（不是清单格，见类注释）。
    /// </summary>
    [Fact]
    public void IdentityCells_NeverSetTheirFontInPlace()
    {
        var offenders = new List<string>();
        foreach (var path in Directory.GetFiles(Path.Combine(Repo.Root, "Desktop", "Native"), "*.cs"))
        {
            var name = Path.GetFileName(path);
            if (name == "Ui.cs") continue;
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains(".Font = UiTokens.Mono.Value", StringComparison.Ordinal)) continue;
                // 唯一允许的一处：证据对话框的「位置」是 Label（不是清单格），走 Ui.Mono 那一支。
                if (name == "EvidenceDialog.cs" && lines[index].Contains("_locationLabel", StringComparison.Ordinal)) continue;
                offenders.Add($"{name}:{index + 1} {lines[index].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "标识格又就地设了一次等宽字体——等宽字体只在 Ui.IdentityCell 一处出厂（DESIGN.md §8「数字等宽」）：\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// 十四处标识格全部经工厂出厂：八张清单原有的十三处（含同步预览的资源与远端 ID 两格）
    /// 加上本轮补上的两处（响应契约首列、项目列表的「Apifox 项目」ID 列）。
    /// </summary>
    [Fact]
    public void EveryIdentityCell_GoesThroughTheFactory()
    {
        Assert.Equal(1, Count(NativeFile("DecisionsDialog.cs"), @"Ui\.IdentityCell\(item, entry\.Resource\)"));
        Assert.Equal(1, Count(NativeFile("PropagationDialog.cs"), @"Ui\.IdentityCell\(path\)"));
        Assert.Equal(1, Count(NativeFile("EvidenceDialog.cs"), @"Ui\.IdentityCell\(location\)"));
        Assert.Equal(1, Count(NativeFile("SupplementsDialog.cs"), @"Ui\.IdentityCell\(item\.SubItems\[0\]\)"));
        Assert.Equal(1, Count(NativeFile("ScanResultsDialog.cs"), @"Ui\.IdentityCell\(location\)"));
        Assert.Equal(1, Count(NativeFile("EndpointTraceDialog.cs"), @"Ui\.IdentityCell\(row\.SubItems\[0\]\)"));
        Assert.Equal(2, Count(NativeFile("CheckResultsDialog.cs"), @"Ui\.IdentityCell\(row\.SubItems\[0\]\)"));
        Assert.Equal(2, Count(NativeFile("SyncPreviewDialog.cs"), @"Ui\.IdentityCell\(row\.SubItems\[0\]\)"));
        Assert.Equal(1, Count(NativeFile("SyncPreviewDialog.cs"), @"Ui\.IdentityCell\(resource\)"));
        Assert.Equal(1, Count(NativeFile("SyncPreviewDialog.cs"), @"Ui\.IdentityCell\(remoteId\)"));
        // 本轮补上的两处（此前一处都没设）。
        Assert.Equal(1, Count(NativeFile("ContractsDialog.cs"), @"Ui\.IdentityCell\(item\.SubItems\[0\]\)"));
        Assert.Equal(1, Count(NativeFile("ProjectsPage.cs"), @"Ui\.IdentityCell\(item, profile\.Apifox\.ProjectId"));
    }

    /// <summary>
    /// 本轮补上的那两列此前是比例字体：判据落在「那一列的文字确实经工厂交出」这一句上，
    /// 而不是「文件里出现过 IdentityCell」——后者在别的列上也能满足（Presence 型断言的坑）。
    /// </summary>
    [Fact]
    public void TheTwoColumnsThatHadNoMonoFont_NowGoThroughTheFactory()
    {
        var contracts = NativeFile("ContractsDialog.cs");
        Assert.Contains("var item = new ListViewItem(contract.EndpointKey) { Tag = contract };", contracts, StringComparison.Ordinal);
        Assert.True(contracts.IndexOf("Ui.IdentityCell(item.SubItems[0]);", StringComparison.Ordinal)
                    > contracts.IndexOf("var item = new ListViewItem(contract.EndpointKey)", StringComparison.Ordinal),
            "响应契约首列（接口身份）的等宽字体没有落在建行之后——那一列仍是比例字体。");

        var projects = NativeFile("ProjectsPage.cs");
        Assert.Contains("Ui.IdentityCell(item, profile.Apifox.ProjectId ?? \"未绑定\");", projects, StringComparison.Ordinal);
        // 占位文字逐字不变：工厂只管字体，不接管「未绑定」这个占位口径。
        Assert.DoesNotContain("item.SubItems.Add(profile.Apifox.ProjectId", projects, StringComparison.Ordinal);
    }

    /// <summary>计数格仍走 <c>Ui.CountCell</c>：两件事的占位口径不同，合成一处会把两种口径混成一个。</summary>
    [Fact]
    public void CountCells_StayOnTheirOwnFactory()
    {
        Assert.Contains("Ui.CountCell(item, contract.Branches.Count.ToString())",
            NativeFile("ContractsDialog.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Ui.IdentityCell(item, contract.Branches", NativeFile("ContractsDialog.cs"), StringComparison.Ordinal);
    }

    private static string FactoryBody()
    {
        var start = UiSource.IndexOf("static ListViewItem.ListViewSubItem IdentityCell(ListViewItem.ListViewSubItem sub)", StringComparison.Ordinal);
        Assert.True(start >= 0, "Ui.cs 里没有 IdentityCell 的「交出已建好的那一格」重载。");
        var end = UiSource.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "Ui.IdentityCell 切不出方法体边界。");
        return UiSource[start..end];
    }

    private static int Count(string text, string pattern) => Regex.Matches(text, pattern).Count;
}
