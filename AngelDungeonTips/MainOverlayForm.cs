using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AngelDungeonTips;

/// <summary>
/// Per-pixel layered overlay: background alpha and text alpha are independent.
/// Background at 0% is fully invisible; text can stay crisp at 100%.
/// </summary>
public sealed class MainOverlayForm : Form
{
    private enum HitId { None, Options, ClickMode, Prev, Next, SyncStatus, Drag, ResizeL, ResizeR, ResizeT, ResizeB, ResizeBR }

    private const int ChromeH = 40;
    private const int Edge = 8;

    private readonly AppSettings settings;
    private DungeonCatalog catalog;
    private DungeonGuide? current;
    private int stageIndex;
    private IntPtr gameHwnd = IntPtr.Zero;

    private bool dragging;
    private bool resizing;
    private HitId resizeKind = HitId.None;
    private Point dragMouseScreen;
    private Point dragFormLoc;
    private Rectangle resizeStartBounds;
    private bool suppressFollow;
    private bool keyLeftWas, keyRightWas, keyF8Was;
    private int scrollY;

    private Rectangle rcOptions, rcClickMode, rcPrev, rcNext, rcDrag, rcSyncStatus;
    private readonly ContextMenuStrip optionsMenu;
    private readonly System.Windows.Forms.Timer bindTimer = new() { Interval = 200 };

    private string syncStatusText = "";
    private bool syncCanRetry;
    private bool syncBusy;

    public MainOverlayForm()
    {
        settings = TipStore.LoadSettings();
        catalog = TipStore.LoadCatalog();

        Text = "AngelDungeonTips";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        // Must stay fully opaque to Windows; we paint our own per-pixel alpha.
        Opacity = 1;
        AllowTransparency = false;
        Width = Math.Clamp(settings.Width, 280, 1200);
        Height = Math.Clamp(settings.Height, 160, 900);
        MinimumSize = new Size(280, 160);
        MaximumSize = new Size(1200, 900);
        Font = new Font("Microsoft JhengHei UI", 9.5f);

        optionsMenu = new ContextMenuStrip();
        optionsMenu.Items.Add("選擇副本", null, (_, _) =>
        {
            SetClickThrough(false, save: false);
            OpenDungeonPicker();
        });
        optionsMenu.Items.Add("設定…", null, (_, _) =>
        {
            SetClickThrough(false, save: false);
            OpenSettings();
        });
        optionsMenu.Items.Add("重新讀取", null, (_, _) => _ = RunCatalogSyncAsync());
        optionsMenu.Items.Add(new ToolStripSeparator());
        optionsMenu.Items.Add("結束", null, (_, _) => Close());

        bindTimer.Tick += (_, _) =>
        {
            SyncBoundGame();
            PollGlobalHotkeys();
        };
        bindTimer.Start();

        HandleCreated += (_, _) =>
        {
            ApplyExStyles();
            RedrawLayered();
        };
        LocationChanged += (_, _) => { /* layered uses Left/Top on redraw */ };
        SizeChanged += (_, _) =>
        {
            PersistGeometry();
            if (IsHandleCreated) RedrawLayered();
        };
        Shown += (_, _) =>
        {
            ApplyExStyles();
            RedrawLayered();
            SyncBoundGame();
            _ = RunCatalogSyncAsync();
        };

        MouseDown += OnOverlayMouseDown;
        MouseMove += OnOverlayMouseMove;
        MouseUp += OnOverlayMouseUp;
        MouseWheel += OnOverlayMouseWheel;

        ResolveGameWindow();
        if (!string.IsNullOrEmpty(settings.LastDungeonId))
        {
            var found = catalog.Dungeons.FirstOrDefault(d => d.Id == settings.LastDungeonId);
            if (found != null)
                OpenDungeon(found, 0);
        }
        PositionInitial();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            // Called during base Form ctor before our fields are assigned — never touch settings blindly.
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_NOACTIVATE;
            if (settings?.ClickThrough != false)
                cp.ExStyle |= WS_EX_TRANSPARENT;
            return cp;
        }
    }

    public void ApplyLivePreview(int width, int height, int backgroundOpacityPercent, int textOpacityPercent)
    {
        settings.Width = width;
        settings.Height = height;
        settings.OpacityPercent = Math.Clamp(backgroundOpacityPercent, 0, 100);
        settings.TextOpacityPercent = Math.Clamp(textOpacityPercent, 0, 100);
        Size = new Size(
            Math.Clamp(width, MinimumSize.Width, MaximumSize.Width),
            Math.Clamp(height, MinimumSize.Height, MaximumSize.Height));
        if (IsHandleCreated) RedrawLayered();
    }

    private void SetClickThrough(bool enabled, bool save)
    {
        settings.ClickThrough = enabled;
        ApplyExStyles();
        if (save) TipStore.SaveSettings(settings);
        RedrawLayered();
        SyncBoundGame();
    }

    private void ApplyExStyles()
    {
        if (!IsHandleCreated) return;
        int ex = GetWindowLong(Handle, GWL_EXSTYLE);
        ex |= WS_EX_LAYERED | WS_EX_NOACTIVATE;
        if (settings.ClickThrough)
            ex |= WS_EX_TRANSPARENT;
        else
            ex &= ~WS_EX_TRANSPARENT;
        SetWindowLong(Handle, GWL_EXSTYLE, ex);
    }

    #region Layered paint

    private void RedrawLayered()
    {
        if (!IsHandleCreated || Width < 10 || Height < 10) return;

        int w = Width;
        int h = Height;
        int bgA = (int)(255 * (Math.Clamp(settings.OpacityPercent, 0, 100) / 100.0));
        int textA = (int)(255 * (Math.Clamp(settings.TextOpacityPercent, 0, 100) / 100.0));

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.CompositingMode = CompositingMode.SourceOver;

            // Background panels (independent alpha)
            if (bgA > 0)
            {
                using var chromeBrush = new SolidBrush(Color.FromArgb(bgA, 40, 46, 56));
                using var bodyBrush = new SolidBrush(Color.FromArgb(bgA, 28, 32, 40));
                using var borderPen = new Pen(Color.FromArgb(Math.Min(255, bgA + 40), 160, 180, 200), 1.5f);
                g.FillRectangle(chromeBrush, 0, 0, w, ChromeH);
                g.FillRectangle(bodyBrush, 0, ChromeH, w, h - ChromeH);
                g.DrawRectangle(borderPen, 0.5f, 0.5f, w - 1.5f, h - 1.5f);
            }

            // Hit layout
            rcOptions = new Rectangle(8, 7, 48, 26);
            rcClickMode = new Rectangle(60, 7, 72, 26);
            rcPrev = new Rectangle(w - 118, 7, 34, 26);
            rcNext = new Rectangle(w - 42, 7, 34, 26);
            rcDrag = new Rectangle(140, 0, Math.Max(40, w - 270), ChromeH);

            // Button fills use background alpha; labels use text alpha
            DrawChromeButton(g, rcOptions, "選項", bgA, textA);
            DrawChromeButton(g, rcClickMode, settings.ClickThrough ? "點穿中" : "可點選", bgA, textA,
                settings.ClickThrough ? Color.FromArgb(40, 140, 90) : Color.FromArgb(180, 100, 40));
            DrawChromeButton(g, rcPrev, "◀", bgA, textA, Color.FromArgb(46, 125, 230));
            DrawChromeButton(g, rcNext, "▶", bgA, textA, Color.FromArgb(46, 125, 230));

            string page = current == null || current.Stages.Count == 0
                ? ""
                : $"{stageIndex + 1}/{current.Stages.Count}";
            string title = current?.Name ?? "AngelDungeonTips";
            using var titleFont = new Font("Microsoft JhengHei UI", 10f, FontStyle.Bold);
            using var pageFont = new Font("Microsoft JhengHei UI", 9.5f, FontStyle.Bold);
            DrawText(g, title, titleFont, Color.FromArgb(textA, 245, 245, 245),
                new RectangleF(140, 10, Math.Max(40, rcPrev.Left - 148), 22));
            DrawText(g, page, pageFont, Color.FromArgb(textA, 245, 245, 245),
                new RectangleF(rcPrev.Right + 2, 10, 40, 22), StringAlignment.Center);

            // Body
            var content = GetStageContent();
            using var stageTitleFont = new Font("Microsoft JhengHei UI", 11f, FontStyle.Bold);
            using var bodyFont = new Font("Microsoft JhengHei UI", 10.5f);
            float x = 14;
            float y = ChromeH + 12 - scrollY;
            float wrap = w - 28;

            var titleSize = g.MeasureString(content.title, stageTitleFont, (int)wrap);
            DrawOutlinedText(g, content.title, stageTitleFont,
                Color.FromArgb(textA, 255, 210, 120), textA,
                new RectangleF(x, y, wrap, titleSize.Height + 2));
            y += titleSize.Height + 10;

            var bodySize = g.MeasureString(content.body, bodyFont, (int)wrap);
            DrawOutlinedText(g, content.body, bodyFont,
                Color.FromArgb(textA, 250, 250, 250), textA,
                new RectangleF(x, y, wrap, bodySize.Height + 4));
            y += bodySize.Height + 8;

            // Sync status (user-facing only: 更新成功 / 抓取失敗（重新讀取）)
            rcSyncStatus = Rectangle.Empty;
            if (!string.IsNullOrEmpty(syncStatusText))
            {
                using var statusFont = new Font("Microsoft JhengHei UI", 8.5f);
                var sz = g.MeasureString(syncStatusText, statusFont);
                int sw = Math.Min(w - 20, (int)Math.Ceiling(sz.Width) + 8);
                int sh = Math.Max(18, (int)Math.Ceiling(sz.Height) + 2);
                rcSyncStatus = new Rectangle(10, h - sh - 8, sw, sh);
                var statusColor = syncCanRetry
                    ? Color.FromArgb(textA, 255, 170, 120)
                    : Color.FromArgb(textA, 140, 220, 160);
                DrawText(g, syncStatusText, statusFont, statusColor, rcSyncStatus,
                    StringAlignment.Near, StringAlignment.Center);
            }

            // Corner resize grip (visual only when interactive)
            if (!settings.ClickThrough && bgA > 10)
            {
                using var grip = new SolidBrush(Color.FromArgb(Math.Min(200, bgA + 60), 200, 200, 200));
                g.FillRectangle(grip, w - 12, h - 12, 10, 10);
            }
        }

        PushLayeredBitmap(bmp);
    }

    private (string title, string body) GetStageContent()
    {
        if (current == null || current.Stages.Count == 0)
            return ("尚未選擇副本", "點「選項 → 選擇副本」載入攻略。\n標題列 ◀ ▶ 翻頁｜F8 切換點穿｜←→ 翻頁");
        var s = current.Stages[Math.Clamp(stageIndex, 0, current.Stages.Count - 1)];
        return (s.Title, s.Body.Replace("\n", Environment.NewLine));
    }

    private static void DrawChromeButton(Graphics g, Rectangle rc, string text, int bgA, int textA, Color? fill = null)
    {
        var baseFill = fill ?? Color.FromArgb(70, 78, 92);
        int a = Math.Max(bgA, textA > 0 ? Math.Min(220, Math.Max(bgA, 160)) : bgA);
        // Keep buttons faintly visible even if background is 0, so user can find them in interactive mode
        if (bgA == 0 && textA > 0)
            a = 40;
        using var brush = new SolidBrush(Color.FromArgb(a, baseFill.R, baseFill.G, baseFill.B));
        using var path = RoundRect(rc, 4);
        g.FillPath(brush, path);
        using var font = new Font("Microsoft JhengHei UI", 9f, FontStyle.Bold);
        DrawText(g, text, font, Color.FromArgb(Math.Max(textA, bgA == 0 ? textA : Math.Min(255, textA)), 255, 255, 255),
            rc, StringAlignment.Center, StringAlignment.Center);
    }

    private static GraphicsPath RoundRect(Rectangle rc, int r)
    {
        var p = new GraphicsPath();
        int d = r * 2;
        p.AddArc(rc.X, rc.Y, d, d, 180, 90);
        p.AddArc(rc.Right - d, rc.Y, d, d, 270, 90);
        p.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0, 90);
        p.AddArc(rc.X, rc.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static void DrawText(Graphics g, string text, Font font, Color color, RectangleF layout,
        StringAlignment align = StringAlignment.Near, StringAlignment line = StringAlignment.Near)
    {
        if (string.IsNullOrEmpty(text) || color.A == 0) return;
        using var brush = new SolidBrush(color);
        using var sf = new StringFormat { Alignment = align, LineAlignment = line, Trimming = StringTrimming.EllipsisCharacter };
        g.DrawString(text, font, brush, layout, sf);
    }

    private static void DrawOutlinedText(Graphics g, string text, Font font, Color fill, int textA, RectangleF layout)
    {
        if (string.IsNullOrEmpty(text) || textA <= 0) return;
        using var path = new GraphicsPath();
        float em = font.SizeInPoints * g.DpiY / 72f;
        using var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };
        path.AddString(text, font.FontFamily, (int)font.Style, em, layout, sf);
        // Outline uses same alpha as text so lowering text concentration fades outline too
        using (var pen = new Pen(Color.FromArgb(textA, 0, 0, 0), 3.2f) { LineJoin = LineJoin.Round })
            g.DrawPath(pen, path);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
    }

    private void PushLayeredBitmap(Bitmap bmp)
    {
        PremultiplyAlpha(bmp);
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr oldBmp = SelectObject(memDc, hBmp);
        try
        {
            var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
            var pointSource = new POINT { x = 0, y = 0 };
            var topPos = new POINT { x = Left, y = Top };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255, // whole-window multiplier stays 255; per-pixel alpha only
                AlphaFormat = AC_SRC_ALPHA
            };
            UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size, memDc, ref pointSource, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            SelectObject(memDc, oldBmp);
            DeleteObject(hBmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// UpdateLayeredWindow + AC_SRC_ALPHA expects premultiplied BGRA.
    /// </summary>
    private static void PremultiplyAlpha(Bitmap bmp)
    {
        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadWrite,
            PixelFormat.Format32bppArgb);
        try
        {
            int bytes = Math.Abs(data.Stride) * bmp.Height;
            byte[] buf = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, bytes);
            for (int i = 0; i < bytes; i += 4)
            {
                byte a = buf[i + 3];
                if (a == 255) continue;
                if (a == 0)
                {
                    buf[i] = buf[i + 1] = buf[i + 2] = 0;
                    continue;
                }
                buf[i] = (byte)(buf[i] * a / 255);         // B
                buf[i + 1] = (byte)(buf[i + 1] * a / 255); // G
                buf[i + 2] = (byte)(buf[i + 2] * a / 255); // R
            }
            System.Runtime.InteropServices.Marshal.Copy(buf, 0, data.Scan0, bytes);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    #endregion

    #region Interaction

    private HitId HitTest(Point p)
    {
        if (rcOptions.Contains(p)) return HitId.Options;
        if (rcClickMode.Contains(p)) return HitId.ClickMode;
        if (rcPrev.Contains(p)) return HitId.Prev;
        if (rcNext.Contains(p)) return HitId.Next;
        if (syncCanRetry && !rcSyncStatus.IsEmpty && rcSyncStatus.Contains(p)) return HitId.SyncStatus;
        if (p.X >= Width - Edge && p.Y >= Height - Edge) return HitId.ResizeBR;
        if (p.X <= Edge) return HitId.ResizeL;
        if (p.X >= Width - Edge) return HitId.ResizeR;
        if (p.Y <= Edge) return HitId.ResizeT;
        if (p.Y >= Height - Edge) return HitId.ResizeB;
        if (rcDrag.Contains(p) || p.Y < ChromeH) return HitId.Drag;
        return HitId.None;
    }

    private void OnOverlayMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || settings.ClickThrough) return;
        var hit = HitTest(e.Location);
        switch (hit)
        {
            case HitId.Options:
                optionsMenu.Show(this, new Point(rcOptions.Left, rcOptions.Bottom));
                break;
            case HitId.ClickMode:
                SetClickThrough(!settings.ClickThrough, save: true);
                break;
            case HitId.Prev:
                TurnPage(-1);
                break;
            case HitId.Next:
                TurnPage(1);
                break;
            case HitId.SyncStatus:
                _ = RunCatalogSyncAsync();
                break;
            case HitId.Drag:
                dragging = true;
                suppressFollow = true;
                dragMouseScreen = Cursor.Position;
                dragFormLoc = Location;
                Capture = true;
                break;
            case HitId.ResizeL:
            case HitId.ResizeR:
            case HitId.ResizeT:
            case HitId.ResizeB:
            case HitId.ResizeBR:
                resizing = true;
                resizeKind = hit;
                suppressFollow = true;
                resizeStartBounds = Bounds;
                dragMouseScreen = Cursor.Position;
                Capture = true;
                break;
        }
    }

    private void OnOverlayMouseMove(object? sender, MouseEventArgs e)
    {
        if (settings.ClickThrough) return;

        if (dragging)
        {
            var now = Cursor.Position;
            Location = new Point(
                dragFormLoc.X + (now.X - dragMouseScreen.X),
                dragFormLoc.Y + (now.Y - dragMouseScreen.Y));
            RedrawLayered();
            return;
        }

        if (resizing)
        {
            var now = Cursor.Position;
            int dx = now.X - dragMouseScreen.X;
            int dy = now.Y - dragMouseScreen.Y;
            var b = resizeStartBounds;
            int left = b.Left, top = b.Top, right = b.Right, bottom = b.Bottom;
            if (resizeKind is HitId.ResizeL) left += dx;
            if (resizeKind is HitId.ResizeR or HitId.ResizeBR) right += dx;
            if (resizeKind is HitId.ResizeT) top += dy;
            if (resizeKind is HitId.ResizeB or HitId.ResizeBR) bottom += dy;
            int nw = Math.Clamp(right - left, MinimumSize.Width, MaximumSize.Width);
            int nh = Math.Clamp(bottom - top, MinimumSize.Height, MaximumSize.Height);
            if (resizeKind is HitId.ResizeL) left = right - nw;
            if (resizeKind is HitId.ResizeT) top = bottom - nh;
            Bounds = new Rectangle(left, top, nw, nh);
            settings.Width = nw;
            settings.Height = nh;
            RedrawLayered();
            return;
        }

        Cursor = HitTest(e.Location) switch
        {
            HitId.ResizeL or HitId.ResizeR => Cursors.SizeWE,
            HitId.ResizeT or HitId.ResizeB => Cursors.SizeNS,
            HitId.ResizeBR => Cursors.SizeNWSE,
            HitId.Drag => Cursors.SizeAll,
            HitId.Options or HitId.ClickMode or HitId.Prev or HitId.Next or HitId.SyncStatus => Cursors.Hand,
            _ => Cursors.Default
        };
    }

    private void OnOverlayMouseUp(object? sender, MouseEventArgs e)
    {
        if (dragging || resizing)
        {
            dragging = false;
            resizing = false;
            Capture = false;
            PersistGeometry();
            TipStore.SaveSettings(settings);
            suppressFollow = false;
            RedrawLayered();
            SyncBoundGame();
        }
    }

    private void OnOverlayMouseWheel(object? sender, MouseEventArgs e)
    {
        if (settings.ClickThrough) return;
        scrollY = Math.Max(0, scrollY - Math.Sign(e.Delta) * 28);
        RedrawLayered();
    }

    #endregion

    private void TurnPage(int delta)
    {
        if (current == null || current.Stages.Count == 0)
        {
            if (delta > 0) OpenDungeonPicker();
            return;
        }
        int next = stageIndex + delta;
        if (next < 0 || next >= current.Stages.Count) return;
        stageIndex = next;
        scrollY = 0;
        RedrawLayered();
    }

    private void OpenDungeonPicker()
    {
        using var dlg = new DungeonPickerForm(catalog.Dungeons, settings.LastDungeonId);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Selected == null) return;
        OpenDungeon(dlg.Selected, 0);
    }

    private void OpenDungeon(DungeonGuide d, int stage)
    {
        if (d.Stages.Count == 0)
        {
            MessageBox.Show(this, "這個副本還沒有關卡提示。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        current = d;
        stageIndex = Math.Clamp(stage, 0, d.Stages.Count - 1);
        scrollY = 0;
        settings.LastDungeonId = d.Id;
        TipStore.SaveSettings(settings);
        RedrawLayered();
    }

    private async Task RunCatalogSyncAsync()
    {
        if (syncBusy) return;
        syncBusy = true;
        syncStatusText = "";
        syncCanRetry = false;
        if (IsHandleCreated) RedrawLayered();

        CatalogSyncStatus status;
        try
        {
            status = await TipStore.SyncCatalogAsync().ConfigureAwait(true);
        }
        catch
        {
            status = CatalogSyncStatus.FetchFailed;
        }

        void Apply()
        {
            if (status == CatalogSyncStatus.Updated)
            {
                catalog = TipStore.LoadCatalog();
                RebindCurrentDungeon();
                syncStatusText = "更新成功";
                syncCanRetry = false;
            }
            else
            {
                syncStatusText = "抓取失敗（重新讀取）";
                syncCanRetry = true;
            }
            syncBusy = false;
            if (IsHandleCreated) RedrawLayered();
        }

        if (InvokeRequired)
            BeginInvoke(Apply);
        else
            Apply();
    }

    private void RebindCurrentDungeon()
    {
        if (catalog.Dungeons.Count == 0) return;

        string? id = current?.Id ?? settings.LastDungeonId;
        if (string.IsNullOrEmpty(id)) return;

        var found = catalog.Dungeons.FirstOrDefault(d => d.Id == id);
        if (found == null) return;

        int keepStage = stageIndex;
        current = found;
        stageIndex = Math.Clamp(keepStage, 0, Math.Max(0, found.Stages.Count - 1));
    }

    private void OpenSettings()
    {
        using var dlg = new SettingsForm(settings, this);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        settings.Width = dlg.Result.Width;
        settings.Height = dlg.Result.Height;
        settings.OpacityPercent = dlg.Result.OpacityPercent;
        settings.TextOpacityPercent = dlg.Result.TextOpacityPercent;
        settings.GameWindowTitle = dlg.Result.GameWindowTitle;
        settings.FollowGameWindow = dlg.Result.FollowGameWindow;
        settings.ClickThrough = dlg.Result.ClickThrough;
        settings.PosX = Left;
        settings.PosY = Top;
        TipStore.SaveSettings(settings);

        ApplyLivePreview(settings.Width, settings.Height, settings.OpacityPercent, settings.TextOpacityPercent);
        ApplyExStyles();
        ResolveGameWindow();
        SyncBoundGame();
        RedrawLayered();
    }

    private void ResolveGameWindow()
    {
        gameHwnd = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(settings.GameWindowTitle)) return;
        var hit = GameWindowHelper.ListWindows()
            .FirstOrDefault(w => w.Title.Equals(settings.GameWindowTitle, StringComparison.OrdinalIgnoreCase));
        if (hit != null) gameHwnd = hit.Handle;
    }

    private void PositionInitial()
    {
        ResolveGameWindow();
        if (settings.FollowGameWindow &&
            GameWindowHelper.TryGetClientScreenRect(gameHwnd, out var rc))
        {
            Location = new Point(rc.Left + settings.RelX, rc.Top + settings.RelY);
            return;
        }
        if (settings.HasSavedPosition)
        {
            Location = new Point(settings.PosX, settings.PosY);
            return;
        }
        if (GameWindowHelper.TryGetClientScreenRect(gameHwnd, out rc))
        {
            settings.RelX = Math.Max(8, rc.Width - Width - 16);
            settings.RelY = 16;
            Location = new Point(rc.Left + settings.RelX, rc.Top + settings.RelY);
            return;
        }
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(screen.Right - Width - 24, screen.Top + 80);
    }

    private void PersistGeometry()
    {
        if (!IsHandleCreated || WindowState != FormWindowState.Normal) return;
        settings.Width = Width;
        settings.Height = Height;
        settings.PosX = Left;
        settings.PosY = Top;
        if (settings.FollowGameWindow &&
            GameWindowHelper.TryGetClientScreenRect(gameHwnd, out var rc))
        {
            settings.RelX = Left - rc.Left;
            settings.RelY = Top - rc.Top;
        }
    }

    private void SyncBoundGame()
    {
        if (!Visible || !IsHandleCreated) return;
        ResolveGameWindow();

        IntPtr fg = GetForegroundWindow();
        bool gameFocused = gameHwnd != IntPtr.Zero && IsWindow(gameHwnd) &&
                           (fg == gameHwnd || GetAncestor(fg, GA_ROOT) == gameHwnd);
        bool selfFocused = fg == Handle;

        if (gameFocused || (selfFocused && !settings.ClickThrough))
        {
            if (!TopMost) TopMost = true;
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        else
        {
            if (TopMost) TopMost = false;
            SetWindowPos(Handle, HWND_NOTOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        if (settings.FollowGameWindow && !suppressFollow &&
            gameHwnd != IntPtr.Zero &&
            GameWindowHelper.TryGetClientScreenRect(gameHwnd, out var rc))
        {
            var target = new Point(rc.Left + settings.RelX, rc.Top + settings.RelY);
            if (Location != target)
            {
                Location = target;
                RedrawLayered();
            }
        }
    }

    private void PollGlobalHotkeys()
    {
        bool left = (GetAsyncKeyState(VK_LEFT) & 0x8000) != 0;
        bool right = (GetAsyncKeyState(VK_RIGHT) & 0x8000) != 0;
        bool f8 = (GetAsyncKeyState(VK_F8) & 0x8000) != 0;
        if (left && !keyLeftWas) TurnPage(-1);
        if (right && !keyRightWas) TurnPage(1);
        if (f8 && !keyF8Was) SetClickThrough(!settings.ClickThrough, save: true);
        keyLeftWas = left;
        keyRightWas = right;
        keyF8Was = f8;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        bindTimer.Stop();
        PersistGeometry();
        TipStore.SaveSettings(settings);
        base.OnFormClosed(e);
    }

    #region Native

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint GA_ROOT = 2;
    private const int VK_LEFT = 0x25;
    private const int VK_RIGHT = 0x27;
    private const int VK_F8 = 0x77;
    private const byte AC_SRC_OVER = 0;
    private const byte AC_SRC_ALPHA = 1;
    private const int ULW_ALPHA = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
        ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    #endregion
}
