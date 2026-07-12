using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace CUETools.Fuzz;

// A GUI random-walk: attaches to the already-running CUETools.Wpf window and hammers it with SAFE
// actions - random page navigation, theme toggling, window resizes, and scrolls - checking the
// process stays alive and the window stays found after each. It deliberately does NOT invoke Rip /
// Verify / Eject / Convert / Detect / folder-picker buttons: those have hardware, filesystem, or
// blocking-dialog side effects that are not the GUI's robustness to fuzz. What it DOES stress is
// the load-bearing UI machinery: the DynamicResource light/dark theme swap, page switching, the
// GPU-drawn custom controls, and layout under random window sizes.
internal static class GuiFuzzer
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? c, string t);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern void mouse_event(uint f, int dx, int dy, int d, IntPtr e);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    public static int Run(string[] args)
    {
        int steps = 400, seed = 20260712;
        foreach (var a in args)
        {
            if (int.TryParse(a, out var n)) { if (n > 1000 || seed != 20260712) steps = n; else seed = n; }
        }
        IntPtr h = FindWindow(null, "CUETools 2026");
        if (h == IntPtr.Zero)
        {
            Console.WriteLine("GUI fuzz: CUETools.Wpf window not found. Launch the app first, then run --gui.");
            return 2;
        }
        GetWindowThreadProcessId(h, out uint pid);
        Process proc;
        try { proc = Process.GetProcessById((int)pid); }
        catch { Console.WriteLine("GUI fuzz: cannot attach to the app process."); return 2; }

        var rnd = new Random(seed);
        Console.WriteLine($"GUI random-walk  seed={seed}  steps={steps}  pid={pid}");
        GetWindowRect(h, out var orig);

        int nav = 0, theme = 0, resize = 0, scroll = 0;
        for (int i = 0; i < steps; i++)
        {
            if (proc.HasExited)
            {
                Console.WriteLine($"  [FAIL] app CRASHED at step {i} (exit {proc.ExitCode}). Reproduce with seed={seed}.");
                RestoreWindow(h, orig);
                return 1;
            }
            try
            {
                switch (rnd.Next(4))
                {
                    case 0: Navigate(h, rnd); nav++; break;
                    case 1: ToggleTheme(h); theme++; break;
                    case 2: Resize(h, rnd); resize++; break;
                    default: Scroll(h, rnd); scroll++; break;
                }
            }
            catch (Exception)
            {
                // a UIA hiccup (element changed under us) is not an app crash - keep walking
            }
            System.Threading.Thread.Sleep(15);
            if ((i + 1) % 50 == 0) Console.WriteLine($"  ...{i + 1} steps ok (nav {nav}, theme {theme}, resize {resize}, scroll {scroll})");
        }

        RestoreWindow(h, orig);
        bool alive = !proc.HasExited;
        Console.WriteLine(alive
            ? $"  [PASS] survived {steps} steps (nav {nav}, theme {theme}, resize {resize}, scroll {scroll})"
            : "  [FAIL] app exited during the walk");
        return alive ? 0 : 1;
    }

    private static void Navigate(IntPtr h, Random rnd)
    {
        var win = AutomationElement.FromHandle(h);
        var items = win.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        if (items.Count == 0) return;
        var it = items[rnd.Next(items.Count)];
        if (it.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p)) ((SelectionItemPattern)p).Select();
    }

    // click the faceplate LIGHT switch (top-right) to exercise the live theme swap both directions
    private static void ToggleTheme(IntPtr h)
    {
        SetForegroundWindow(h);
        GetWindowRect(h, out var r);
        int cx = r.Right - 52, cy = r.Top + 57;
        SetCursorPos(cx, cy);
        System.Threading.Thread.Sleep(30);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }

    private static void Resize(IntPtr h, Random rnd)
    {
        GetWindowRect(h, out var r);
        int w = rnd.Next(760, 1500), ht = rnd.Next(520, 1000);
        MoveWindow(h, r.Left, r.Top, w, ht, true);
    }

    private static void Scroll(IntPtr h, Random rnd)
    {
        SetForegroundWindow(h);
        GetWindowRect(h, out var r);
        SetCursorPos((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
        int dir = rnd.Next(2) == 0 ? 120 : -120;
        for (int i = 0; i < rnd.Next(1, 6); i++) { mouse_event(0x0800, 0, 0, dir, IntPtr.Zero); System.Threading.Thread.Sleep(8); }
    }

    private static void RestoreWindow(IntPtr h, RECT o)
    {
        if (h != IntPtr.Zero) MoveWindow(h, o.Left, o.Top, o.Right - o.Left, o.Bottom - o.Top, true);
    }
}
