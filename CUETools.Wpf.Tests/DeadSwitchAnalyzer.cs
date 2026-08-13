using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// Pure source-analysis helper for DeadSwitchTests. Encodes, in code, the heuristic the
    /// settings audit used by hand: take a XAML value binding, resolve its path to the backing
    /// config field, then check whether any code outside the plumbing (declaration, defaults,
    /// copy-constructor, Save/Load, the ViewModel pass-through itself) actually reads that field.
    ///
    /// This is a heuristic over the repo's OWN SOURCE TEXT, not a full C# parser - it is meant to
    /// catch obvious dead switches, not to replace human judgment. Ambiguous or unresolvable
    /// bindings are reported as "unresolved" rather than guessed at, so the test never asserts on
    /// a binding it is not confident about (see DeadSwitchTests for how that is used).
    /// </summary>
    internal static class DeadSwitchAnalyzer
    {
        // ==================== repo root ====================

        /// <summary>Walk up from <paramref name="startDir"/> until a directory contains both
        /// CUETools.Wpf and CUETools.Processor. Returns null if it reaches the filesystem root
        /// without finding one (the caller should Assert.Inconclusive in that case).</summary>
        public static string FindRepoRoot(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "CUETools.Wpf")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "CUETools.Processor")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        // ==================== step 1: XAML binding scan ====================

        public sealed class BoundProperty
        {
            public string Name = "";
            public string XamlFile = "";
        }

        // Value-carrying bindings only: IsChecked (toggles/radios), Text (boxes and, deliberately,
        // read-only display text - see the NonSettingSuffixes filter below), Value (progress/slider
        // style), SelectedIndex / SelectedItem (combo pickers).
        private static readonly Regex BindingAttr = new Regex(
            "\\b(?:IsChecked|Text|Value|SelectedIndex|SelectedItem)\\s*=\\s*\"\\{Binding\\s*([^,}\"]*)",
            RegexOptions.Compiled);

        // Deliberately narrow, per the brief: commands, plain display text, visibility flags and
        // collections are not settings. Everything else is kept, even if it turns out to be a
        // display-only property - ResolveViewModelProperties will simply fail to resolve those to
        // a backing member and they land in "unresolved", not "dead". A false negative here (over-
        // filtering) would defeat the test, so this list stays short on purpose.
        private static readonly string[] NonSettingSuffixes = { "Command", "Text", "Visible" };

        public static List<BoundProperty> ScanXamlBindings(string repoRoot)
        {
            var results = new List<BoundProperty>();
            string viewsDir = Path.Combine(repoRoot, "CUETools.Wpf", "Views");
            if (!Directory.Exists(viewsDir)) return results;

            foreach (var file in Directory.GetFiles(viewsDir, "*.xaml", SearchOption.TopDirectoryOnly))
            {
                string text = File.ReadAllText(file);
                foreach (Match m in BindingAttr.Matches(text))
                {
                    string raw = m.Groups[1].Value.Trim();
                    if (raw.Length == 0) continue;                                  // "{Binding}" - the DataContext itself
                    if (raw.Contains('.')) continue;                                // nested path (Details.Model) - not a settings root
                    if (!Regex.IsMatch(raw, "^[A-Za-z_][A-Za-z0-9_]*$")) continue;   // not a plain identifier
                    if (NonSettingSuffixes.Any(s => raw.EndsWith(s, StringComparison.Ordinal))) continue;
                    results.Add(new BoundProperty { Name = raw, XamlFile = Path.GetFileName(file) });
                }
            }
            return results;
        }

        // ==================== step 2: resolve each bound property to its backing member ====================

        public sealed class ResolvedProperty
        {
            public string BackingMember;      // null = could not be determined
            public string SourceFile = "";
            public int StartLine;            // 1-based, inclusive
            public int EndLine;               // 1-based, inclusive
        }

        /// <summary>For each distinct VM property name, find its declaration in the first
        /// CUETools.Wpf/ViewModels/*ViewModel.cs file (in a stable file-name order) that declares
        /// it, and resolve the backing expression in its body. A handful of property names are
        /// reused across more than one page's view model (e.g. SelectedFormat on Convert/Rip/Queue);
        /// taking the first declaration is a simplification, but it does not change any real
        /// finding here - see DeadSwitchTests' report for the ones that end up unresolved.</summary>
        public static Dictionary<string, ResolvedProperty> ResolveViewModelProperties(string repoRoot, IEnumerable<string> propertyNames)
        {
            var result = new Dictionary<string, ResolvedProperty>();
            // View models live in CUETools.Wpf and, since the shared app-core
            // extraction, in CUETools.App.Core; both hold WPF-bound pages.
            string[] vmDirs =
            {
                Path.Combine(repoRoot, "CUETools.Wpf", "ViewModels"),
                Path.Combine(repoRoot, "CUETools.App.Core", "ViewModels"),
            };
            var files = vmDirs.Where(Directory.Exists)
                .SelectMany(dir => Directory.GetFiles(dir, "*ViewModel.cs", SearchOption.TopDirectoryOnly))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length == 0) return result;
            var fileText = files.ToDictionary(f => f, File.ReadAllText);

            foreach (var name in propertyNames.Distinct())
            {
                foreach (var file in files)
                {
                    var decl = FindPropertyDecl(fileText[file], name);
                    if (decl == null) continue;
                    result[name] = new ResolvedProperty
                    {
                        BackingMember = ResolveBackingMember(decl.Body),
                        SourceFile = file,
                        StartLine = decl.StartLine,
                        EndLine = decl.EndLine,
                    };
                    break;
                }
            }
            return result;
        }

        private sealed class PropertyDecl
        {
            public string Body = "";
            public int StartLine;
            public int EndLine;
        }

        // Matches "public <type> Name" followed by either a block ("{ get ... }") or an
        // expression body ("=> expr;"). The type token is left generic (anything up to the name
        // that is not a brace/semicolon/newline) so generics, nullable refs, etc. all work.
        private static PropertyDecl FindPropertyDecl(string text, string name)
        {
            var m = Regex.Match(text, @"(?m)^[ \t]*public\s+[^\n;{=]+?\b" + Regex.Escape(name) + @"\b\s*(\{|=>)");
            if (!m.Success) return null;

            int afterName = m.Index + m.Length;
            char opener = m.Groups[1].Value[0];
            int bodyStart, bodyEndExclusive;

            if (opener == '{')
            {
                int braceStart = afterName - 1;   // the '{' just matched
                int depth = 0;
                int i = braceStart;
                for (; i < text.Length; i++)
                {
                    if (text[i] == '{') depth++;
                    else if (text[i] == '}') { depth--; if (depth == 0) { i++; break; } }
                }
                bodyStart = braceStart;
                bodyEndExclusive = i;
            }
            else
            {
                // expression-bodied property: "=> expr;" - runs to the next top-level ';'. None of
                // these properties nest a ';' inside brackets/strings in this codebase, so a plain
                // scan for the next ';' is enough.
                int semi = text.IndexOf(';', afterName);
                if (semi < 0) semi = text.Length - 1;
                bodyStart = afterName;
                bodyEndExclusive = semi + 1;
            }

            string body = text.Substring(bodyStart, Math.Max(0, bodyEndExclusive - bodyStart));
            int startLine = 1 + CountNewlines(text, 0, m.Index);
            int endLine = 1 + CountNewlines(text, 0, Math.Min(bodyEndExclusive, text.Length));
            return new PropertyDecl { Body = body, StartLine = startLine, EndLine = endLine };
        }

        private static int CountNewlines(string text, int from, int to)
        {
            int n = 0;
            for (int i = from; i < to && i < text.Length; i++)
                if (text[i] == '\n') n++;
            return n;
        }

        // Chains like "_c.ejectAfterRip", "_c.advanced.CreateTOC", "_enc.Settings.EncoderMode":
        // an underscore-prefixed field followed by one or more ".Member" hops. A chain immediately
        // followed by '(' is a method call (e.g. reflection's "_prop.GetValue(...)") and is not
        // treated as a field/property read. Of everything left, the LAST match in the property
        // body wins - for a get/set pair that both mention the same chain this is a no-op, and for
        // a pattern like "get => _field; set { ... _settings.X = value; }" it correctly prefers the
        // setter's real sink over a bare, dot-less backing field the getter alone cannot name.
        private static readonly Regex BackingChain = new Regex(
            @"_[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+", RegexOptions.Compiled);

        private static string ResolveBackingMember(string body)
        {
            string last = null;
            foreach (Match m in BackingChain.Matches(body))
            {
                int k = m.Index + m.Length;
                while (k < body.Length && char.IsWhiteSpace(body[k])) k++;
                if (k < body.Length && body[k] == '(') continue;   // method call, not a field read
                string chain = m.Value;
                last = chain.Substring(chain.LastIndexOf('.') + 1);
            }
            return last;
        }

        // ==================== step 3: count real consumers of a backing member ====================

        // Exact substrings from the brief. A line containing any of these is persistence plumbing
        // (Save/Load round-trip), not a real reader of the value.
        private static readonly string[] PersistenceMarkers = { "sw.Save", "sr.Load", "SaveText", "LoadBoolean", "LoadInt32", "JsonConvert" };

        public sealed class ConsumerScan
        {
            public int Count;
            public List<string> SampleLines = new();
        }

        /// <summary>Search CUETools.Wpf, CUETools.App.Core (the shared app core the
        /// view models and portable services moved into), CUETools.Processor and
        /// CUETools.Ripper.SCSI (excluding bin/obj) for real, non-plumbing references
        /// to <paramref name="member"/>. The
        /// <paramref name="excludedSpans"/> are the (file, first line, last line) of every
        /// ViewModel property whose body resolved to this exact member - i.e. the pass-through
        /// itself, excluded per rule 5 of the brief.</summary>
        public static ConsumerScan CountRealConsumers(string repoRoot, string member, IReadOnlyList<(string file, int start, int end)> excludedSpans)
        {
            var scan = new ConsumerScan();
            var wordRegex = new Regex(@"\b" + Regex.Escape(member) + @"\b");
            string[] dirs = { "CUETools.Wpf", "CUETools.App.Core", "CUETools.Processor", "CUETools.Ripper.SCSI" };

            foreach (var d in dirs)
            {
                string full = Path.Combine(repoRoot, d);
                if (!Directory.Exists(full)) continue;

                foreach (var file in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
                {
                    if (PathHasSegment(file, "bin") || PathHasSegment(file, "obj")) continue;

                    string[] lines;
                    try { lines = File.ReadAllLines(file); }
                    catch (IOException) { continue; }   // best-effort; a locked/transient file is not fatal to the guard

                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        if (!wordRegex.IsMatch(line)) continue;

                        // A mention inside a // or /// comment is not code reading the field.
                        int slashSlash = line.IndexOf("//", StringComparison.Ordinal);
                        string codePart = slashSlash >= 0 ? line.Substring(0, slashSlash) : line;
                        if (!wordRegex.IsMatch(codePart)) continue;

                        int lineNo = i + 1;
                        bool inPassThroughSpan = false;
                        foreach (var span in excludedSpans)
                        {
                            if (string.Equals(span.file, file, StringComparison.OrdinalIgnoreCase) &&
                                lineNo >= span.start && lineNo <= span.end)
                            { inPassThroughSpan = true; break; }
                        }
                        if (inPassThroughSpan) continue;

                        if (IsDeclarationLine(codePart, member)) continue;
                        if (IsLiteralAssignmentLine(codePart, member)) continue;
                        if (IsCopyCtorLine(codePart, member)) continue;
                        if (PersistenceMarkers.Any(codePart.Contains)) continue;
                        // A config field is always reached through something: "_config.x",
                        // "advanced.x", "cfg.advanced.x". A BARE occurrence of the same word is a
                        // different symbol that merely shares the name - a method parameter or a local.
                        // That is not a theoretical concern: CUESheet.LookupAlbumInfo takes a parameter
                        // called "metadataSearch", which made the genuinely dead
                        // CUEConfigAdvanced.metadataSearch look alive and slipped a real dead switch
                        // past this guard.
                        if (!IsQualifiedMemberAccess(codePart, member)) continue;

                        scan.Count++;
                        if (scan.SampleLines.Count < 3) scan.SampleLines.Add($"{file}:{lineNo}: {line.Trim()}");
                    }
                }
            }
            return scan;
        }

        /// <summary>True when at least one occurrence of <paramref name="member"/> on this line is
        /// preceded by a '.', i.e. an actual member access rather than a same-named parameter or local.
        /// Only such a reference can be a real read of the config field.</summary>
        private static bool IsQualifiedMemberAccess(string codePart, string member)
        {
            int from = 0;
            while (true)
            {
                int at = codePart.IndexOf(member, from, StringComparison.Ordinal);
                if (at < 0) return false;
                int after = at + member.Length;
                bool wholeWord = (at == 0 || !IsIdentChar(codePart[at - 1]) || codePart[at - 1] == '.')
                                 && (after >= codePart.Length || !IsIdentChar(codePart[after]));
                // walk back over any whitespace to find the character that introduces this token
                int p = at - 1;
                while (p >= 0 && char.IsWhiteSpace(codePart[p])) p--;
                if (wholeWord && p >= 0 && codePart[p] == '.') return true;
                from = at + 1;
            }
        }

        private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        private static bool PathHasSegment(string path, string segment)
        {
            string sep = Path.DirectorySeparatorChar.ToString();
            return path.Contains(sep + segment + sep, StringComparison.OrdinalIgnoreCase)
                || path.Contains("/" + segment + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDeclarationLine(string codePart, string name)
        {
            // "public bool noUnverifiedOutput;" / "public bool CTDBSubmit { get; set; }" /
            // "public bool X { get; set; } = true;" - the field/property declaration itself.
            return Regex.IsMatch(codePart,
                @"^\s*(?:\[[^\]]*\]\s*)*(?:public|private|protected|internal)\s+.*?\b" + Regex.Escape(name) + @"\b\s*(;|\{|=(?!=))");
        }

        private static bool IsLiteralAssignmentLine(string codePart, string name)
        {
            // "noUnverifiedOutput = false;" / "c.detectHDCD = true;" - a defaults/Init assignment
            // to a bare literal, not a computed or forwarded value.
            return Regex.IsMatch(codePart,
                @"(?:^|[\s{;])(?:[A-Za-z_]\w*\.)*" + Regex.Escape(name) + @"\s*=\s*(true|false|null|-?\d+(?:\.\d+)?|""(?:[^""\\]|\\.)*"")\s*;");
        }

        private static bool IsCopyCtorLine(string codePart, string name)
        {
            // "fixOffset = src.fixOffset;" - CUEConfig's copy-constructor pattern.
            return Regex.IsMatch(codePart,
                @"(?:^|[\s{;])(?:[A-Za-z_]\w*\.)*" + Regex.Escape(name) + @"\s*=\s*[A-Za-z_]\w*\." + Regex.Escape(name) + @"\s*;");
        }
    }
}
