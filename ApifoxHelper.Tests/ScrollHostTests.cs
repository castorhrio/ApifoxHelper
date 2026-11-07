using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 长内容区可滚动宿主断言（DESIGN.md §6「长内容区」）：任务详情、补充说明编辑区的内容高于可视高度，
/// 必须放在 <see cref="ApifoxHelper.Desktop.Native.Ui"/> 的 ScrollHost（AutoScroll 面板）里，
/// 否则底部内容被裁掉且无滚动条可达（第三十一轮前的真实缺陷：同步/报告动作在任务详情底部不可达）。
/// 呈现接线由源码机械检查钉住（行首正则——接线被注释掉也算删除）；滚轮取焦与滚动行为的真机观感随 G-D5 复核。
/// </summary>
public class ScrollHostTests
{
    [Fact]
    public void Factory_EnablesAutoScrollAndStaysOutOfTabOrder()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Contains("public static Panel ScrollHost() => new ScrollPanel();", ui);
        // 内容超高必须出滚动条——这是本切片要修的缺陷本体。
        Assert.Matches(@"(?m)^\s*AutoScroll = true;", ui);
        Assert.Contains("BackColor = UiTokens.Bg;", ui);
        // 宿主只是滚动容器，不参与 Tab 序。
        Assert.Matches(@"(?m)^\s*TabStop = false;", ui);
    }

    [Fact]
    public void Host_TakesFocusOnClickAnywhere()
    {
        // 子项多为不接收焦点的标签，且内容盖住宿主可视面时宿主自己的 MouseDown 收不到——
        // 取焦必须递归挂到子树上（可自取焦的按钮与输入框除外）。行首正则断言：被注释掉也算删除，变异可红。
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Matches(@"(?m)^\s*ControlAdded \+= \(_, e\) => \{ if \(e\.Control is not null\) HookFocus\(e\.Control\); \};", ui);
        Assert.Matches(@"(?m)^\s*private void HookFocus\(Control control\)", ui);
        Assert.Matches(@"(?m)^\s*if \(control is ButtonBase or TextBoxBase\) return;", ui);
        Assert.Matches(@"(?m)^\s*control\.MouseDown \+= \(_, _\) => Focus\(\);", ui);
        Assert.Matches(@"(?m)^\s*foreach \(Control child in control\.Controls\) HookFocus\(child\);", ui);
        Assert.Matches(@"(?m)^\s*control\.ControlAdded \+= \(_, e\) => \{ if \(e\.Control is not null\) HookFocus\(e\.Control\); \};", ui);
        Assert.Matches(@"(?m)^\s*protected override void OnMouseDown\(MouseEventArgs e\)", ui);
        Assert.Matches(@"(?m)^\s*Focus\(\);", ui);
    }

    [Fact]
    public void DesignDoc_DeclaresLongContentHost()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("长内容区", section);
        Assert.Contains("`Ui.ScrollHost`", section);
    }

    [Fact]
    public void TasksPage_Detail_IsInsideScrollHost()
    {
        var tasks = Repo.Source("Desktop", "Native", "TasksPage.cs");
        // 详情区经工厂出厂并被放入 Panel2（旧写法直接停靠 Panel2，超高即被裁掉）。
        Assert.Matches(@"(?m)^\s*private readonly Panel _detailScroll = Ui\.ScrollHost\(\);", tasks);
        Assert.Matches(@"(?m)^\s*_detailScroll\.Controls\.Add\(_detail\);", tasks);
        Assert.Matches(@"(?m)^\s*_split\.Panel2\.Controls\.Add\(_detailScroll\);", tasks);
        Assert.DoesNotMatch(@"(?m)^\s*_split\.Panel2\.Controls\.Add\(_detail\);", tasks);
        // 可见性切换作用于滚动宿主；详情面板本身不再被直接切换可见性。
        Assert.Matches(@"(?m)^\s*_detailScroll\.Visible = (true|false);", tasks);
        Assert.DoesNotMatch(@"(?m)^\s*_detail\.Visible = (true|false);", tasks);
        // 详情网格保持 Dock=Top + AutoSize：滚动范围来自内容自身高度，改成 Dock=Fill 会退回不可滚动。
        var build = tasks[(tasks.IndexOf("private TableLayoutPanel BuildDetail()", StringComparison.Ordinal))..];
        Assert.Contains("new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true", build);
    }

    [Fact]
    public void SupplementsDialog_Editors_UseSameScrollHost()
    {
        // 编辑区宿主收敛为同一工厂：AutoScroll、点击取焦的口径只有一份。
        var supplements = Repo.Source("Desktop", "Native", "SupplementsDialog.cs");
        Assert.Matches(@"(?m)^\s*private readonly Panel _editorsScroll = Ui\.ScrollHost\(\);", supplements);
        Assert.Matches(@"(?m)^\s*_editorsScroll\.Dock = DockStyle\.Fill;", supplements);
    }

    [Fact]
    public void ProfileAndNewTaskDialogs_Grids_UseScrollHost()
    {
        // 配置编辑（十七行字段与提示、底部四个子配置入口与导出）与新建任务（十二行、底部离线模式）
        // 的网格内容高于对话框可视高度：必须放入可滚动宿主，否则底部行被裁掉且 FixedDialog 无法拉高补救
        //（第三十二轮前的真实缺陷，与第三十一轮任务详情同类）。
        var projects = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        Assert.Matches(@"(?m)^\s*var gridScroll = Ui\.ScrollHost\(\);", projects);
        Assert.Matches(@"(?m)^\s*gridScroll\.BackColor = UiTokens\.Panel;", projects);
        Assert.Matches(@"(?m)^\s*gridScroll\.Controls\.Add\(grid\);", projects);
        Assert.Matches(@"(?m)^\s*Controls\.Add\(gridScroll\);", projects);
        // 旧直连写法（网格直接停靠窗体）不得回归；网格保持 Dock=Top + AutoSize（Dock=Fill 退回不可滚动）。
        Assert.DoesNotMatch(@"(?m)^\s*Controls\.Add\(grid\);", projects);
        Assert.Contains("new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = UiTokens.Panel", projects);

        var tasks = Repo.Source("Desktop", "Native", "TasksPage.cs");
        Assert.Matches(@"(?m)^\s*var gridScroll = Ui\.ScrollHost\(\);", tasks);
        Assert.Matches(@"(?m)^\s*gridScroll\.Controls\.Add\(grid\);", tasks);
        Assert.Matches(@"(?m)^\s*Controls\.Add\(gridScroll\);", tasks);
        Assert.DoesNotMatch(@"(?m)^\s*Controls\.Add\(grid\);", tasks);
        Assert.Contains("new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = UiTokens.Panel", tasks);
    }

    [Fact]
    public void ConfigDialogs_Grids_UseScrollHostFactoryOnly()
    {
        // 三个配置子对话框此前在 TableLayoutPanel 上就地开 AutoScroll（无点击取焦）——
        // DESIGN.md §6 要求长内容区宿主一律经 Ui.ScrollHost 工厂；AutoScroll 的口径只在工厂内。
        foreach (var name in new[] { "GenerationDialog.cs", "ProfileAdvancedDialog.cs", "ProfileAnalysisDialog.cs" })
        {
            var source = Repo.Source("Desktop", "Native", name);
            Assert.Matches(@"(?m)^\s*var gridScroll = Ui\.ScrollHost\(\);", source);
            Assert.Matches(@"(?m)^\s*gridScroll\.BackColor = UiTokens\.Panel;", source);
            Assert.Matches(@"(?m)^\s*gridScroll\.Controls\.Add\(grid\);", source);
            Assert.Matches(@"(?m)^\s*Controls\.Add\(gridScroll\);", source);
            Assert.DoesNotMatch(@"(?m)^\s*Controls\.Add\(grid\);", source);
            Assert.DoesNotMatch(@"(?m)^\s*AutoScroll\b", source);
            Assert.Contains("new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = UiTokens.Panel", source);
        }

        // 工厂自身的 AutoScroll 仍在（口径唯一落点）。
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Matches(@"(?m)^\s*AutoScroll = true;", ui);
    }
}
