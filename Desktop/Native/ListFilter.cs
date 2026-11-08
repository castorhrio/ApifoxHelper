namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 列表筛选的口径（docs/DESIGN.md §6「列表筛选」）：任务与配置会一直累积，
/// 固定顺序下要找到「上次那个任务」「某个配置下的任务」只能逐行扫一列；两个主页面列表上方
/// 各有一个筛选框，输入即按关键词筛选行，清空回到全部（Esc 与空态的「清空筛选」按钮是同一条路的
/// 两个触发，都走各页的 ClearListFilter 唯一路径——见 <see cref="EscClears"/>）。
/// 规则：关键词按空白切成多个词，每个词都要在至少一个字段里出现（词间「且」、字段间「或」，
/// 按当前区域忽略大小写比较，空关键词即全部行；筛选先于排序（<see cref="Apply"/> → <see cref="ListSort.Order"/>）。
/// 筛选只改呈现顺序与可见行：不写任务与配置数据，选中按 Id 恢复。
/// 两个主页面之外，五个带筛选下拉的视图（注释候选、响应契约、差异确认、检查结果、注释缺口）同在册：
/// 筛到零行与整份数据没有条目是两种空，下一步不同、说法也不同（<see cref="IsFilteredEmpty"/>）。
/// 这句话只讲「列表区这一屏空了」，不规定那一屏还要留下什么——整份数据的聚合入口留在各视图自己手里
/// （前四处是计数行与筛选器；注释缺口另有覆盖率行与「分析警告与降级」「接口追踪明细」，那些讲的是整份扫描，
/// 不因当前筛选不命中而失效）。
/// 关键词框不止两个主页面有：差异视图的两张长清单（检查结果、差异确认）在第六十五轮补上同一个框——
/// 检查结果在真实项目上动辄几十上百个接口（方案 §4.2 记录千级接口分支），差异确认的条目按接口×位置展开更多，
/// 要找到「上次那个接口」只能逐行扫；冻结页面的差异视图一直有关键词框（「关键词（路径、接口名）」）。
/// 同一个动作（在长清单里按关键词找一行）在主页有、在对话框没有，属同壳不同行为，故按同一份口径收口：
/// 占位文字、匹配规则与「筛到零行那句话」都从这里取，三个对话框不各写一套。
/// 注释缺口清单在第六十六轮并入同一册：它是全应用最长的一张清单（一次扫描的全部注释位置），
/// 同样只有筛选下拉时找「上次那个位置」只能逐行扫；匹配的是那一行在屏幕上被读作的那四格
/// （符号、标注、角色、文件位置），状态那一列由下拉管、不另造一套字段。
/// 两个审阅队列在第六十七轮并入同一册：注释候选与响应契约是逐条清队列的两张清单，
/// 队列里上百条时找「上次那条」只能逐行扫（审阅状态那一列由下拉管，不进关键词）；
/// 匹配的是那一行在屏幕上被读作的那几格——候选是位置/标注/依据/来源，契约是接口/封装/data 类型，
/// 取法在 <c>QueueText</c>（与清单单元格同源，不另造一套字段）。五张带筛选下拉的清单到此都有同一个框。
/// 第六张清单在第七十二轮并入同一册：同步预览的接口勾选页（生成计划前选哪些接口）此前没有框——
/// 它是整份检查结果的接口清单，真实项目几十上百个（方案 §4.2 记录千级接口分支），要找到「上次那个接口」
/// 只能逐行扫；这一页与那五张有一处不同：**勾选状态挂在行上**，筛选要重建清单，重建就把勾选丢了，
/// 故勾选状态搬到 <c>SyncSelect</c> 按接口键记（单一来源），筛选只改可见行、不改勾选。
/// 它也是唯一没有筛选下拉的一张：状态那一列的文字因此进关键词（那五张里状态由下拉管、不进关键词，
/// 这里没有下拉可管，不进关键词就等于筛不到），筛到零行那句话也只指那个框
/// （<see cref="FilteredEmptyKeywordHint"/>：文字不指向界面上按不动的控件）。
/// 第七十五至七十七轮把三张只读长清单并入同一册（接口追踪明细、补充说明的接口清单、生成文档核对条目）：
/// 三处都没有筛选下拉，走工厂里只有关键词框的那一支，状态/预算/三环这类「这一屏唯一能一眼认出的文字」
/// 因此进关键词（有下拉的那几张里它们由下拉管、不进关键词），筛到零行那句话也只指那个框。
/// 关键词框不是表单字段：输入即筛选，没有「提交」这一步——框里的裸 Enter 不提交对话框
/// （五张清单各自在 <c>ProcessDialogKey</c> 里挡下；冻结页面的差异视图关键词框在 Enter 上同样什么都不做），
/// 带修饰键的组合不属本判定、不吞键。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class ListFilter
{
    // 文案单一落点：占位文字、空态文字与空态行动按钮的文字都从这里取，界面不就地拼写。
    public const string TaskPlaceholder = "筛选任务（名称、配置、下一步）";
    public const string ProfilePlaceholder = "筛选配置（名称、API 项目、Apifox 目标）";

    /// <summary>
    /// 检查结果的关键词框：按接口身份与远端名称找（与列表首两列同一批文字）。
    /// 补充说明的接口清单（第七十六轮）匹配的正是同样两格（接口身份 + 名称，取法在
    /// <c>SupplementEntries.KeywordFields</c>），故共用这一份文字——同一个动作在两处说同一句话，
    /// 不为「找接口」另起第二种说法。
    /// </summary>
    public const string EndpointSearchPlaceholder = "筛选接口（方法、路径、名称）";

    /// <summary>差异确认的关键词框：按条目资源、位置与类型找（与列表三列同一批文字）。</summary>
    public const string DecisionSearchPlaceholder = "筛选条目（资源、位置、类型）";

    /// <summary>
    /// 注释缺口的关键词框：按符号、标注、角色与文件位置找（与列表那四格同一批文字）。
    /// 状态不在其中——那一列的取值（缺失/空/已失效/有效/其他问题）由筛选下拉管，不必再敲一遍。
    /// </summary>
    public const string SlotSearchPlaceholder = "筛选位置（符号、标注、角色、文件）";

    /// <summary>
    /// 同步预览接口勾选页的关键词框：按接口身份、远端名称与状态文字找（与列表那三格同一批文字）。
    /// 这一页没有筛选下拉，状态那一列的文字因此进关键词——五张带下拉的清单里它由下拉管、不进关键词，
    /// 这里没有下拉可管，不进关键词就等于筛不到（「待确认」那一列是数字，不进关键词）。
    /// </summary>
    public const string SyncSelectSearchPlaceholder = "筛选接口（方法、路径、名称、状态）";

    /// <summary>
    /// 接口追踪明细的关键词框：按接口身份与预算列文字找（与列表那两格同一批文字）。
    /// 这一屏也没有筛选下拉（与同步预览的接口勾选页同一支），故「超限」这一格文字进关键词——
    /// 这一屏存在的理由正是「预算超限或调用未解析时在这里可见」，要找到那几条只能逐行扫。
    /// 三个计数列是数字、不进关键词（占位「—」不是内容，也不进）。
    /// </summary>
    public const string TraceSearchPlaceholder = "筛选接口（方法、路径、预算）";

    /// <summary>
    /// 生成文档核对条目的关键词框：按位置、标注与三环状态文字找（与列表那五格同一批文字）。
    /// 这一屏也没有筛选下拉（与接口追踪明细同一支），三环状态文字因此进关键词——
    /// 这一屏存在的理由正是「注释有没有真的写进源码、出现在 XML、被 Swagger 读到」，
    /// 要找到「XML 缺失」的那几条只能逐行扫。
    /// </summary>
    public const string PropagationSearchPlaceholder = "筛选条目（位置、标注、三环状态）";

    /// <summary>
    /// 注释候选的关键词框：按位置、标注、依据与来源找（与列表那四格同一批文字）。
    /// 审阅状态不在其中——那一列的取值由筛选下拉管，不必再敲一遍。
    /// </summary>
    public const string CandidateSearchPlaceholder = "筛选候选（位置、标注、依据、来源）";

    /// <summary>
    /// 响应契约的关键词框：按接口、封装与 data 类型找（与列表那三格同一批文字）。
    /// 审阅状态不在其中——那一列的取值由筛选下拉管，不必再敲一遍。
    /// </summary>
    public const string ContractSearchPlaceholder = "筛选契约（接口、封装、类型）";

    public const string TaskNoMatchTitle = "没有匹配的任务";
    public const string TaskNoMatchHint = "换一个关键词，或点「清空筛选」回到全部任务。";
    public const string ProfileNoMatchTitle = "没有匹配的配置";
    public const string ProfileNoMatchHint = "换一个关键词，或点「清空筛选」回到全部配置。";

    /// <summary>
    /// 筛到零行时空态给出的唯一行动按钮：提示语指名的入口必须是界面上真实存在的那一个
    /// （第四十二轮的纪律）。按钮与 Esc 同走一条清空路径，谁也不替代谁。
    /// </summary>
    public const string ClearActionLabel = "清空筛选";

    /// <summary>
    /// 这一次 Esc 是不是「退出筛选」：当前可见页的筛选框里留着关键词才算。
    /// 另一页的关键词是用户留给那一屏的，在这一屏按 Esc 不构成清掉它的理由；
    /// 关键词已空时不接这一次按键——空框里按 Esc 什么都没做，也就不把按键说成做了什么
    /// （对话框里的 Esc 仍是取消、菜单打开时仍是收起菜单，都不归本口径管）。
    /// 「焦点在不在筛选框上」不在判定之内：筛选是这一屏的状态，不是焦点落在哪个控件上的状态——
    /// Enter 把选中交给列表之后想退出筛选，正是不该回去点框的时候。
    /// </summary>
    public static bool EscClears(bool pageVisible, bool hasKeyword) => pageVisible && hasKeyword;

    /// <summary>
    /// 对话框里这一次 Esc 是不是「退出筛选」：关键词框取得焦点、且框里留着关键词才算。
    /// 与两个主页面的 <see cref="EscClears"/> 差一件事——对话框的 Esc 另有「取消」这一既有含义
    /// （DESIGN.md §6「对话框 Enter=确认、Esc=取消」），所以这里必须看焦点：焦点在筛选框上时
    /// Esc 退出筛选（筛选框不是表单字段，与第七十轮 Enter 的判定同一理由），焦点在别处时 Esc 仍是
    /// 取消对话框——一次按键只有一个含义，不因为框里留着字就把「取消」从用户手里拿走。
    /// 关键词已空时不接这一次按键（空框里按 Esc 什么都没做，也就不把按键说成做了什么），
    /// 于是「框里按 Esc 清空 → 再按一次关掉对话框」是两步，与资源管理器的搜索框同序。
    /// </summary>
    public static bool EscClearsInDialog(bool keywordFocused, bool hasKeyword) => keywordFocused && hasKeyword;

    // 这五个视图的每一行是什么：名词在这里给一份词汇表，筛到零行那句话与未选中那句话都由它组合，界面不就地拼写。
    public const string CandidateNoun = "候选";
    public const string ContractNoun = "契约";
    public const string EndpointNoun = "接口";
    public const string DecisionNoun = "条目";

    /// <summary>注释缺口清单清的是注释位置（列表首列「位置（说明点）」、筛选项「全部位置」、计数行「位置 N」同一批条目）。</summary>
    public const string SlotNoun = "注释位置";

    /// <summary>
    /// 生成文档核对的条目（列表首列「位置」、计数行「位置 N」、筛到零行那句话同一批条目）。
    /// </summary>
    public const string PropagationNoun = "核对条目";

    /// <summary>筛到零行时提示语指名的下一步；说这句话，筛选器就得真的按得动（第四十二轮）。</summary>
    public const string FilteredEmptyPointer = "换一个筛选再试。";

    /// <summary>
    /// 带关键词框的视图（检查结果、差异确认）筛到零行时的下一步：这一屏有两个真实控件能变出条目，
    /// 只指筛选器等于把关键词框那一半藏起来——用户刚在框里敲了字，最可能挡住行的正是它。
    /// </summary>
    public const string FilteredEmptySearchPointer = "换一个关键词或筛选再试。";

    /// <summary>
    /// 只有关键词框、没有筛选下拉的清单（同步预览的接口勾选页）筛到零行时的下一步：
    /// 这一屏能变出条目的控件只有那个框，指名「筛选」等于指向界面上不存在的控件
    /// （第四十六轮「文字不指向界面上不存在的按钮」同一条纪律）。
    /// </summary>
    public const string FilteredEmptyKeywordPointer = "换一个关键词再试。";

    /// <summary>
    /// 关键词框能不能用：整份数据不可得时停用，作业运行期间同样停用。
    /// 这一条与「筛到零行时关键词框留着」不矛盾，两者说的是两种空：筛到零行是**有数据、只是当前关键词不命中**，
    /// 下一步就在那个框上（§7「禁用不得移除下一步」）；整份数据不可得是**没有可筛的东西**，
    /// 下一步在提示出口那句话里（读取失败/尚未检查/没有条目各有自己的出路），框里敲字筛不到任何东西——
    /// 留着可用就是让一个按不动的控件看起来按得动（§7「禁用 = 控件禁用 + 一句话说明缺什么」）。
    /// 五张带筛选下拉的清单自第五十九轮起就是这条口径（整份数据不可得时下拉与框一起停），
    /// 四张只有关键词框的清单（同步预览的接口勾选页、接口追踪明细、补充说明、生成文档核对）在第七十八轮并入同一册：
    /// 此前只有同步预览那一页停用，另三处留着可用，同一个状态在四张清单上有两种样子。
    /// 停用的框也不接 Ctrl+F（见 <c>Ui.TryFocusFilter</c>）：焦点交不过去，吞掉按键等于把「什么都没发生」说成做了什么。
    /// </summary>
    public static bool KeywordEnabled(bool hasData, bool busy = false) => hasData && !busy;

    /// <summary>
    /// 这一屏的空属于哪一种：整份数据就没有条目，还是有条目、只是当前筛选一条都不命中。
    /// 两者的下一步不同——前者不在筛选上（各视图另有说明原因与出路的提示，筛选器也随之停用），
    /// 后者正是换一个筛选，而且聚合计数仍然可信，不能因为这一屏空了就把它抹掉：
    /// 逐条清队列的人要看到「待审阅 0」「待处理 0」才算清完，与两页「筛选出 0 个任务（共 42 个）」同一口径。
    /// </summary>
    public static bool IsFilteredEmpty(int totalEntries, int visibleEntries) => totalEntries > 0 && visibleEntries == 0;

    /// <summary>
    /// 筛到零行时给的一句话：说清这一屏为什么是空的，并指名下一步。
    /// <paramref name="hasKeyword"/> 为真表示这一屏的关键词框里留着字——此时有两个真实控件能变出条目，
    /// 只指筛选器等于把关键词框那一半藏起来（用户刚在框里敲了字，最可能挡住行的正是它）；
    /// 框里没字时下一步只有筛选器，指名它才算说实（文字不指向界面上按不动的控件）。
    /// </summary>
    public static string FilteredEmptyHint(string noun, bool hasKeyword = false) =>
        $"这个筛选下没有{noun}。" + (hasKeyword ? FilteredEmptySearchPointer : FilteredEmptyPointer);

    /// <summary>
    /// 只有关键词框的清单筛到零行时给的一句话：与 <see cref="FilteredEmptyHint"/> 同一句式，
    /// 但下一步只指名那个框——这一屏没有筛选下拉，说「换一个筛选」就是指向按不动的控件。
    /// </summary>
    public static string FilteredEmptyKeywordHint(string noun) =>
        $"这个关键词下没有{noun}。" + FilteredEmptyKeywordPointer;

    /// <summary>关键词去首尾空白；null 与全空白都视为「没有筛选」。</summary>
    public static string Normalize(string? query) => query?.Trim() ?? "";

    /// <summary>筛选框是否有内容（决定空态文字与状态行的计数口径）。</summary>
    public static bool IsActive(string? query) => Normalize(query).Length > 0;

    /// <summary>
    /// 一行是否命中关键词：词间「且」、字段间「或」，按当前区域忽略大小写；
    /// 空关键词命中全部行；null 字段按空处理（不抛异常、不误命中）。
    /// </summary>
    public static bool Matches(string? query, params string?[] fields) => MatchesAll(Terms(query), fields);

    /// <summary>
    /// 按关键词筛选行：没有关键词时返回数据源顺序的副本（与 <see cref="ListSort.Order"/> 同一纪律，
    /// 页面拿到的列表与数据源自身列表互不影响）；有关键词时保持数据源相对顺序，交给排序口径再排。
    /// </summary>
    public static List<T> Apply<T>(IEnumerable<T> rows, string? query, Func<T, string?[]> fields)
    {
        var terms = Terms(query);
        return terms.Length == 0 ? rows.ToList() : rows.Where(row => MatchesAll(terms, fields(row))).ToList();
    }

    private static string[] Terms(string? query) =>
        Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static bool MatchesAll(string[] terms, string?[] fields)
    {
        foreach (var term in terms)
        {
            var hit = false;
            foreach (var field in fields)
            {
                if (field is null || !field.Contains(term, StringComparison.CurrentCultureIgnoreCase)) continue;
                hit = true;
                break;
            }
            if (!hit) return false;
        }
        return true;
    }
}
