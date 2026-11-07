using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 列表双击激活断言（DESIGN.md §6「列表行」双击条款，第三十九轮）：ListView.DoubleClick 不区分命中行，
/// 配置列表双击空白区（首击清空选中）会触发事件并借当时的选中（已为空）打开「新建配置」对话框。
/// 修复口径：激活只经 Ui.ActivateItem 唯一入口——MouseDoubleClick + HitTest 守卫，命中哪行激活哪行，
/// 双击空白区不动作。接线由行首正则机械检查钉住（被注释掉也算删除）；真机观感随 G-D5 复核。
/// </summary>
public class ListActivationTests
{
    [Fact]
    public void Ui_ActivateItem_GuardsByHitTest()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Matches(@"(?m)^\s*public static void ActivateItem\(ListView list, Action<ListViewItem> activate\)", ui);
        Assert.Matches(@"(?m)^\s*list\.MouseDoubleClick \+= \(_, e\) =>", ui);
        // 空白区命中（Item 为 null）必须短路：守卫删除即回归为「不区分命中行」的旧行为。
        Assert.Matches(@"(?m)^\s*if \(list\.HitTest\(e\.Location\)\.Item is not ListViewItem item\) return;", ui);
        Assert.Matches(@"(?m)^\s*activate\(item\);", ui);
    }

    [Fact]
    public void ProjectsPage_ActivatesOnlyViaSharedEntry()
    {
        var projects = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        // 双击只经工厂入口：命中行的 Tag（该行自身的配置）进入编辑，不再借当前选中。
        Assert.Matches(
            @"(?m)^\s*Ui\.ActivateItem\(_list, item => ShowDialog\(item\.Tag as ProjectProfile\)\);",
            projects);
        // 裸 DoubleClick 不区分命中行，不得回归。
        Assert.DoesNotContain("_list.DoubleClick", projects, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_DeclaresDoubleClickRule()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("双击", section);
        Assert.Contains("ActivateItem", section);
        Assert.Contains("空白区", section);
    }
}
