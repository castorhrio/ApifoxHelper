using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 差异确认对话框筛选空态断言（第四十二轮，DESIGN.md §7「禁用」条款与 §8 条款）：
/// 筛选器此前在筛选结果为空时被共享提示出口（ShowHint）一并禁用——点开一个没有条目的分类筛选、
/// 或把「待处理」下最后一条确认完，提示语「换一个筛选再试」与被禁用的筛选器自相矛盾，
/// 对话框锁死在本状态只能关闭重开。修复口径：打开期四个无数据门控在提示前显式禁用筛选器
/// （与检查结果对话框同款）；运行期筛选为空时显式保持筛选器可用（恢复路径本身不禁用）；
/// ShowHint 不触碰筛选状态。接线由行首正则机械检查钉住（被注释掉也算删除）；真机观感随 G-D5 复核。
/// </summary>
public class DecisionsFilterStateTests
{
    [Fact]
    public void EmptyFilter_KeepsFilterEnabledAsRecoveryPath()
    {
        var source = Repo.Source("Desktop", "Native", "DecisionsDialog.cs");
        // 空清单分支：先分清是「整份数据没有条目」还是「当前筛选不命中」（第五十九轮接进共享口径），
        // 再显式保持筛选可用，最后提示——提示所指引的恢复路径必须可达。
        // （正则允许语句前有注释行；语句本身被注释掉时行首是 //，仍然算删除。）
        Assert.Matches(
            @"(?m)^\s*if \(_list\.Items\.Count == 0\)\s*\n\s*\{\s*\n(?:\s*//[^\n]*\n)*\s*var filteredEmpty = ListFilter\.IsFilteredEmpty\(_entries\.Count, _list\.Items\.Count\);\s*\n\s*_filter\.Enabled = true;\s*\n\s*_keyword\.Enabled = true;\s*\n\s*ShowHint\(filteredEmpty",
            source);
        // 第六十五轮起这一屏还有关键词框：筛到零行那句话的下一步因此有两个真实入口（关键词或筛选）。
        Assert.Matches(@"(?m)^\s*\? ListFilter\.FilteredEmptyHint\(ListFilter\.DecisionNoun, ListFilter\.IsActive\(_keyword\.Text\)\)$", source);
        Assert.Matches(@"(?m)^\s*: ""没有需要处理的差异确认。"",$", source);
        Assert.Matches(@"(?m)^\s*keepAggregate: filteredEmpty\);$", source);
        // 判定先于两处触达：筛选器启停与提示文案都取同一个判定结果，不各判一次。
        // 比较只在这个空清单分支之内做——OnLoad 里也有一句 _filter.Enabled = true，全文件第一次不算数。
        var branch = source.IndexOf("if (_list.Items.Count == 0)", StringComparison.Ordinal);
        var judged = source.IndexOf("var filteredEmpty = ListFilter.IsFilteredEmpty", StringComparison.Ordinal);
        var touched = source.IndexOf("_filter.Enabled = true;", branch, StringComparison.Ordinal);
        Assert.True(branch >= 0 && judged >= 0 && judged < touched, "应先判定是哪一种空，再动筛选器。");
    }

    [Fact]
    public void LoadGates_DisableFilterBeforeHint()
    {
        var source = Repo.Source("Desktop", "Native", "DecisionsDialog.cs");
        // 打开期四个无数据门控（读取失败/尚未检查/已保存决策读取失败/没有条目）：先禁用筛选器与关键词框再提示
        // （第六十五轮起这一屏多了关键词框，两个筛选入口一起停用——只停一个等于留一个按不动的入口）。
        Assert.Equal(4, Regex.Matches(source, @"(?m)^\s*_filter\.Enabled = false;\s*\n\s*_keyword\.Enabled = false;\s*\n\s*ShowHint\(").Count);
    }

    [Fact]
    public void ShowHint_DoesNotTouchFilterState()
    {
        var source = Repo.Source("Desktop", "Native", "DecisionsDialog.cs");
        // 共享提示出口不触碰筛选器：启停由调用方按状态决定，空筛选的恢复路径不得被出口连坐禁用。
        var start = source.IndexOf("private void ShowHint", StringComparison.Ordinal);
        var end = source.IndexOf("/// <summary>计数行按全部条目统计", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "DecisionsDialog.cs 缺少 ShowHint 方法或其后续锚点。");
        var body = source[start..end];
        Assert.DoesNotMatch(@"(?m)^\s*_filter\.Enabled", body);
    }

    [Fact]
    public void DesignDoc_RecordsDecisionsFilterEmptyState()
    {
        var section = Repo.DesignSection(8);
        Assert.Contains("差异确认筛选空态", section);
        Assert.Contains("换一个筛选再试", section);
        Assert.Contains("禁用不得移除下一步", section);
    }
}
