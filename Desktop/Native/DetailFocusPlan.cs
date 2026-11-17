namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 任务详情区「交焦点」与「换落点」的协调规则（第三百七十三轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：详情区这一族从第三百六十八轮起连做三轮——<c>BannerButtonIdentityTests</c>
/// （横幅那两颗按钮出厂一次、刷新只换字与显隐）、<c>DetailScrollResetTests</c>（换任务回到顶部、
/// 刷新同一个任务不归位）、<c>StageStepsTests</c>（点阶段跳分区之后把键盘交给那一区）。
/// 三条门各自钉住一处，而它们钉的是同一件事的三个面：<b>详情区里「焦点落在哪」与「详情停在哪」这两笔
/// 谁先谁后、由谁说了算</b>。此前这两笔散在 <c>RefreshDetail</c>（归位）、<c>JumpToStage</c>（交焦点 + 定落点）、
/// <c>FocusFirstAction</c>（挑哪一颗）三处，每一处都只知道自己那一半——下一轮再往详情区加一个入口
/// （例如「跳到报告区」），第四处就会自己再写一遍「先交焦点再滚」，而顺序写反了在 Linux 上没人看得见。
/// </para>
/// <para>
/// <b>本文件只放判定</b>（不依赖 WinForms、与测试工程共享编译，真值表在 Linux 真执行）：
/// ① 落点怎么算（<see cref="Landing"/>）；② 一行动作按钮里该把键盘交给哪一颗（<see cref="FirstSelectable"/>）。
/// 触达控件的那一步收在 <c>TasksPage.MoveDetailTo</c> 一处——它是详情区「交焦点 + 换落点」的唯一落点，
/// 顺序（先交焦点、再定落点）也只在那一处写一次。
/// </para>
/// <para>
/// <b>顺序为什么是判据</b>：反过来的话，交焦点那一下（WinForms 把焦点控件滚进视野）会把刚定好的落点顶掉——
/// 落点由那一区标题算出来，而焦点控件在标题之下，于是详情停在焦点控件那一行、标题被滚出屏外
/// （wine 96 档实测：跳到源码注释区之后按一次 Tab，详情由 −779px 回到 0px）。
/// </para>
/// </summary>
public static class DetailFocusPlan
{
    /// <summary>
    /// 跳到某一区之后的滚动落点：那一区标题的上沿再往上留一笔间距（<paramref name="gap"/>），
    /// 负值一律夹到 0——标题在可视区之上时（例如详情刚归位）不该滚出一个负偏移。
    /// </summary>
    public static int Landing(int sectionTop, int gap) => Math.Max(0, sectionTop - gap);

    /// <summary>
    /// 一行动作按钮里第一颗<b>可点</b>的那一颗的下标；一颗都不可点时返回 <c>-1</c>。
    /// <para>
    /// 一颗都不可点时不动键盘：焦点落在一个禁用控件上等于没落（<c>Focus()</c> 直接返回 false），
    /// 而把焦点硬塞给别处会让用户下一步按 Tab 时从别的地方起手——那比留在原处更难解释。
    /// </para>
    /// </summary>
    public static int FirstSelectable(IReadOnlyList<bool> canSelect)
    {
        for (var i = 0; i < canSelect.Count; i++)
        {
            if (canSelect[i]) return i;
        }

        return -1;
    }
}
