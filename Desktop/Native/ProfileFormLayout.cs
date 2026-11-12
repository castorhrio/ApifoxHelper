namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 配置编辑对话框（新建 / 编辑）的表单口径：哪些字段是「创建所必需」、哪些是可选的、
/// 高级入口收在哪一行、空标签格怎么占位、未保存时「读取项目列表」怎么说话。
/// <para>
/// <b>为什么要有这个文件</b>（第一百四十八轮）：新建配置对话框在真机截图里是「名称、仓库根目录之后
/// 到 API 项目之间一大段空白，四个高级按钮彼此间距极大，右侧还出滚动条」——用户的原话是「很抽象、没法用」。
/// 根因不是审美：<c>AddRow(grid, "", …)</c> 在空标签格里放的是 <c>new Panel { Margin = 0 }</c>，
/// 而 <see cref="System.Windows.Forms.Panel"/> 的默认尺寸是 200×100——每一行「没有标签」的行
/// （两条说明、一条长说明、四个高级入口、一个导出入口）都被撑到 100px 高，七行就是 700px 的死空白；
/// 同时那条七十余字的长说明在 495px 宽的字段列里放不下，把网格撑宽、右侧出横向滚动条。
/// 这两件事都不是「调一下间距」能解决的，得先把「一行该多高、哪些行属于首用路径」写成可断言的形状。
/// </para>
/// <para>
/// <b>行高预算按「这一行用的哪一档字号」给</b>（第二百三十六轮，简报 P0-1；完整决定项见
/// <c>docs/DECISIONS/D2-行高预算按字号档给.md</c>）：第二百三十四轮逐行量到的两组超预算行里，甲组是
/// 「该放大的列没放大」（已于第二百三十五轮修掉），乙组是「预算只认识正文档一个字号」——分区标题用的是
/// Section 档，200% 档单行就 58px，加 4+4 留白 = 66px，而按正文档算的预算是 41 + 24 = 65px，差 1px。
/// <b>把正文档预算一提了之会放过甲组那一族真正的长行</b>：125% 档那四行折成两行是 60px，而分区档在
/// 125% 档的预算线第二百三十六轮量到的是 37 + 24 = 61px，一提档就一行都判不出——那两个数正是量出甲组根因的数。所以改的是
/// 「喂给算式的行高取哪一档」，算式本身仍只有 <see cref="MaxRowHeight"/> 一处；认档的口径在 <see cref="TierOf"/>。
/// </para>
/// <para>
/// 本文件只放判定与文案，不依赖 WinForms，以共享源码编入测试工程在 Linux 断言；
/// 真实行高与控件位置由冒烟器在 Windows/wine 下量（<c>RunProfileFormLayout</c>）。
/// </para>
/// </summary>
public static class ProfileFormLayout
{
    /// <summary>
    /// 空标签格的占位尺寸（0 = 不占位）。空标签格此前放的是默认尺寸的 <c>Panel</c>（200×100），
    /// 行高因此被撑到 100px；占位必须是零尺寸，行高才由字段列那一格决定。
    /// </summary>
    public const int SpacerSize = 0;

    /// <summary>单行字段行的行内上下留白上限（像素）：DESIGN.md §4 的 Xs + Sm + Md = 4 + 8 + 12。</summary>
    public const int RowPadding = 24;

    /// <summary>分区标题：创建所必需的三项。</summary>
    public const string CoreSectionTitle = "必填";

    /// <summary>分区标题：可选设置（保存后再补也不影响创建）。</summary>
    public const string OptionalSectionTitle = "可选设置（可保存后再补）";

    /// <summary>分区标题：高级设置（四个入口收在同一行，不占首用视线）。</summary>
    public const string AdvancedSectionTitle = "高级设置（可选）";

    /// <summary>
    /// 创建所必需的字段（顺序即界面顺序）：名称、仓库根目录、API 项目。
    /// 这三项填完就能保存——「读取项目列表需要先保存」因此不是死胡同，而是保存之后的事。
    /// </summary>
    public static readonly string[] CoreFields = ["名称", "仓库根目录", "API 项目"];

    /// <summary>可选字段（顺序即界面顺序）：保存不依赖它们，留空即取缺省。</summary>
    public static readonly string[] OptionalFields =
    [
        "允许回写范围",
        "版本备注",
        "Swagger 来源",
        "Swagger 位置",
        "Apifox 项目 ID",
        "分支",
    ];

    /// <summary>高级入口（同一行、同一容器）：模块与模板、序列化与分析、模型服务、导出配置。</summary>
    public static readonly string[] AdvancedEntries =
    [
        "模块与模板…",
        "序列化与分析…",
        "模型服务…",
        "导出配置（不含凭据）",
    ];

    /// <summary>未保存时「读取项目列表」旁边的说明：讲清下一步，而不是等用户点了才报错。</summary>
    public const string ProjectListHintUnsaved = "保存后可读取项目列表；也可以直接填写项目 ID。";

    /// <summary>已保存时「读取项目列表」旁边的说明。</summary>
    public const string ProjectListHintSaved = "按已保存的 CLI 设置读取项目列表。";

    /// <summary>
    /// 「读取项目列表」是否可用：列表按已保存配置的 CLI 设置读取（与冻结页面同一门控）。
    /// 未保存时按钮<b>停用</b>并给出 <see cref="ProjectListHintUnsaved"/>——此前按钮是启用的，
    /// 点下去只得到一句「请先保存配置」，用户得自己猜「那我现在能不能保存」。
    /// </summary>
    public static bool ProjectListEnabled(bool saved) => saved;

    /// <summary>「读取项目列表」旁边的说明（两态各一句，界面不就地拼字）。</summary>
    public static string ProjectListHint(bool saved) => saved ? ProjectListHintSaved : ProjectListHintUnsaved;

    /// <summary>
    /// 单行的行高上限：<b>这一行所属字号档</b>的行高 + <see cref="RowPadding"/>。超过它的行说明那一行里
    /// 混进了不该有的东西（默认尺寸的占位面板、多行说明、被拉高的容器），冒烟器据此判失败。
    /// 这是行高预算<b>唯一</b>的算式——取哪一档的行高由 <see cref="TierOf"/> 决定，这里不认档。
    /// </summary>
    public static int MaxRowHeight(int lineHeight) => lineHeight + RowPadding;

    /// <summary>
    /// 配置表单里一行可能落的字号档（<b>封闭</b>：只有这两档登记在册）。
    /// <para>
    /// 提示行的 Caption 档<b>故意不在这里</b>：那一条量出的是「句子本身长到折两行」，不是「字号高一档」
    /// （2 × 35 + 8 = 78px，两行的提示行在任何档位都进不了预算），放宽它等于把第二百三十五轮放大标签列
    /// 时扣走的那笔宽度（提示行可用宽 1086 → 966px）从读数里抹掉。取舍记在 D2 §4 第三条。
    /// </para>
    /// </summary>
    public enum RowFontTier
    {
        /// <summary>正文档：字段行、说明行、按钮行——表单里绝大多数行。</summary>
        Body,

        /// <summary>分区标题档：<c>Ui.Section</c> 出厂、跨两列的那一行。</summary>
        Section,
    }

    /// <summary>
    /// 量到的字号行高落在哪一档：与登记档的行高<b>相等</b>才算那一档，其余一律算正文档（更严的一档）。
    /// <para>
    /// <b>为什么是「相等」而不是「不小于」</b>：按「不小于分区档就给分区预算」写，任何人给某一格
    /// 换一个更大的字，就顺手把自己那一行的预算放宽一档——预算线就成了行高的函数，永远追得上，
    /// 这一族判据当场失效（第一百四十八轮立它就是为了拦住「行被撑高」）。认不出来的字号
    /// 按正文档算，多出的一档会红在报告里，逼人来 D2 这里登记，而不是让它自己过去。
    /// </para>
    /// <para>
    /// <c>sectionLineHeight &gt; bodyLineHeight</c> 那一半判的是「分区档真的比正文档高一档」：
    /// 两个字号被改成同一个数时，这一档就不再提供放宽，全部退回正文档——放宽必须是一次显式登记，
    /// 不能是两档行高碰巧相等的后果。
    /// </para>
    /// </summary>
    public static RowFontTier TierOf(int lineHeight, int bodyLineHeight, int sectionLineHeight) =>
        lineHeight == sectionLineHeight && sectionLineHeight > bodyLineHeight ? RowFontTier.Section : RowFontTier.Body;

    /// <summary>
    /// 这一行的行高预算线：先按 <see cref="TierOf"/> 认档，再走 <see cref="MaxRowHeight"/> 那一处算式。
    /// 调用方（冒烟器逐行量字段网格）给的是<b>当前缩放档上</b>的两档字体行高与这一行自己的字号行高。
    /// </summary>
    public static int MaxRowHeightFor(int lineHeight, int bodyLineHeight, int sectionLineHeight) =>
        MaxRowHeight(TierOf(lineHeight, bodyLineHeight, sectionLineHeight) == RowFontTier.Section ? sectionLineHeight : bodyLineHeight);

    /// <summary>
    /// 高级入口那一行里相邻两个按钮的最大水平间距（像素）：DESIGN.md §4 的 Sm。
    /// 四个入口此前各占一整行、彼此隔着 100px 的空白，读起来像四颗孤立的按钮。
    /// </summary>
    public const int AdvancedEntryGap = 8;
}
