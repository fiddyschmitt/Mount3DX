namespace Mount3DX
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            //Only allow one instance; a second instance would fail to bind the WebDAV port with a confusing error.
            //Global, because the port is machine-wide even across user sessions.
            using var singleInstanceMutex = new Mutex(initiallyOwned: true, @"Global\Mount3DX", out var isFirstInstance);
            if (!isFirstInstance)
            {
                MessageBox.Show($"{Form1.PROGRAM_NAME} is already running.", Form1.PROGRAM_NAME, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}