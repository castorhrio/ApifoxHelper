using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 清单宿主形状断言（DESIGN.md §6「列表行」的「空态叠在清单之上」，第一百一十九轮）：
/// 两个主页面的清单区此前各拼一遍——任务页把空态面板加在发丝边面板之后（空态落在 z 序最前，成立），
/// 项目设置页把发丝边面板加在空态面板之后（发丝边落在最前，空态被整块盖住）。两者都是 Dock=Fill、
/// 占同一块矩形，谁在上面只由 z 序决定，而 Control.Controls 的索引 0 才是 z 序最前
/// （BringToFront 即 SetChildIndex(this, 0)），后加入的控件默认排在后面。
/// 后果是项目设置页在没有配置时内容区只剩一片 #E3E8F0（发丝边面板的 BackColor = Line）：
/// 空态标题、说明与「新建配置」按钮都看不见——第一百一十九轮在 wine 下实测复现（截图主色 889,860 像素
/// 为 #E3E8F0、OCR 读不出任何文字），修好后同一路径读出「还没有项目配置」与那句说明、主色变成 #F5F7FB。
/// 现形状收进 Ui.ListHost 一处（两页共用），z 序只在那里定；界面接线由行首正则与源码切片钉住，
/// 真机观感随 G-D5 复核。
/// </summary>
public class ListHostTests
{
    private static string Ui => Repo.Source("Desktop", "Native", "Ui.cs");

    private static string Factory()
    {
        var source = Ui;
        var start = source.IndexOf("public static Panel ListHost(ListView list, Panel empty)", StringComparison.Ordinal);
        Assert.True(start >= 0, "Ui.ListHost 不存在");
        var end = source.IndexOf("/// <summary>筛选行里关键词框的宽度", start, StringComparison.Ordinal);
        Assert.True(end > start, "找不到 ListHost 的结束位置");
        return source[start..end];
    }

    [Fact]
    public void Factory_AddsTheHairlineThenTheEmptyPanel()
    {
        var factory = Factory();
        // 发丝边包住清单、空态面板随后加入：两者同矩形，顺序由本工厂一处决定。
        Assert.Matches(@"(?m)^\s*host\.Controls\.Add\(Hairline\(list\)\);$", factory);
        Assert.Matches(@"(?m)^\s*host\.Controls\.Add\(empty\);$", factory);
        // 宿主自身填满剩余空间、底色取 Bg（与两页此前的宿主同一份）。
        Assert.Matches(@"new Panel \{ Dock = DockStyle\.Fill, BackColor = UiTokens\.Bg \}", factory);
        Assert.Matches(@"(?m)^\s*return host;$", factory);
    }

    [Fact]
    public void Factory_PutsTheEmptyPanelInFrontOfTheHairline()
    {
        // 空态必须压在发丝边面板之上：不显式提到最前，后加入的控件排在后面，空态就被整块盖住
        // （项目设置页第一百一十九轮实测：内容区只剩发丝边面板的 #E3E8F0，一个字都看不见）。
        var factory = Factory();
        Assert.Matches(@"(?m)^\s*empty\.BringToFront\(\);$", factory);
        // 提到最前只在这一处：界面代码不得自己 BringToFront 空态面板（两页共用同一份形状）。
        foreach (var page in new[] { "TasksPage.cs", "ProjectsPage.cs" })
        {
            var source = Repo.Source("Desktop", "Native", page);
            Assert.DoesNotContain("BringToFront", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BothPages_BuildTheListAreaFromTheSharedFactory()
    {
        // 两页都经工厂出厂，且不再各自拼发丝边与空态面板（拼一遍就多一处 z 序可写错的地方）。
        var tasks = Repo.Source("Desktop", "Native", "TasksPage.cs");
        var projects = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        Assert.Matches(@"(?m)^\s*_split\.Panel1\.Controls\.Add\(Ui\.ListHost\(_list, _empty\)\);$", tasks);
        Assert.Matches(@"(?m)^\s*Controls\.Add\(Ui\.ListHost\(_list, _empty\)\);$", projects);
        foreach (var source in new[] { tasks, projects })
        {
            Assert.DoesNotContain("Ui.Hairline(_list)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Controls.Add(_empty)", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DesignBaseline_RecordsTheListHostShape()
    {
        // 设计基线回写：形状与 z 序口径写进 §6，下一轮改这一块时先看这里。
        var design = Repo.Source("docs", "DESIGN.md");
        Assert.Contains("Ui.ListHost", design, StringComparison.Ordinal);
        // 断言取 §6 里那一行的实际标题与关键口径（标题写「清单宿主与空态叠放」，正文写「空态面板随后加入并显式 BringToFront()」）。
        Assert.Contains("清单宿主与空态叠放", design, StringComparison.Ordinal);
        Assert.Contains("空态面板随后加入并显式 `BringToFront()` 提到最前", design, StringComparison.Ordinal);
    }
}
