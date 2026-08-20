namespace AngelDungeonTips;

/// <summary>
/// Settings with live preview on the tip overlay.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly MainOverlayForm overlay;
    private readonly AppSettings original;
    private readonly NumericUpDown numW;
    private readonly NumericUpDown numH;
    private readonly TrackBar trkOpacity;
    private readonly Label lblOpacity;
    private readonly TrackBar trkTextOpacity;
    private readonly Label lblTextOpacity;
    private readonly ComboBox cmbWindow;
    private readonly CheckBox chkFollow;
    private readonly CheckBox chkClickThrough;
    private bool applying;

    public AppSettings Result { get; private set; }

    public SettingsForm(AppSettings current, MainOverlayForm overlay)
    {
        this.overlay = overlay;
        original = Clone(current);
        Result = Clone(current);

        Text = "提示視窗設定（即時預覽）";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(420, 360);
        Font = new Font("Microsoft JhengHei UI", 9.5f);
        Owner = overlay;

        var hintTop = new Label
        {
            Text = "調整時會直接套在提示窗上，方便對照遊戲畫面。",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = new Point(16, 12)
        };

        var lblSize = new Label { Text = "視窗大小", AutoSize = true, Location = new Point(16, 44) };
        numW = new NumericUpDown
        {
            Minimum = 260, Maximum = 1200,
            Value = Math.Clamp(current.Width, 260, 1200),
            Location = new Point(100, 40), Width = 80
        };
        var lblX = new Label { Text = "×", AutoSize = true, Location = new Point(188, 44) };
        numH = new NumericUpDown
        {
            Minimum = 140, Maximum = 900,
            Value = Math.Clamp(current.Height, 140, 900),
            Location = new Point(208, 40), Width = 80
        };

        var lblOp = new Label { Text = "背景透明度", AutoSize = true, Location = new Point(16, 84) };
        trkOpacity = new TrackBar
        {
            Minimum = 0, Maximum = 100,
            Value = Math.Clamp(current.OpacityPercent, 0, 100),
            TickFrequency = 10,
            Location = new Point(110, 74), Width = 200
        };
        lblOpacity = new Label
        {
            AutoSize = true,
            Location = new Point(320, 84),
            Text = trkOpacity.Value + "%"
        };
        var lblOpHint = new Label
        {
            Text = "只調底板。拉到 0％ ＝底板全透，文字仍可清晰",
            AutoSize = true,
            ForeColor = Color.Gray,
            Location = new Point(110, 108)
        };

        var lblText = new Label { Text = "文字濃度", AutoSize = true, Location = new Point(16, 136) };
        trkTextOpacity = new TrackBar
        {
            Minimum = 0, Maximum = 100,
            Value = Math.Clamp(current.TextOpacityPercent, 0, 100),
            TickFrequency = 10,
            Location = new Point(110, 126), Width = 200
        };
        lblTextOpacity = new Label
        {
            AutoSize = true,
            Location = new Point(320, 136),
            Text = trkTextOpacity.Value + "%"
        };
        var lblTextHint = new Label
        {
            Text = "只調文字。與背景互不影響",
            AutoSize = true,
            ForeColor = Color.Gray,
            Location = new Point(110, 160)
        };

        var lblGame = new Label { Text = "綁定遊戲", AutoSize = true, Location = new Point(16, 192) };
        cmbWindow = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(110, 188), Width = 220
        };
        var btnRefresh = new Button { Text = "重整", Location = new Point(338, 186), Size = new Size(56, 26) };
        btnRefresh.Click += (_, _) => ReloadWindows(current.GameWindowTitle);

        chkFollow = new CheckBox
        {
            Text = "跟隨遊戲視窗移動",
            AutoSize = true,
            Checked = current.FollowGameWindow,
            Location = new Point(110, 224)
        };

        chkClickThrough = new CheckBox
        {
            Text = "滑鼠點穿（遊玩時建議開）",
            AutoSize = true,
            Checked = current.ClickThrough,
            Location = new Point(110, 250)
        };

        var hint = new Label
        {
            Text = "標題列有 ◀ ▶ 翻頁；點穿時也可用鍵盤 ← →。",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = new Point(16, 280)
        };

        var btnOk = new Button
        {
            Text = "確定",
            DialogResult = DialogResult.OK,
            Location = new Point(210, 312),
            Size = new Size(90, 32)
        };
        var btnCancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(310, 312),
            Size = new Size(90, 32)
        };
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.AddRange(new Control[]
        {
            hintTop, lblSize, numW, lblX, numH,
            lblOp, trkOpacity, lblOpacity, lblOpHint,
            lblText, trkTextOpacity, lblTextOpacity, lblTextHint,
            lblGame, cmbWindow, btnRefresh, chkFollow, chkClickThrough,
            hint, btnOk, btnCancel
        });

        numW.ValueChanged += (_, _) => LivePreview();
        numH.ValueChanged += (_, _) => LivePreview();
        trkOpacity.ValueChanged += (_, _) =>
        {
            lblOpacity.Text = trkOpacity.Value + "%";
            LivePreview();
        };
        trkTextOpacity.ValueChanged += (_, _) =>
        {
            lblTextOpacity.Text = trkTextOpacity.Value + "%";
            LivePreview();
        };

        ReloadWindows(current.GameWindowTitle);
        PlaceBesideOverlay();
    }

    private void PlaceBesideOverlay()
    {
        try
        {
            int x = overlay.Right + 8;
            int y = overlay.Top;
            var wa = Screen.FromControl(overlay).WorkingArea;
            if (x + Width > wa.Right) x = Math.Max(wa.Left, overlay.Left - Width - 8);
            if (y + Height > wa.Bottom) y = Math.Max(wa.Top, wa.Bottom - Height);
            Location = new Point(x, y);
        }
        catch
        {
            StartPosition = FormStartPosition.CenterScreen;
        }
    }

    private void LivePreview()
    {
        if (applying) return;
        overlay.ApplyLivePreview(
            (int)numW.Value,
            (int)numH.Value,
            trkOpacity.Value,
            trkTextOpacity.Value);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            Result.Width = (int)numW.Value;
            Result.Height = (int)numH.Value;
            Result.OpacityPercent = trkOpacity.Value;
            Result.TextOpacityPercent = trkTextOpacity.Value;
            Result.FollowGameWindow = chkFollow.Checked;
            Result.ClickThrough = chkClickThrough.Checked;
            Result.GameWindowTitle = cmbWindow.SelectedItem is GameWindowInfo g ? g.Title : null;
            if (Result.GameWindowTitle == null)
                Result.FollowGameWindow = false;
            Result.LastDungeonId = original.LastDungeonId;
            Result.PosX = overlay.Left;
            Result.PosY = overlay.Top;
            Result.RelX = original.RelX;
            Result.RelY = original.RelY;
        }
        else
        {
            overlay.ApplyLivePreview(
                original.Width, original.Height,
                original.OpacityPercent, original.TextOpacityPercent);
            if (original.HasSavedPosition)
                overlay.Location = new Point(original.PosX, original.PosY);
        }
        base.OnFormClosing(e);
    }

    private void ReloadWindows(string? preferTitle)
    {
        applying = true;
        var wins = GameWindowHelper.ListWindows();
        cmbWindow.Items.Clear();
        cmbWindow.Items.Add("(不綁定遊戲視窗)");
        int sel = 0;
        for (int i = 0; i < wins.Count; i++)
        {
            cmbWindow.Items.Add(wins[i]);
            if (!string.IsNullOrEmpty(preferTitle) &&
                wins[i].Title.Equals(preferTitle, StringComparison.OrdinalIgnoreCase))
                sel = i + 1;
        }
        cmbWindow.SelectedIndex = Math.Min(sel, cmbWindow.Items.Count - 1);
        applying = false;
    }

    private static AppSettings Clone(AppSettings s) => new()
    {
        Width = s.Width,
        Height = s.Height,
        OpacityPercent = s.OpacityPercent,
        TextOpacityPercent = s.TextOpacityPercent,
        LastDungeonId = s.LastDungeonId,
        GameWindowTitle = s.GameWindowTitle,
        PosX = s.PosX,
        PosY = s.PosY,
        RelX = s.RelX,
        RelY = s.RelY,
        FollowGameWindow = s.FollowGameWindow,
        ClickThrough = s.ClickThrough
    };
}
