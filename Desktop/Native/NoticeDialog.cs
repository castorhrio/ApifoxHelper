using System.Drawing;
using System.Windows.Forms;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 提示对话框（DESIGN.md §6「对话框」）：说一件事、不需要用户做选择——标题（Section 档）、
/// 正文（Body 档）、可选的等宽行（路径）与说明行（Caption 档），一个「关闭」ghost 按钮，
/// Enter 与 Esc 都是关闭。与其余信息视图同一套令牌与形状（FixedDialog、面板底、Body 字体、
/// 长内容区经 <see cref="Ui.ScrollHost"/>），不再用系统 MessageBox 的默认观感。
/// 确认类动作（执行同步、覆盖式写入、退出）仍用原生确认对话框（§6 例外条款），不经过这里。
/// 文字取 <see cref="NoticeText"/> 一份，本对话框不就地拼写。
/// </summary>
internal sealed class NoticeDialog : Form
{
    public NoticeDialog(string heading, string body, string? mono = null, string? note = null)
    {
        Text = heading;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 260);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        // 正文按对话框宽度折行：MaximumSize 的宽度取可视宽度，高度交给 AutoSize（长内容由宿主滚动）。
        var wrapWidth = ClientSize.Width - 2 * UiTokens.Lg;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = UiTokens.Panel,
            Padding = new Padding(UiTokens.Lg),
            Margin = new Padding(0),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var title = Ui.Section(heading);
        title.Margin = new Padding(0, 0, 0, UiTokens.Md);
        grid.Controls.Add(title);

        var bodyLabel = Ui.Body(body);
        bodyLabel.MaximumSize = new Size(wrapWidth, 0);
        bodyLabel.Margin = new Padding(0, 0, 0, UiTokens.Md);
        grid.Controls.Add(bodyLabel);

        if (mono is not null)
        {
            var monoLabel = Ui.Mono(mono);
            monoLabel.MaximumSize = new Size(wrapWidth, 0);
            monoLabel.Margin = new Padding(0, 0, 0, UiTokens.Md);
            grid.Controls.Add(monoLabel);
        }

        if (note is not null)
        {
            var noteLabel = Ui.Caption(note);
            noteLabel.MaximumSize = new Size(wrapWidth, 0);
            grid.Controls.Add(noteLabel);
        }

        // 停靠方向由工厂一处决定（Dock = Fill），这里不再补设。
        var host = Ui.ScrollHost();
        host.Controls.Add(grid);

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
            Padding = new Padding(UiTokens.Lg, UiTokens.Sm, UiTokens.Lg, UiTokens.Md),
        };
        // RightToLeft 流向：先加的在最右（Windows 惯例：确认类按钮在右）。
        buttonRow.Controls.Add(closeButton);

        // 停靠按集合倒序生效：按钮行最先停靠（最底部），内容区填满剩余空间。
        Controls.Add(host);
        Controls.Add(buttonRow);
    }
}
