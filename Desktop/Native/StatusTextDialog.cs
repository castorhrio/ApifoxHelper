using System.Drawing;
using System.Windows.Forms;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 完整错误信息（只读）：状态行放不下的那句失败原因，全文在这里读、可以选中复制。
/// <para>
/// 数据来自状态行当前文字（<see cref="StatusLineHost"/> 传进来），本对话框不读磁盘、不重试、
/// 不写任何东西——「再读一次」仍由原处那颗「重试」负责（说明行里指名它，不在这里放第二颗）。
/// 文字取 <see cref="StatusLineText"/> 一份，本对话框不就地拼写。
/// </para>
/// <para>
/// 形状与其余只读视图同一份（DESIGN.md §6）：标题 Section 档、说明行 Caption 档、
/// 发丝边只读框填满剩余空间、一颗「关闭」ghost 按钮，Enter 与 Esc 都是关闭。
/// 用可滚动的只读框而不是自动折行的标签：错误原因里带文件全路径与英文句子，
/// 折行会把路径断在任意位置，而用户要抄的往往正是那段路径。
/// </para>
/// <para>
/// <b>两条交出文字的路（第一百九十七轮）</b>：框本身可框选（只挑其中一段），旁边一颗「复制全文」
/// 一次点击交出整段（口径在 <see cref="StatusLineText.CopyFullTextButton"/>，结果标签取
/// <see cref="NoticeCopy"/> 那一份）。此前只有框选那条路，说明行也只说「可以选中复制」——
/// 用户读完还得自己找怎么复制。
/// </para>
/// </summary>
internal sealed class StatusTextDialog : DpiAwareForm
{
    private readonly TextBox _text;

    public StatusTextDialog(string message)
    {
        Text = StatusLineText.FullTextHeading;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        DpiScale.Apply(this, new Size(720, 420));
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        var head = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = UiTokens.Panel,
            Padding = new Padding(UiTokens.Lg, UiTokens.Lg, UiTokens.Lg, UiTokens.Sm),
            Margin = new Padding(0),
        };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var title = Ui.Section(StatusLineText.FullTextHeading);
        title.Margin = new Padding(0, 0, 0, UiTokens.Xs);
        head.Controls.Add(title);
        head.Controls.Add(Ui.Caption(StatusLineText.FullTextNote));

        _text = Ui.Preview(multiline: true, mono: false, wordWrap: true, scrollBars: ScrollBars.Vertical);
        _text.Text = message;
        _text.SelectionStart = 0;
        _text.SelectionLength = 0;

        var boxHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTokens.Panel,
            Padding = new Padding(UiTokens.Lg, 0, UiTokens.Lg, UiTokens.Sm),
        };
        boxHost.Controls.Add(Ui.Hairline(_text));

        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;
        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = UiTokens.Panel,
            Padding = new Padding(0, UiTokens.Sm, UiTokens.Lg, UiTokens.Md),
        };
        // RightToLeft 流向：先加的在最右（Windows 惯例：确认类按钮在右）。
        buttonRow.Controls.Add(closeButton);
        // 复制全文（第一百九十七轮）：框已经能选中复制，但「整段交给剪贴板」要三步（点进框 → Ctrl+A → Ctrl+C），
        // 而错误原因正是用户要整段贴进工单的东西。结果标签与提示对话框同一份（NoticeCopy），不另写一套；
        // 三个标签同字数（4/4/4），复制成功/失败时旁边那颗「关闭」不挪位。
        var copyButton = Ui.Ghost(StatusLineText.CopyFullTextButton);
        copyButton.Click += (_, _) =>
            copyButton.Text = Ui.CopyToClipboard(_text.Text) ? NoticeCopy.CopiedLabel : NoticeCopy.FailedLabel;
        buttonRow.Controls.Add(copyButton);

        // 停靠按集合倒序生效：按钮行最先停靠（最底部），只读框填满剩余空间，标题行最上。
        Controls.Add(boxHost);
        Controls.Add(buttonRow);
        Controls.Add(head);
    }

    /// <summary>框里的全文（冒烟核对「点开看到的就是状态行那句话」用）。</summary>
    internal string Message => _text.Text;
}
