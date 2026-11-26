using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace ClubEQ;

public partial class App : Application
{
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--selftest"))
        {
            AttachConsole(-1); // print into the terminal that started us
            int code = SelfTest.Run();
            Shutdown(code);
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--smoke")
        {
            AttachConsole(-1);
            Shutdown(SelfTest.Smoke(e.Args[1]));
            return;
        }
        if (e.Args.Contains("--devices")) // what FMOD sees, for debugging routing
        {
            AttachConsole(-1);
            using var en = new Audio.Engine();
            Console.WriteLine("\nInputs:\n  " + string.Join("\n  ", en.Inputs()) + "\nOutputs:\n  " + string.Join("\n  ", en.Outputs()));
            Shutdown(0);
            return;
        }
        new MainWindow().Show();
    }
}
