namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「新建任务」对话框打开时，<b>「项目配置」下拉默认选中的是哪一份</b>（第四百零八轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：这个下拉此前一律 <c>SelectedIndex = 0</c>——而 <c>ProfileStore.List()</c> 是按
/// <b>配置名称</b>排的（<c>OrderBy(p =&gt; p.Name, StringComparer.CurrentCulture)</c>），不是按创建时间。
/// 于是「刚在项目设置页建好（或刚改好）的那一份」与「下拉里默认选中的那一份」是两件事：
/// 首程那条路上用户刚建完配置、回到任务页点「新建任务」，只要清单里还有一份名字排在它前面的配置，
/// 对话框默认选中的就是<b>别人</b>。用户照着默认值点「创建并扫描」，任务就绑到了另一份配置上——
/// 扫的是另一个仓库、写的是另一个 Apifox 项目。这不是观感问题：<b>默认值错了，一次回车就把任务建到错的项目上</b>。
/// </para>
/// <para>
/// <b>这一份是什么</b>：这条判定的唯一落点——<see cref="Index"/> 收「清单里各份配置的 Id（按清单顺序）」与
/// 「用户刚保存的那一份 Id」，答「下拉该选中第几项」。界面层不自己写 <c>SelectedIndex = 0</c>，
/// 也不自己判「那一份还在不在清单里」。
/// </para>
/// <para>
/// <b>为什么「认不出」回退到第一项而不是不选</b>：<see cref="Index"/> 收的 <c>preferredId</c> 可能是
/// <c>null</c>（这一次启动还没保存过任何配置）、也可能指向一份<b>已经被删掉</b>的配置。
/// 两种情形都不该让下拉空着——空着的话「创建并扫描」点下去只会得到一句「请选择项目配置」，
/// 而清单里明明有配置可选。回退到第一项就是<b>这一屏此前的行为</b>：认不出时不变差，认得出时才变好。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「清单该按什么排」（那是 <c>ProfileStore.List</c> 的事，这一轮不动它）；
/// 不判「用户会不会改下拉」（那是用户的事）；也不判真机上这个下拉弹开之后键盘落在哪（随 G-D5 真机复核）。
/// </para>
/// </summary>
public static class NewTaskProfileChoice
{
    /// <summary>清单是空的：没有可选项（调用方据此不设 <c>SelectedIndex</c>）。</summary>
    public const int None = -1;

    /// <summary>
    /// 下拉该选中第几项：<b>刚保存的那一份还在清单里就选它</b>，否则选第一项；清单为空时 <see cref="None"/>。
    /// <para>
    /// 比较按 <see cref="StringComparison.Ordinal"/>：配置 Id 是落盘文件名，不是给人读的文字，
    /// 大小写与文化差异都不该让「同一份配置」认成两份。
    /// </para>
    /// </summary>
    public static int Index(IReadOnlyList<string> profileIds, string? preferredId)
    {
        if (profileIds.Count == 0) return None;
        if (!string.IsNullOrEmpty(preferredId))
        {
            for (var i = 0; i < profileIds.Count; i++)
            {
                if (string.Equals(profileIds[i], preferredId, StringComparison.Ordinal)) return i;
            }
        }
        return 0;
    }
}
