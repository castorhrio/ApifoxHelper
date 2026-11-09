namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 两列字段网格的列宽口径（DESIGN.md §6「两列字段网格」）：左列是字段名（固定宽），
/// 右列是输入框（吃掉剩余宽度）。配置编辑对话框、新建任务对话框与三个配置子对话框共用这一份。
/// <para>
/// <b>为什么要有这个文件</b>（第一百二十三轮）：这些网格此前把右列写成
/// <c>ColumnStyle(SizeType.Percent, 100)</c>，而网格是 <c>Dock = Top + AutoSize</c> 放进
/// <c>AutoScroll</c> 宿主的——百分比列宽在网格还是默认 200 宽时算过一次就再没重算，
/// 网格后来被停靠拉到 663 宽，右列仍是 <c>200 − 48（左右留白）− 120（标签列）= 32px</c>。
/// 真机实测：名称、允许回写范围、版本备注、Swagger 位置四个输入框只有 32px 宽（内部文本框 30px），
/// 而下拉框与按钮行因为自带显式宽度看着正常——「新建配置」对话框因此填不了字，主链路卡在第一步。
/// 列宽必须按网格<b>当前</b>宽度重算，且重算规则要能在 Linux 上逐值断言，故收在这里：
/// 不依赖 WinForms，以共享源码编入测试工程。
/// </para>
/// </summary>
public static class ProfileGridColumns
{
    /// <summary>标签列宽（配置编辑对话框与三个配置子对话框）。</summary>
    public const int LabelWidth = 120;

    /// <summary>标签列宽（新建任务对话框：标签更短，与既有 110 一致）。</summary>
    public const int NarrowLabelWidth = 110;

    /// <summary>
    /// 字段列的可读下限：低于这个宽度，输入框里连一个中文词都看不全。
    /// 取 96 是「一行能放下 6 个中文字符 + 光标」的量级，与 <see cref="UiTokens"/> 的间距档同源推导
    /// （<c>UiTokens.Xl × 4</c>），不是随手挑的数。
    /// </summary>
    public const int MinFieldWidth = 96;

    /// <summary>
    /// 字段控件（输入框/下拉框）自身的最小宽：低于这个宽度，一行提示语或一个路径就折成两行，
    /// 而字段列本身可能比它窄（窗口很窄时列宽按比例收缩）——此时控件保持这个宽度、由网格出横向滚动条，
    /// 不把控件压到读不出内容。
    /// <para>
    /// 此前这个数在三个配置子对话框（<c>GenerationDialog</c> / <c>ProfileAdvancedDialog</c> /
    /// <c>ProfileAnalysisDialog</c>）的 <c>AddRow</c> 里各写一遍 <c>Math.Max(field.Width, 480)</c>——
    /// 同一个数三处字面重复，改一处就三处不一致（第一百二十四轮收口）。现在只在这里出现，
    /// 界面代码引用本常量，机械检查钉住「界面代码不出现字面 480」。
    /// </para>
    /// </summary>
    public const int MinFieldBoxWidth = 480;

    /// <summary>
    /// 按可用宽度算各列宽（像素）。<paramref name="totalWidth"/> 是网格自身宽度，
    /// <paramref name="pad"/> 是网格左右留白合计（列宽可用的是 <c>totalWidth − pad</c>）。
    /// <list type="bullet">
    /// <item>够宽：各列取最小宽，余量全给最后一列（即「字段列吃掉剩余宽度」）。</item>
    /// <item>不够宽：按最小宽的比例收缩——同一比例，不把某一列单独压成 0。</item>
    /// <item>可用宽度 ≤ 0（宿主还没布局）：全 0，调用方据此跳过这一次重算。</item>
    /// </list>
    /// 任何情况下各列之和恰等于可用宽度（可用宽度为正时），不出现负数。
    /// </summary>
    public static int[] ColumnWidths(int totalWidth, IReadOnlyList<int> minWidths, int pad)
    {
        ArgumentNullException.ThrowIfNull(minWidths);
        var count = minWidths.Count;
        var widths = new int[count];
        if (count == 0) return widths;

        var usable = totalWidth - Math.Max(0, pad);
        if (usable <= 0) return widths;

        // 最小宽合计用 long：极端输入（每列都给 int.MaxValue）在 int 上会溢出成负数，
        // 于是「没有最小宽约束」那条分支被误判命中、各列之和不再等于可用宽度（第一百二十五轮）。
        long minimum = 0;
        for (var i = 0; i < count; i++) minimum += Math.Max(0, minWidths[i]);
        if (minimum <= 0)
        {
            // 没有最小宽约束：全部给最后一列（与「字段列吃剩余宽度」同一口径）。
            widths[count - 1] = usable;
            return widths;
        }

        if (minimum <= usable)
        {
            for (var i = 0; i < count; i++) widths[i] = Math.Max(0, minWidths[i]);
            widths[count - 1] += (int)(usable - minimum);
            return widths;
        }

        // 不够宽：按最小宽比例收缩。整数除法会丢余数，按小数部分从大到小补回，
        // 保证各列之和恰为可用宽度（否则网格右侧会多出一条缝）。
        var remainders = new long[count];
        var assigned = 0;
        for (var i = 0; i < count; i++)
        {
            var scaled = (long)usable * Math.Max(0, minWidths[i]);
            widths[i] = (int)(scaled / minimum);
            remainders[i] = scaled % minimum;
            assigned += widths[i];
        }
        var order = Enumerable.Range(0, count).OrderByDescending(i => remainders[i]).ThenBy(i => i).ToArray();
        // 补余数的循环**有界于列数**（第一百二十五轮）：整数除法每列丢掉的余数都小于 1px，
        // 合计必然小于列数，所以按小数部分从大到小走一遍就够。此前是环形步进（取模回绕、
        // 终止只靠 assigned 递增）——读代码看不出上界，输入偏斜时也无法从形状上排除多转（缺陷 2）。
        for (var k = 0; k < count && assigned < usable; k++)
        {
            widths[order[k]]++;
            assigned++;
        }
        // 兜底：上面的上界是算术结论，万一不成立（极端输入的取整意外）也不能让各列之和
        // 少于可用宽度——差额全给最后一列。「各列之和恰等于可用宽度」这条不变量优先于
        // 「余数按小数部分分配」这条分配规则。
        if (assigned < usable) widths[count - 1] += usable - assigned;

        // 不出现 0 列：可用宽度至少能每列 1px 时，从最宽的一列挪过来。
        if (usable >= count)
        {
            for (var i = 0; i < count; i++)
            {
                if (widths[i] > 0) continue;
                var donor = 0;
                for (var j = 1; j < count; j++) if (widths[j] > widths[donor]) donor = j;
                if (widths[donor] <= 1) break;
                widths[donor]--;
                widths[i]++;
            }
        }
        return widths;
    }
}
