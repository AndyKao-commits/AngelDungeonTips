namespace AngelDungeonTips;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ShowFatal(ex);
        };

        try
        {
            // Startup must NOT own the overlay via ShowDialog — that pushes the tip to the bottom.
            using (var startup = new StartupForm())
            {
                if (startup.ShowDialog() != DialogResult.OK)
                    return;
            }

            Application.Run(new MainOverlayForm());
        }
        catch (Exception ex)
        {
            ShowFatal(ex);
        }
    }

    private static void ShowFatal(Exception ex)
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "AngelDungeonTips-crash.txt");
            File.WriteAllText(path, ex.ToString());
        }
        catch { /* ignore */ }

        MessageBox.Show(ex.ToString(), "AngelDungeonTips 錯誤",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
