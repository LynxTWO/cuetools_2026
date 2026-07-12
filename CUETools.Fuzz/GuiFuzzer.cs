using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace CUETools.Fuzz;

// A GUI random-walk over SAFE actions - random page navigation, switch toggling (theme + option
// switches), and window resizes - checking the app stays alive after each. It stresses the
// load-bearing UI: the DynamicResource light/dark theme swap, page switching, the GPU-drawn custom
// controls, and layout under random window sizes.
//
// MOUSE-FREE by design: it drives controls through UIAutomation patterns (SelectionItem, Toggle,
// ScrollItem) and resizes with MoveWindow - it never moves the physical cursor or steals focus, so
// you can keep using the machine while it runs and your mouse won't fight it. It deliberately does
// NOT invoke Rip / Verify / Eject / Convert / Detect / folder-picker controls (hardware, filesystem,
// or blocking-dialog side effects).
internal static class GuiFuzzer
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? c, string t);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    public static int Run(string[] args)
    {
        // --gui [seed] [steps]
        var nums = new List<int>();
        foreach (var a in args) if (a != "--gui" && int.TryParse(a, out var v)) nums.Add(v);
        int seed = nums.Count > 0 ? nums[0] : 20260712;
        int steps = nums.Count > 1 ? nums[1] : 300;

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
        Console.WriteLine($"GUI random-walk (mouse-free)  seed={seed}  steps={steps}  pid={pid}");
        GetWindowRect(h, out var orig);
        int nav = 0, toggle = 0, resize = 0;

        for (int i = 0; i < steps; i++)
        {
            if (proc.HasExited)
            {
                Console.WriteLine($"  [FAIL] app EXITED at step {i}. Reproduce with seed={seed}.");
                RestoreWindow(h, orig);
                return 1;
            }
            try
            {
                var win = AutomationElement.FromHandle(h);   // re-fetch: the tree changes under us
                switch (rnd.Next(3))
                {
                    case 0: if (Navigate(win, rnd)) nav++; break;
                    case 1: if (ToggleSwitch(win, rnd)) toggle++; break;
                    default: Resize(h, rnd); resize++; break;
                }
            }
            catch (Exception)
            {
                // a UIA hiccup (element changed mid-action, window busy) is not an app crash
            }
            System.Threading.Thread.Sleep(20);
            if ((i + 1) % 50 == 0)
            {
                proc.Refresh();
                Console.WriteLine($"  ...{i + 1} steps ok (nav {nav}, toggle {toggle}, resize {resize})  mem={proc.WorkingSet64 / (1024 * 1024)}MB");
            }
        }

        RestoreWindow(h, orig);
        bool alive = !proc.HasExited;
        Console.WriteLine(alive
            ? $"  [PASS] survived {steps} steps (nav {nav}, toggle {toggle}, resize {resize})"
            : "  [FAIL] app exited during the walk");
        return alive ? 0 : 1;
    }

    // Exhaustively drive every switch through all 2^N combinations (or a random sample when N is
    // large), checking the app stays healthy. Toggling only sets a config bool / the theme, so no
    // combination should misbehave - this proves it and would catch a setter/binding surprise.
    public static int RunToggleSweep(string[] args)
    {
        var nums = new List<int>();
        foreach (var a in args) if (a != "--toggles" && int.TryParse(a, out var v)) nums.Add(v);
        int seed = nums.Count > 0 ? nums[0] : 20260712;

        IntPtr h = FindWindow(null, "CUETools 2026");
        if (h == IntPtr.Zero) { Console.WriteLine("toggle sweep: window not found. Launch the app first."); return 2; }
        GetWindowThreadProcessId(h, out uint pid);
        Process proc;
        try { proc = Process.GetProcessById((int)pid); } catch { Console.WriteLine("cannot attach."); return 2; }

        // land on Settings (the most switches), then include whatever toggles the window exposes
        SelectNav(AutomationElement.FromHandle(h), "Settings");
        System.Threading.Thread.Sleep(500);
        var toggles = FindToggles(AutomationElement.FromHandle(h));
        int n = toggles.Count;
        if (n == 0) { Console.WriteLine("toggle sweep: no switches found."); return 2; }
        if (n > 16) n = 16;   // cap the exponent

        long total = 1L << n;
        bool exhaustive = total <= 4096;
        int runs = exhaustive ? (int)total : 4096;
        var rnd = new Random(seed);
        Console.WriteLine($"toggle sweep  {n} switches  {(exhaustive ? "all " + total : runs + " sampled")} combinations  pid={pid}");

        for (int c = 0; c < runs; c++)
        {
            long combo = exhaustive ? c : (long)(rnd.NextDouble() * total);
            for (int j = 0; j < n; j++)
            {
                try
                {
                    var tb = toggles[j];
                    if (!tb.TryGetCurrentPattern(TogglePattern.Pattern, out var p)) continue;
                    var tp = (TogglePattern)p;
                    bool want = ((combo >> j) & 1) == 1;
                    if ((tp.Current.ToggleState == ToggleState.On) != want) tp.Toggle();
                }
                catch (ElementNotAvailableException)
                {
                    toggles = FindToggles(AutomationElement.FromHandle(h));   // tree changed; re-fetch
                    if (toggles.Count < n) return Done(proc, "toggle sweep aborted: switch set changed", false);
                }
                catch (Exception) { /* transient UIA hiccup */ }
            }
            if (proc.HasExited)
            {
                Console.WriteLine($"  [FAIL] app EXITED at combination {c} (bits {Convert.ToString(combo, 2)}). seed={seed}");
                return 1;
            }
            if ((c + 1) % 64 == 0) { proc.Refresh(); Console.WriteLine($"  ...{c + 1}/{runs} combos ok  mem={proc.WorkingSet64 / (1024 * 1024)}MB"); }
        }
        return Done(proc, $"{runs} toggle combinations across {n} switches, app healthy", !proc.HasExited);
    }

    private static int Done(Process proc, string msg, bool ok)
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {msg}");
        return ok ? 0 : 1;
    }

    private static void SelectNav(AutomationElement win, string name)
    {
        var el = win.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name));
        if (el != null && el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p)) ((SelectionItemPattern)p).Select();
    }

    private static List<AutomationElement> FindToggles(AutomationElement win)
    {
        var list = new List<AutomationElement>();
        var found = win.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.IsTogglePatternAvailableProperty, true));
        foreach (AutomationElement e in found) list.Add(e);
        return list;
    }

    private static bool Navigate(AutomationElement win, Random rnd)
    {
        var items = win.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        if (items.Count == 0) return false;
        var it = items[rnd.Next(items.Count)];
        if (it.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p)) { ((SelectionItemPattern)p).Select(); return true; }
        return false;
    }

    // Toggle a random switch (the theme switch + the per-page option switches). TogglePattern flips
    // the bound IsChecked directly - no mouse, and no CommandManager-requery timing issue.
    private static bool ToggleSwitch(AutomationElement win, Random rnd)
    {
        var toggles = win.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.IsTogglePatternAvailableProperty, true));
        if (toggles.Count == 0) return false;
        var tb = toggles[rnd.Next(toggles.Count)];
        if (tb.TryGetCurrentPattern(TogglePattern.Pattern, out var p)) { ((TogglePattern)p).Toggle(); return true; }
        return false;
    }

    private static void Resize(IntPtr h, Random rnd)
    {
        GetWindowRect(h, out var r);
        MoveWindow(h, r.Left, r.Top, rnd.Next(760, 1500), rnd.Next(520, 1000), true);
    }

    private static void RestoreWindow(IntPtr h, RECT o)
    {
        if (h != IntPtr.Zero) MoveWindow(h, o.Left, o.Top, o.Right - o.Left, o.Bottom - o.Top, true);
    }
}
