// Zero Hour LAN Launcher: plays Zero Hour Reforged v3.6.0 with the mods from a LAN pack folder.
//
//   ZHLauncher.exe                       the pack is the folder the exe sits in
//   ZHLauncher.exe --pack D:\ZH-LAN-Pack --data D:\ZHLauncherData
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Zero Hour LAN Launcher")]
[assembly: System.Reflection.AssemblyProduct("Zero Hour LAN Launcher")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace ZHLauncher
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string pack = AppDomain.CurrentDomain.BaseDirectory;
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZHLauncher");
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "--pack") pack = args[++i];
                else if (args[i] == "--data") data = args[++i];
            }

            Engine engine;
            try { engine = new Engine(pack, data); }
            catch (Exception e)
            {
                MessageBox.Show("Could not read the pack in " + pack + ":\n\n" + e.Message, "Zero Hour LAN Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Application.ThreadException += (s, e) =>
            {
                engine.Log("crash: " + e.Exception);
                MessageBox.Show(e.Exception.Message, "Zero Hour LAN Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new MainForm(engine));
        }
    }
}
