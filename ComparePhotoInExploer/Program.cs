namespace ComparePhotoInExploer;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();

        Application.ThreadException += (s, e) =>
        {
            MessageBox.Show($"UI线程异常:\n{e.Exception}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            MessageBox.Show($"未处理异常:\n{e.ExceptionObject}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        Application.Run(new Form1(args));
    }    
}