using System.Runtime.InteropServices;
using System.Text;

namespace AngelDungeonTips;

public sealed class GameWindowInfo
{
    public IntPtr Handle { get; init; }
    public string Title { get; init; } = "";
    public override string ToString() => Title;
}

public static class GameWindowHelper
{
    public static List<GameWindowInfo> ListWindows()
    {
        var list = new List<GameWindowInfo>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            if (GetWindow(h, 4) != IntPtr.Zero) return true;
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            string title = sb.ToString().Trim();
            if (title.Length < 2) return true;
            if (title.Contains("AngelDungeonTips", StringComparison.OrdinalIgnoreCase)) return true;
            if (title.Contains("AngelMapMarker", StringComparison.OrdinalIgnoreCase)) return true;
            GetClientRect(h, out var rc);
            if (rc.Right - rc.Left < 200 || rc.Bottom - rc.Top < 150) return true;
            list.Add(new GameWindowInfo { Handle = h, Title = title });
            return true;
        }, IntPtr.Zero);

        return list
            .OrderByDescending(w => Score(w.Title))
            .ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static bool TryGetClientScreenRect(IntPtr hwnd, out Rectangle screenRect)
    {
        screenRect = Rectangle.Empty;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
        GetClientRect(hwnd, out var client);
        int w = client.Right - client.Left;
        int h = client.Bottom - client.Top;
        if (w < 32 || h < 32) return false;
        var pt = new Point(0, 0);
        ClientToScreen(hwnd, ref pt);
        screenRect = new Rectangle(pt.X, pt.Y, w, h);
        return true;
    }

    private static int Score(string title)
    {
        string t = title.ToLowerInvariant();
        int s = 0;
        if (t.Contains("angel") || t.Contains("alog") || title.Contains("天使") || title.Contains("愛神")) s += 50;
        if (title.Contains("國際") || title.Contains("線上") || t.Contains("online")) s += 20;
        return s;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }
}
