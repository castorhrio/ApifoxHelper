using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 补充说明对话框保存主按钮状态断言（第四十一轮，DESIGN.md §7「执行中/禁用」条款）：
/// 保存主按钮此前在 SaveCurrent 开始时禁用，此后没有任何恢复路径——一次保存（成功或被拒）后
/// 按钮残留禁用，无法继续保存其他接口、也无法重试失败保存。修复口径：门控收敛为 UpdateSaveState
/// 单一口径（选中接口 + 已保存补充可读 + 不在保存中），构造（初始禁用，等数据）、ReloadEditors
/// （选中变化/放弃修改/保存成功）与 MarkDirty（保存被拒后恢复）三处接线；清空选中后保存随放弃
/// 一起禁用并说明缺什么；执行中禁用的锚点保留。接线由行首正则机械检查钉住（被注释掉也算删除）；
/// 真机观感随 G-D5 复核。
/// </summary>
public class SupplementSaveStateTests
{
    [Fact]
    public void SaveButton_GatedByOneSourceOfTruth()
    {
        var source = Repo.Source("Desktop", "Native", "SupplementsDialog.cs");
        // 门控单一口径与表达式（启用值由状态推导，不得写死）。
        Assert.Matches(@"(?m)^\s*private void UpdateSaveState\(\) =>$", source);
        Assert.Matches(@"(?m)^\s*_saveButton\.Enabled = Current\(\) is not null && _supplement is not null && !_saving;", source);
        Assert.DoesNotMatch(@"(?m)^\s*_saveButton\.Enabled = true;", source);
        // 三处接线：构造、ReloadEditors（选中变化/放弃/保存成功）、MarkDirty（保存被拒恢复）。
        Assert.Equal(3, Regex.Matches(source, @"(?m)^\s*UpdateSaveState\(\);").Count);
        // 清空选中后如实禁用并说明缺什么（DESIGN.md §7）。
        Assert.Matches(@"(?m)^\s*SetStatus\(""选择接口后可编辑与保存说明补充。"", UiTokens\.Muted\);", source);
    }

    [Fact]
    public void SaveButton_DisabledWhileRunningAndRestoredAfter()
    {
        var source = Repo.Source("Desktop", "Native", "SupplementsDialog.cs");
        // 执行中先置位再禁用（锚点：_saving 置位与禁用相邻，门控在保存期间保持禁用）。
        Assert.Matches(@"(?m)^\s*_saving = true;\s*\n\s*_saveButton\.Enabled = false;", source);
        // 保存被拒：_saving 复位后经 MarkDirty 恢复（UserException 与兜底 Exception 两条 catch 路径同型）。
        Assert.Equal(2, Regex.Matches(source, @"(?m)^\s*_saving = false;\s*\n\s*MarkDirty\(\);").Count);
        // 保存成功：_saving 复位后经 ReloadEditors 恢复。
        Assert.Matches(@"(?m)^\s*_supplement = saved;\s*\n\s*_saving = false;\s*\n\s*ReloadEditors\(\);", source);
    }

    [Fact]
    public void DesignDoc_RecordsSupplementSaveButtonState()
    {
        var section = Repo.DesignSection(8);
        Assert.Contains("补充保存主按钮", section);
        Assert.Contains("UpdateSaveState", section);
        Assert.Contains("残留禁用", section);
    }
}
