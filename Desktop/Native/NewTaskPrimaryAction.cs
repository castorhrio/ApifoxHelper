namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「新建任务」对话框里那两颗创建按钮（「创建并扫描」/「仅创建」）此刻该不该可点（第四百一十五轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：这个对话框此前那两颗按钮<b>永远可点</b>——一份配置都没有时点下去，
/// <c>Confirm</c> 读到 <c>_profile.SelectedItem</c> 不是 <c>ProfileItem</c>，往状态行写一句
/// 「请选择项目配置。」就返回了。屏上是一个空下拉、一个空模块清单、一句「先选一份配置」，
/// 而两颗创建按钮照样亮着——<b>它们指向一个此刻不可能成功的动作</b>。
/// 这与第四十六轮「文字不指向不存在的按钮」、第五十轮「提示所指引的恢复路径不可达」是同一族：
/// 一颗按钮可点，点下去却只能得到一句「请选择项目配置」，而屏上根本没有可选的配置。
/// </para>
/// <para>
/// <b>这一份是什么</b>：这条判定的唯一落点——<see cref="CanCreate"/> 收「下拉里有没有选中一份配置」，
/// 答「两颗创建按钮此刻该不该可点」。界面层不自己写 <c>Enabled = _profile.Items.Count &gt; 0</c>，
/// 也不自己判「清单空不空」。
/// </para>
/// <para>
/// <b>为什么「没选中」与「清单空」是同一件事</b>：下拉的默认选中项由 <see cref="NewTaskProfileChoice.Index"/>
/// 判——清单里有配置时它一定选中第一项（或刚保存的那一份），所以「没选中」只会在清单为空时出现。
/// 入参收成一个布尔，是因为界面层真的只知道这一件事（<c>_profile.SelectedItem is ProfileItem</c>）；
/// 再拆一个「清单空不空」进来，两处判据就会各说一套。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「用户会不会改下拉」（那是用户的事）；不判「取消」那颗（它永远可点）；
/// 也不判真机上这两颗按钮停用之后键盘落在哪（随 G-D5 真机复核）。
/// </para>
/// </summary>
public static class NewTaskPrimaryAction
{
    /// <summary>
    /// 两颗创建按钮此刻该不该可点：<b>下拉里选中了一份配置</b>时才可点。
    /// <para>
    /// 没选中时点下去只会得到一句「请选择项目配置」——而屏上根本没有可选的配置，
    /// 所以那颗按钮该停用，而不是让人点一次再被拒。
    /// </para>
    /// </summary>
    public static bool CanCreate(bool hasProfile) => hasProfile;
}
