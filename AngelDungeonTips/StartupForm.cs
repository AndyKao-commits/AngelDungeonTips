namespace AngelDungeonTips;

/// <summary>
/// Pick game window, then exit with OK so Program can Application.Run the overlay.
/// </summary>
public sealed class StartupForm : Form
{
    private readonly ComboBox cmbWindows;
    private readonly AppSettings settings;

    public StartupForm()
    {
        settings = TipStore.LoadSettings();
        Text = "AngelDungeonTips — 啟動";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(500, 240);
        Font = new Font("Microsoft JhengHei UI", 9.5f);

        var title = new Label
        {
            Text = "副本提示外掛",
            Font = new Font("Microsoft JhengHei UI", 14f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 18)
        };
        var desc = new Label
        {
            Text = "選取遊戲後，提示預設「點穿」：看得見但不擋操作。\n需要按按鈕時再切成「可點選」。",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = new Point(20, 52)
        };

        var lbl = new Label { Text = "遊戲視窗", AutoSize = true, Location = new Point(20, 110) };
        cmbWindows = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(90, 106),
            Width = 300
        };
        var btnRefresh = new Button { Text = "重新整理", Location = new Point(400, 104), Size = new Size(80, 28) };
        btnRefresh.Click += (_, _) => Reload();

        var btnStart = new Button
        {
            Text = "綁定並啟動",
            Size = new Size(120, 34),
            Location = new Point(250, 170),
            BackColor = Color.FromArgb(46, 125, 230),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnStart.FlatAppearance.BorderSize = 0;
        btnStart.Click += (_, _) => StartBound();

        var btnSkip = new Button
        {
            Text = "不綁定，直接開",
            Size = new Size(130, 34),
            Location = new Point(100, 170),
            FlatStyle = FlatStyle.Flat
        };
        btnSkip.Click += (_, _) =>
        {
            settings.GameWindowTitle = null;
            settings.FollowGameWindow = false;
            TipStore.SaveSettings(settings);
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.AddRange(new Control[] { title, desc, lbl, cmbWindows, btnRefresh, btnStart, btnSkip });
        Reload();
    }

    private void Reload()
    {
        var wins = GameWindowHelper.ListWindows();
        cmbWindows.Items.Clear();
        foreach (var w in wins) cmbWindows.Items.Add(w);
        if (cmbWindows.Items.Count == 0)
        {
            cmbWindows.Items.Add("(找不到可視視窗，可先開遊戲再整理)");
            cmbWindows.SelectedIndex = 0;
            return;
        }

        int sel = 0;
        if (!string.IsNullOrEmpty(settings.GameWindowTitle))
        {
            for (int i = 0; i < wins.Count; i++)
            {
                if (wins[i].Title.Equals(settings.GameWindowTitle, StringComparison.OrdinalIgnoreCase))
                {
                    sel = i;
                    break;
                }
            }
        }
        cmbWindows.SelectedIndex = sel;
    }

    private void StartBound()
    {
        if (cmbWindows.SelectedItem is not GameWindowInfo g)
        {
            MessageBox.Show(this, "請先選遊戲視窗，或按「不綁定，直接開」。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        settings.GameWindowTitle = g.Title;
        settings.FollowGameWindow = true;
        TipStore.SaveSettings(settings);
        DialogResult = DialogResult.OK;
        Close();
    }
}
