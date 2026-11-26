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
        if (e.Args.Length > 0 && e.Args[0] is "--serve" or "--record" or "--measure-clubeq" or "--analyze")
        {
            AttachConsole(-1);
            var a = e.Args;
            float F(int i, float d) => a.Length > i ? float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture) : d;
            int code;
            try
            {
                code = a[0] switch
                {
                    "--serve" => Measure.Serve(a.Length > 1 ? int.Parse(a[1]) : 8765),
                    "--record" => Measure.RecordCmd(a[1], F(2, 40), a[3]),
                    "--measure-clubeq" => Measure.MeasureClubEQ(a[1], F(2, 0), F(3, 0), F(4, 0), a.Length > 5 ? a[5] : "Garage", F(6, 1)),
                    _ => Measure.Analyze(a[1], a.Length > 2 ? a[2] : null),
                };
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); code = 1; }
            Shutdown(code);
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--pan")
        {
            AttachConsole(-1);
            Shutdown(SelfTest.Pan(e.Args[1]));
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
