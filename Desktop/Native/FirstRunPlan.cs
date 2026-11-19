namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 首程（一台什么都没装的机器上第一次打开）任务页空态那颗按钮该指向哪一步（第四百零五轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：任务页空态的说明写着「先在「项目设置」新建配置，然后点「新建任务」开始源码扫描」，
/// 而空态上<b>唯一那颗按钮</b>是「新建任务」。第一次打开的人照着屏上唯一可点的东西按下去，
/// <c>ShowNewTaskDialog</c> 读到配置清单是空的，往状态行写一句「还没有项目配置。请先在「项目设置」新建配置，
/// 再创建任务。」就返回了——<b>屏上没有任何一步能到「项目设置」</b>：侧栏那颗导航按钮在，但空态那句话
/// 与那颗按钮都没有把用户送过去，用户得自己想到「去点左边那一栏」。这与第四十二轮「禁用不得移除下一步」、
/// 第五十轮「提示所指引的恢复路径不可达」、第四十六轮「文字不指向不存在的按钮」是同一族：
/// <b>这一次是「空态唯一那颗按钮点下去被拒，而拒绝之后屏上没有下一步」</b>。
/// </para>
/// <para>
/// <b>这一份是什么</b>：首程那一步的唯一落点——两颗按钮的文字（<see cref="NewTaskLabel"/> /
/// <see cref="NewProfileLabel"/>，两页共用，不各写一份字面）、空态那颗按钮该指向哪一步
/// （<see cref="EmptyActionFor"/>）、它此刻该写什么字（<see cref="Label"/>）、空态说明该说哪一句
/// （<see cref="Hint"/>）、以及「没有配置时点『新建任务』」那一句状态行（<see cref="NoProfileStatus"/>）。
/// 界面层不就地拼这几处文字，也不自己判「配置清单空不空」。
/// </para>
/// <para>
/// <b>为什么「认不出」按「有配置」算</b>：<see cref="EmptyActionFor"/> 收的是 <c>int?</c>——
/// 配置清单读不出来时（磁盘被占用、权限不足、某份 JSON 坏了）调用方给 <c>null</c>。
/// 那时<b>不</b>把按钮换成「新建配置」：这一屏连「有没有配置」都还不知道，说成「还没有配置」是把
/// 「读不出来」说成「读到了空」（与 <see cref="ProfileLoadState"/> 那一族同一取舍）。
/// 走「新建任务」那条路时，<c>ShowNewTaskDialog</c> 自己会把读取失败的原因写进状态行——
/// 那是如实说，不是猜。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「配置页那个新建对话框长什么样」（那是 <c>ProfileDialog</c> 自己的事）；
/// 不判真机上切页之后键盘落在哪、对话框弹出来的位置（随 G-D5 真机复核）。
/// </para>
/// </summary>
public static class FirstRunPlan
{
    /// <summary>「新建任务」的文字（<b>唯一落点</b>：任务页工具栏那颗实心主按钮、空态那颗 ghost 与说明文字都取它）。</summary>
    public const string NewTaskLabel = "新建任务";

    /// <summary>「新建配置」的文字（<b>唯一落点</b>：配置页工具栏那颗实心主按钮、空态那颗 ghost 与首程那一跳都取它）。</summary>
    public const string NewProfileLabel = "新建配置";

    /// <summary>空态那颗按钮此刻指向哪一步。</summary>
    public enum EmptyAction
    {
        /// <summary>打开「新建任务」对话框（已经有配置了，缺的只是任务）。</summary>
        NewTask,

        /// <summary>切到项目设置页并打开「新建配置」对话框（首程：连配置都还没有）。</summary>
        NewProfile,
    }

    /// <summary>
    /// 空态那颗按钮该指向哪一步：<b>一份配置都没有</b>时指向「新建配置」，其余（含认不出）指向「新建任务」。
    /// <para>
    /// 认不出（<c>null</c>）按「有配置」算的理由见类注释——不把「读不出来」说成「还没有配置」。
    /// </para>
    /// </summary>
    public static EmptyAction EmptyActionFor(int? profileCount) =>
        profileCount == 0 ? EmptyAction.NewProfile : EmptyAction.NewTask;

    /// <summary>那一步对应的按钮文字（与 <see cref="EmptyActionFor"/> 同一份判定，界面不另判一次）。</summary>
    public static string Label(EmptyAction action) =>
        action == EmptyAction.NewProfile ? NewProfileLabel : NewTaskLabel;

    /// <summary>
    /// 空态说明那一句：<b>指名的入口必须是此刻屏上真的那一颗</b>（第四十二轮纪律）。
    /// <para>
    /// 首程那一支说的是「点「新建配置」建一份，再回来点「新建任务」」——两步都在屏上按得出来：
    /// 第一步就是空态那颗按钮，第二步是它建完配置回来之后同一位置换回的那一颗。
    /// 有配置那一支保持原句不变（那时空态那颗按钮本来就是「新建任务」）。
    /// </para>
    /// </summary>
    public static string Hint(EmptyAction action) => action == EmptyAction.NewProfile
        ? "还没有项目配置：点「" + NewProfileLabel + "」建一份，再回来点「" + NewTaskLabel + "」开始源码扫描。"
        : "先在「" + PageNames.Projects + "」新建配置，然后点「" + NewTaskLabel + "」开始源码扫描。";

    /// <summary>
    /// 一份配置都没有时点「新建任务」，状态行说的那一句（<b>唯一落点</b>：界面与冒烟段都读它）。
    /// <para>
    /// 说的是「还没有配置」与「先建配置」两件事，不冒充「创建失败」——这一次根本没有走到创建那一步。
    /// 页面名取 <see cref="PageNames.Projects"/>：侧栏那颗按钮上的字与这句话同源（第二百五十七轮那一族）。
    /// </para>
    /// </summary>
    public const string NoProfileStatus = "还没有项目配置。请先在「" + PageNames.Projects + "」新建配置，再创建任务。";
}
