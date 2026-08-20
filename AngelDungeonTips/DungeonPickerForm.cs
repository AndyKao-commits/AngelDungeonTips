namespace AngelDungeonTips;

public sealed class DungeonPickerForm : Form
{
    private readonly ListBox list;
    public DungeonGuide? Selected { get; private set; }

    public DungeonPickerForm(IEnumerable<DungeonGuide> dungeons, string? preferId)
    {
        Text = "選擇副本";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 420);
        Font = new Font("Microsoft JhengHei UI", 10f);

        list = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            Font = new Font("Microsoft JhengHei UI", 11f)
        };
        foreach (var d in dungeons.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            list.Items.Add(d);
        if (list.Items.Count > 0)
        {
            int sel = 0;
            for (int i = 0; i < list.Items.Count; i++)
            {
                if (list.Items[i] is DungeonGuide g && g.Id == preferId)
                {
                    sel = i;
                    break;
                }
            }
            list.SelectedIndex = sel;
        }
        list.DoubleClick += (_, _) => Accept();

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48 };
        var btnOk = new Button
        {
            Text = "開啟",
            Size = new Size(100, 32),
            Location = new Point(140, 8),
            BackColor = Color.FromArgb(46, 125, 230),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnOk.FlatAppearance.BorderSize = 0;
        btnOk.Click += (_, _) => Accept();
        var btnCancel = new Button
        {
            Text = "取消",
            Size = new Size(90, 32),
            Location = new Point(250, 8),
            DialogResult = DialogResult.Cancel
        };
        bottom.Controls.AddRange(new Control[] { btnOk, btnCancel });
        CancelButton = btnCancel;
        AcceptButton = btnOk;

        Controls.Add(list);
        Controls.Add(bottom);
    }

    private void Accept()
    {
        if (list.SelectedItem is not DungeonGuide d)
        {
            MessageBox.Show(this, "請先選一個副本。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Selected = d;
        DialogResult = DialogResult.OK;
        Close();
    }
}
