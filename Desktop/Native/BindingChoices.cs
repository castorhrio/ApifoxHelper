namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 模型绑定下拉的一项：<see cref="Value"/> 是写进 decisions.json 的值（""=未确认、远端模型 ID、"new"=新建模型），
/// <see cref="Text"/> 是屏幕上读到的那一行。显示文字一律取 <see cref="TaskDisplay.ModelBindingText"/>，
/// 与差异确认清单「状态」列同一份口径——下拉与状态列不能对同一件事说两种话。
/// </summary>
public readonly record struct BindingChoice(string Value, string Text)
{
    public override string ToString() => Text;
}

/// <summary>
/// 差异确认对话框「模型绑定」下拉的选项口径（不依赖 WinForms，以共享源码编入测试工程在 Linux 断言）。
///
/// 缺口（第七十四轮）：已保存的绑定若不在本次检查的候选里（远端模型被删或改名后重新检查、候选集变化），
/// 旧实现 `FindIndex` 落空后一律 `SelectedIndex = 0`——下拉显示「未确认」，而同一屏的「状态」列显示
/// 「绑定远端 42」，两处对同一件事说两种话；更要紧的是保存按钮按「选中值 ≠ 已保存值」启用，
/// 于是「未确认」≠「42」使按钮亮着，一次点击就把用户从未打算改的绑定静默删掉。
///
/// 同壳里三个配置对话框的同类下拉（模块与模板的接口状态、模型服务的生成方式、序列化与分析的 JSON 命名）
/// 早就按「未知取值按原值追加一项显示，不静默改值」处理，这一处是第四个同类下拉、也是唯一会静默改值的。
/// 故本文件把「选项清单 + 选中项 + 保存按钮是否可用」收成一处：已保存但不在候选里的值按原值追加一项并注明
/// 「（不在本次候选）」，选中项就是它，保存按钮因此不可用——下拉说的是事实，一次点击也不会抹掉它。
/// </summary>
public static class BindingChoices
{
    /// <summary>「未确认」的取值（写进 decisions.json 时表示移除该模型的绑定）。</summary>
    public const string UnconfirmedValue = "";

    /// <summary>「新建模型」的取值（DecisionService 认可的两种取值之一）。</summary>
    public const string NewModelValue = "new";

    /// <summary>已保存但不在本次候选里的那一项的注记（说明它为什么不在候选清单上）。</summary>
    public const string NotCandidateSuffix = "（不在本次候选）";

    /// <summary>下拉的选项清单与选中项（选中项由构造保证落在清单之内，调用方不再自己兜底）。</summary>
    public readonly record struct Options(IReadOnlyList<BindingChoice> Choices, int SelectedIndex);

    /// <summary>
    /// 构造选项清单：未确认 → 候选（按候选顺序、同一远端 ID 只出现一次）→ 新建模型 →
    /// 已保存但不在候选里的值（按原值追加，不静默改值）。选中项是已保存的那一项。
    /// </summary>
    public static Options Build(IEnumerable<long> candidates, string? saved)
    {
        var choices = new List<BindingChoice>
        {
            new(UnconfirmedValue, TaskDisplay.ModelBindingText(UnconfirmedValue)),
        };
        var seen = new HashSet<long>();
        foreach (var id in candidates)
        {
            if (!seen.Add(id)) continue;
            var value = id.ToString(CultureInfo.InvariantCulture);
            choices.Add(new BindingChoice(value, TaskDisplay.ModelBindingText(value)));
        }
        choices.Add(new BindingChoice(NewModelValue, TaskDisplay.ModelBindingText(NewModelValue)));

        var current = saved ?? "";
        if (current.Length > 0 && choices.All(c => c.Value != current))
        {
            // 已保存的绑定不在本次候选里：按原值追加一项并注明，不把它显示成「未确认」。
            choices.Add(new BindingChoice(current, TaskDisplay.ModelBindingText(current) + NotCandidateSuffix));
        }

        var index = 0;
        for (var i = 0; i < choices.Count; i++)
        {
            if (choices[i].Value == current)
            {
                index = i;
                break;
            }
        }
        return new Options(choices, index);
    }

    /// <summary>
    /// 保存按钮是否可用：选中值与已保存值不同才可用（同一份比较，不在对话框里另写一份）。
    /// 选中项缺失（下拉被清空、没有选中）时不可用——不猜「没有选中」等于「未确认」。
    /// </summary>
    public static bool CanSave(string? selected, string? saved) =>
        selected is not null && selected != (saved ?? "");
}
