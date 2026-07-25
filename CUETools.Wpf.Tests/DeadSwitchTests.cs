using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// Dead-switch guard. A settings audit found user-visible options that are bound in a View,
    /// persisted by CUEConfig/CUEConfigAdvanced/AppSettings, and yet read by nothing outside that
    /// plumbing - "a switch that does nothing is a lie". This test encodes the exact heuristic the
    /// audit used by hand: take a XAML value binding, resolve its path to the backing config
    /// field, then check whether any code outside the plumbing (declaration, defaults, copy-
    /// constructor, Save/Load, the ViewModel pass-through itself) actually reads that field.
    ///
    /// It reads the REPO'S OWN SOURCE at test time via <see cref="DeadSwitchAnalyzer"/> (not a
    /// fixed snapshot), so it fails the moment a new dead switch appears, and fails again - on
    /// purpose - the moment a KnownDead entry gets wired up or its row removed from the UI: that
    /// is the "list drains, not rots" rule from the brief. Each entry below is a tracked audit
    /// finding awaiting an owner decision; see docs/review/decisions-needed.md.
    /// </summary>
    [TestClass]
    public class DeadSwitchTests
    {
        public TestContext TestContext { get; set; }

        // Backing-member names (not the ViewModel property names) that the audit found bound,
        // persisted, and never read for real. Each line carries the one-line reason it is here.
        private static readonly HashSet<string> KnownDead = new(StringComparer.Ordinal)
        {
            "noUnverifiedOutput",   // tracked audit finding, awaiting an owner decision - CUEConfig.noUnverifiedOutput has no reader beyond declaration/defaults/copy-ctor/Save/Load
            "fixOffset",            // tracked audit finding, awaiting an owner decision - CUEConfig.fixOffset has no reader beyond declaration/defaults/copy-ctor/Save/Load
            "disableEjectDisc",     // tracked audit finding, awaiting an owner decision - CUEConfig.disableEjectDisc is never consulted where the eject actually happens (only ejectAfterRip is)
            "CTDBSubmit",           // tracked audit finding, awaiting an owner decision - CUEConfigAdvanced.CTDBSubmit round-trips only through the generic Advanced JSON blob; nothing reads the property
            "CTDBAsk",              // tracked audit finding, awaiting an owner decision - CUEConfigAdvanced.CTDBAsk round-trips only through the generic Advanced JSON blob; nothing reads the property
            "coversSize",           // AUDIT: newly detected, needs triage - CUEConfigAdvanced.coversSize is read only by the legacy WinForms CUERipper, not by anything under CUETools.Wpf/CUETools.Processor/CUETools.Ripper.SCSI
            "CacheMetadata",        // AUDIT: newly detected, needs triage - CUEConfigAdvanced.CacheMetadata is read only by the legacy WinForms CUETools app, not by anything under CUETools.Wpf/CUETools.Processor/CUETools.Ripper.SCSI
        };

        [TestMethod]
        public void EveryBoundSetting_HasARealConsumer_OrIsATrackedKnownDeadFinding()
        {
            string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
            if (repoRoot == null)
            {
                Assert.Inconclusive("Could not locate the repo root (a directory containing both " +
                    "CUETools.Wpf and CUETools.Processor) above " + AppContext.BaseDirectory + " - " +
                    "skipping the dead-switch scan rather than failing for an unrelated reason.");
                return;
            }

            var bindings = DeadSwitchAnalyzer.ScanXamlBindings(repoRoot);
            // A loose sanity floor, not a precise count: if the Views folder moved or the scan
            // regressed to matching nothing, fail loudly here instead of silently passing empty.
            Assert.IsTrue(bindings.Count > 15,
                $"expected a substantial number of XAML value bindings under CUETools.Wpf/Views; found only {bindings.Count} - the scan may be broken");

            var distinctNames = bindings.Select(b => b.Name).Distinct().ToList();
            var resolved = DeadSwitchAnalyzer.ResolveViewModelProperties(repoRoot, distinctNames);

            // Several ViewModel properties can wrap the same underlying config field (e.g.
            // RipViewModel.CreateCue and SettingsViewModel.CreateCueInTracksMode both wrap
            // CUEConfig.createCUEFileInTracksMode) - group by the resolved backing member so it is
            // checked once, with every one of its pass-through lines excluded from the count.
            var spansByMember = new Dictionary<string, List<(string file, int start, int end)>>(StringComparer.Ordinal);
            var xamlFilesByMember = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var unresolved = new List<string>();

            foreach (var b in bindings)
            {
                if (!resolved.TryGetValue(b.Name, out var r) || r.BackingMember == null)
                {
                    unresolved.Add($"{b.Name} ({b.XamlFile})");
                    continue;
                }

                if (!spansByMember.TryGetValue(r.BackingMember, out var spans))
                    spansByMember[r.BackingMember] = spans = new List<(string, int, int)>();
                spans.Add((r.SourceFile, r.StartLine, r.EndLine));

                if (!xamlFilesByMember.TryGetValue(r.BackingMember, out var xamlFiles))
                    xamlFilesByMember[r.BackingMember] = xamlFiles = new List<string>();
                if (!xamlFiles.Contains(b.XamlFile)) xamlFiles.Add(b.XamlFile);
            }

            var dead = new List<string>();
            foreach (var member in spansByMember.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                var scan = DeadSwitchAnalyzer.CountRealConsumers(repoRoot, member, spansByMember[member]);
                if (scan.Count == 0) dead.Add(member);
            }

            TestContext.WriteLine($"XAML value bindings scanned: {bindings.Count} (distinct property names: {distinctNames.Count})");
            TestContext.WriteLine($"Resolved to a backing member: {spansByMember.Count}; left unresolved: {unresolved.Count}");
            if (unresolved.Count > 0)
                TestContext.WriteLine("Unresolved (backing member could not be determined - not asserted on): " + string.Join(", ", unresolved.OrderBy(x => x, StringComparer.Ordinal)));
            TestContext.WriteLine("Dead (zero real consumers found): " + (dead.Count == 0 ? "(none)" : string.Join(", ", dead)));

            // (a) a dead member not on the allowlist is a NEW regression - name it and its binding site.
            var unexpectedlyDead = dead.Where(m => !KnownDead.Contains(m)).ToList();
            // (b) an allowlisted member that is no longer dead means the list has rotted - drain it.
            var noLongerDead = KnownDead.Where(m => !dead.Contains(m)).ToList();

            if (unexpectedlyDead.Count > 0 || noLongerDead.Count > 0)
            {
                var msg = new System.Text.StringBuilder();
                msg.AppendLine();
                msg.AppendLine("Dead switches NOT in KnownDead (new regression - add them to KnownDead in DeadSwitchTests.cs, or wire the setting up):");
                if (unexpectedlyDead.Count == 0) msg.AppendLine("  (none)");
                else foreach (var m in unexpectedlyDead)
                    msg.AppendLine($"  {m}  bound via: {string.Join(", ", xamlFilesByMember[m])}");
                msg.AppendLine("KnownDead entries that are NO LONGER dead (delete them from KnownDead in DeadSwitchTests.cs):");
                if (noLongerDead.Count == 0) msg.AppendLine("  (none)");
                else foreach (var m in noLongerDead) msg.AppendLine($"  {m}");
                Assert.Fail(msg.ToString());
            }
        }
    }
}
