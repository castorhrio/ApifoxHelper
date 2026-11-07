using System.Drawing;
using System.Windows.Forms;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 控件形态的统一出口（docs/DESIGN.md §6）：主按钮/ghost/危险、标签档位、发丝边包裹。
/// 界面代码从这里取控件形态，不逐处设置颜色与边框。
/// </summary>
internal static class Ui
{
    /// <summary>实心主按钮：Accent 填充 + 白字，hover 只改底色；每屏至多一个。</summary>
    public static Button Primary(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = UiTokens.Accent,
            ForeColor = Color.White,
            Padding = new Padding(UiTokens.Md, UiTokens.Xs, UiTokens.Md, UiTokens.Xs),
            Margin = new Padding(0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = UiTokens.AccentHover;
        button.FlatAppearance.MouseDownBackColor = UiTokens.AccentHover;
        return button;
    }

    /// <summary>Ghost 按钮：面板底 + 1px 发丝边，hover 只改底色。</summary>
    public static Button Ghost(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = UiTokens.Panel,
            ForeColor = UiTokens.Text,
            Padding = new Padding(UiTokens.Md, UiTokens.Xs, UiTokens.Md, UiTokens.Xs),
            Margin = new Padding(0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = UiTokens.Line;
        button.FlatAppearance.MouseOverBackColor = UiTokens.Bg;
        button.FlatAppearance.MouseDownBackColor = UiTokens.Bg;
        return button;
    }

    /// <summary>停止/危险按钮：ghost 形态 + 危险字色（位置尺寸不变，防按钮跳位）。</summary>
    public static Button DangerGhost(string text)
    {
        var button = Ghost(text);
        button.ForeColor = UiTokens.BadText;
        return button;
    }

    public static Label Caption(string? text = null, bool muted = true) => Label(UiTokens.Caption.Value, muted ? UiTokens.Muted : UiTokens.Text, text);

    public static Label Body(string? text = null, bool muted = false) => Label(UiTokens.Body.Value, muted ? UiTokens.Muted : UiTokens.Text, text);

    public static Label Section(string? text = null) => Label(UiTokens.Section.Value, UiTokens.Text, text);

    public static Label Mono(string? text = null) => Label(UiTokens.Mono.Value, UiTokens.Text, text);

    public static Label StatusText(string? text = null) => Label(UiTokens.Body.Value, UiTokens.Text, text);

    /// <summary>错误/提示行：固定占位（文案变化不推动布局），错误用文字色不铺底。</summary>
    public static Label StatusLine() => new()
    {
        AutoSize = false,
        Height = UiTokens.Body.Value.Height + UiTokens.Xs * 2,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = UiTokens.BadText,
        Font = UiTokens.Body.Value,
        Text = "",
    };

    private static Label Label(Font font, Color color, string? text) => new()
    {
        Text = text ?? "",
        AutoSize = true,
        Font = font,
        ForeColor = color,
        Margin = new Padding(0),
    };

    /// <summary>用 1px 发丝边（Line 色）包裹无边框内容控件。</summary>
    public static Panel Hairline(ListView inner)
    {
        inner.Dock = DockStyle.Fill;
        inner.BorderStyle = BorderStyle.None;
        return HairlineCore(inner);
    }

    /// <summary>同上，用于只读文本框（去系统边框，包发丝边）。</summary>
    public static Panel Hairline(TextBox inner)
    {
        inner.Dock = DockStyle.Fill;
        inner.BorderStyle = BorderStyle.None;
        return HairlineCore(inner);
    }

    private static Panel HairlineCore(Control inner)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTokens.Line,
            Padding = new Padding(1),
        };
        panel.Controls.Add(inner);
        return panel;
    }
}
