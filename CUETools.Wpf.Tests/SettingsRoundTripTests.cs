using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CUETools.Processor;
using CUETools.Processor.Settings;
using CUETools.Wpf.Services;
using CUETools.Wpf.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// Settings save/load round-trip guards, written for a settings audit that found three bug
    /// classes on the persistence path (CUEConfig.Save/Load, and the AppSettings keys SettingsStore
    /// layers on top):
    ///   1. a setting SAVED but never LOADED back - it silently resets to the built-in default
    ///      every launch (see CUEConfigSkip's "scripts" entry below: this is a real, live example,
    ///      not hypothetical);
    ///   2. a setting LOADED but never SAVED - a user's change never actually persists;
    ///   3. a view-model setter that accepts a value its own loader then range-clamps away, so the
    ///      value silently reverts with no message (maxAlbumArtSize: the setter takes 12000,
    ///      CUEConfig.Load only accepts 100..10000).
    /// A reflection round trip over every simple field/property catches classes 1 and 2 as a value
    /// that changed on only one side of Save/Load; NumericSetters_ClampToTheSameRangeTheLoaderEnforces
    /// catches class 3 directly.
    /// </summary>
    [TestClass]
    public class SettingsRoundTripTests
    {
        // ---- shared plumbing ---------------------------------------------------------------

        // Thin reflection handle over either a public field or a public-setter property, so the
        // same sweep code can walk both CUEConfig (public fields) and AppSettings (properties).
        private sealed class Member
        {
            public readonly string Name;
            public readonly Type Type;
            private readonly Func<object, object> _get;
            private readonly Action<object, object> _set;

            public Member(string name, Type type, Func<object, object> get, Action<object, object> set)
            {
                Name = name; Type = type; _get = get; _set = set;
            }

            public object GetValue(object target) => _get(target);
            public void SetValue(object target, object value) => _set(target, value);
        }

        // The brief's scan targets bool/int/string/enum. CUEConfig also range-checks four `uint`
        // confidence/percent fields the exact same way as int ones (LoadUInt32 with a min/max) -
        // skipping them purely because of the unsigned keyword would leave the biggest chunk of the
        // loader's own range-checking logic completely untested, so this sweep widens to include uint.
        private static bool IsSimpleType(Type t) =>
            t == typeof(bool) || t == typeof(int) || t == typeof(uint) || t == typeof(string) || t.IsEnum;

        private static List<Member> GetSimpleMembers(Type t, ISet<string> skip)
        {
            var result = new List<Member>();
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (skip.Contains(f.Name) || !IsSimpleType(f.FieldType)) continue;
                result.Add(new Member(f.Name, f.FieldType, o => f.GetValue(o), (o, v) => f.SetValue(o, v)));
            }
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (skip.Contains(p.Name)) continue;
                if (p.GetIndexParameters().Length > 0) continue;
                if (p.GetSetMethod() == null) continue; // no PUBLIC setter: computed/read-only, not part of this sweep
                if (!IsSimpleType(p.PropertyType)) continue;
                result.Add(new Member(p.Name, p.PropertyType, o => p.GetValue(o), (o, v) => p.SetValue(o, v)));
            }
            return result;
        }

        // Builds a non-default sentinel for one member. `numericSentinels` supplies the value for
        // int/uint members (the loader's own range dictates what is even legal), keyed by member
        // name; bool/string/enum sentinels are generic. Returns false with a `problem` message
        // instead of guessing when nothing register a numeric field - a wrong guess could
        // manufacture a false failure instead of catching a real one.
        private static bool TryMakeSentinel(Member m, object current,
            IReadOnlyDictionary<string, object> numericSentinels, out object sentinel, out string problem)
        {
            problem = null;
            if (m.Type == typeof(bool)) { sentinel = !(bool)current; return true; }
            if (m.Type == typeof(string)) { sentinel = "rt-sentinel"; return true; }
            if (m.Type.IsEnum)
            {
                object other = Enum.GetValues(m.Type).Cast<object>().FirstOrDefault(v => !v.Equals(current));
                if (other == null)
                {
                    sentinel = null;
                    problem = $"{m.Name}: enum {m.Type.Name} has no second defined value to use as a sentinel";
                    return false;
                }
                sentinel = other;
                return true;
            }
            if (m.Type == typeof(int) || m.Type == typeof(uint))
            {
                if (numericSentinels != null && numericSentinels.TryGetValue(m.Name, out sentinel)) return true;
                sentinel = null;
                problem = $"{m.Name}: no sentinel registered for this {m.Type.Name} field - add one to the " +
                          "sentinel table (check the loader's own range for it first, e.g. CUEConfig.Load)";
                return false;
            }
            sentinel = null;
            problem = $"{m.Name}: unhandled member type {m.Type.Name} - add handling above or add it to the skip list";
            return false;
        }

        private static string Describe(object v) => v == null ? "<null>" : v.ToString();

        private sealed class FakeLog : IDiagnosticLog
        {
            public string LogPath => Path.Combine(Path.GetTempPath(), "settings-roundtrip-fake.log");
            public void Info(string category, string message) { }
            public void Warn(string category, string message) { }
            public void Error(string category, string message, Exception ex = null) { }
            public void Redact(params string[] sensitive) { }
        }

        // CUEConfig.Save/Load only know about the file-backed SettingsWriter/SettingsReader in
        // CUETools.Processor.Settings - there is no in-memory writer/reader anywhere in this
        // codebase (both classes take appName/fileName/appPath and go straight to a StreamWriter /
        // StreamReader). Both constructors resolve their folder via
        // SettingsShared.GetProfileDir(appName, appPath), which uses Path.GetDirectoryName(appPath)
        // as the settings folder as long as no "user_profiles_enabled" marker file sits next to
        // appPath - so passing a path inside a throwaway temp directory gives a fully isolated,
        // disk-backed round trip that never touches a real user profile. Dispose() deletes the
        // whole temp directory.
        private sealed class TempProfile : IDisposable
        {
            private const string AppName = "CUETools2026RoundTripTest";
            private const string FileName = "settings.txt";
            private readonly string _dir;
            private readonly string _appPath;

            public TempProfile()
            {
                _dir = Path.Combine(Path.GetTempPath(), "cuetools-rt-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_dir);
                _appPath = Path.Combine(_dir, "app.exe"); // need not actually exist; only its directory name is used
            }

            public SettingsWriter NewWriter() => new SettingsWriter(AppName, FileName, _appPath);
            public SettingsReader NewReader() => new SettingsReader(AppName, FileName, _appPath);

            public void Dispose()
            {
                try { Directory.Delete(_dir, true); } catch { /* best-effort cleanup */ }
            }
        }

        // ---- test 1: CUEConfig ----------------------------------------------------------------

        // Members that legitimately cannot round-trip through this simple bool/int/uint/string/enum
        // sweep, each with why. Kept as short as possible - everything else that is bool/int/uint/
        // string/enum with a public setter really does round-trip through Save/Load.
        private static readonly HashSet<string> CUEConfigSkip = new HashSet<string>
        {
            // Dictionary<string, CUEToolsScript>: not a simple field type for this sweep, so it
            // would be excluded either way - but it is called out explicitly because of what it
            // hides. CUEConfig.Save serializes every custom script (CustomScript{n}Name /
            // CustomScript{n}Condition{m}), but CUEConfig.Load NEVER reads any "CustomScript*" key
            // back - grep the Load method, it just is not there. A user-defined script silently
            // reverts to the built-in six on every relaunch. That is a real save-without-load bug
            // in the engine itself; it is out of scope for a reflection sweep over simple fields
            // only because "scripts" is a collection, not because the bug is not real.
            "scripts",
            // CUEConfigAdvanced: persisted separately as one JSON blob (the "Advanced" key) with its
            // own round trip already; not a simple type, and its property setter is private besides.
            "advanced",
            // Computed passthroughs onto `advanced` - read-only (no public setter), not persisted as
            // their own settings key.
            "Encoders",
            "Decoders",
            "formats",
        };

        // Sentinel values for the numeric fields CUEConfig.Load range-checks, taken straight from
        // the clamps in CUEConfig.Load so the test cannot manufacture a false failure by picking an
        // out-of-range value.
        private static readonly Dictionary<string, object> CUEConfigNumericSentinels = new Dictionary<string, object>
        {
            { "fixOffsetMinimumConfidence", (uint)777 },     // LoadUInt32("ArFixWhenConfidence", 1, 1000); default 2
            { "fixOffsetMinimumTracksPercent", (uint)77 },   // LoadUInt32("ArFixWhenPercent", 1, 100); default 51
            { "encodeWhenConfidence", (uint)888 },           // LoadUInt32("ArEncodeWhenConfidence", 1, 1000); default 2
            { "encodeWhenPercent", (uint)55 },                // LoadUInt32("ArEncodeWhenPercent", 1, 100); default 100
            { "maxAlbumArtSize", 500 },                       // LoadInt32("MaxAlbumArtSize", 100, 10000); default 300
        };

        [TestMethod]
        public void CUEConfig_RoundTripsEverySimpleField()
        {
            var members = GetSimpleMembers(typeof(CUEConfig), CUEConfigSkip);
            Assert.IsTrue(members.Count > 0, "reflection found no round-trippable members - the selector is broken");

            var source = new CUEConfig();
            var problems = new List<string>();
            var expected = new Dictionary<string, object>();

            foreach (var m in members)
            {
                if (!TryMakeSentinel(m, m.GetValue(source), CUEConfigNumericSentinels, out object sentinel, out string problem))
                {
                    problems.Add(problem);
                    continue;
                }
                m.SetValue(source, sentinel);
                expected[m.Name] = sentinel;
            }
            Assert.IsTrue(problems.Count == 0, "test setup problem(s) - fix the sentinel table:\n" + string.Join("\n", problems));

            var mismatches = new List<string>();
            using (var profile = new TempProfile())
            {
                var writer = profile.NewWriter();
                source.Save(writer);
                writer.Close();

                var loaded = new CUEConfig();
                loaded.Load(profile.NewReader());

                foreach (var m in members)
                {
                    object exp = expected[m.Name];
                    object act = m.GetValue(loaded);
                    if (!Equals(exp, act))
                        mismatches.Add($"{m.Name}: saved {Describe(exp)}, loaded back {Describe(act)}");
                }
            }

            Assert.IsTrue(mismatches.Count == 0,
                $"{mismatches.Count} of {members.Count} field(s) did not round-trip:\n" + string.Join("\n", mismatches));
        }

        // ---- test 2: AppSettings via SettingsStore --------------------------------------------

        // Every settable bool/int/string property on AppSettings really is written and read by
        // SettingsStore.Save/Load (see the "Wpf*" keys in both methods) - no skip list needed today.
        private static readonly HashSet<string> AppSettingsSkip = new HashSet<string>();

        // AppSettings has exactly one numeric property; SettingsStore.Load clamps it itself
        // (Math.Max(0, Math.Min(2, ...))) independently of CUEConfig's own range checks.
        private static readonly Dictionary<string, object> AppSettingsNumericSentinels = new Dictionary<string, object>
        {
            { "CorrectionQuality", 2 }, // SettingsStore.Load: Math.Max(0, Math.Min(2, ...)); ctor default 1
        };

        [TestMethod]
        public void AppSettings_RoundTripsEveryProperty()
        {
            var members = GetSimpleMembers(typeof(AppSettings), AppSettingsSkip);
            Assert.IsTrue(members.Count > 0, "reflection found no round-trippable members - the selector is broken");

            // Run against an isolated directory, never the user's real profile: SettingsStore takes an
            // appPath test seam for exactly this. A test that mutates the real settings.txt would risk
            // the user's configuration if it ever died mid-run.
            // appPath follows the engine's portable-mode convention: it is a FILE path, and the profile
            // lands in <its directory>\CUETools2026. A unique directory therefore isolates this run
            // completely - passing a directory instead would resolve to the PARENT and be shared.
            string tempAppData = Path.Combine(Path.GetTempPath(), "cuetools-rt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempAppData);
            string fakeExe = Path.Combine(tempAppData, "CUETools.Wpf.exe");

            try
            {
                var store = new SettingsStore(new FakeLog(), fakeExe);
                string realPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CUETools2026", "settings.txt");
                Assert.AreNotEqual(realPath, store.SettingsFilePath,
                    "the test must not read or write the user's real settings file");
                var sourceApp = new AppSettings();
                var problems = new List<string>();
                var expected = new Dictionary<string, object>();

                foreach (var m in members)
                {
                    if (!TryMakeSentinel(m, m.GetValue(sourceApp), AppSettingsNumericSentinels, out object sentinel, out string problem))
                    {
                        problems.Add(problem);
                        continue;
                    }
                    m.SetValue(sourceApp, sentinel);
                    expected[m.Name] = sentinel;
                }
                Assert.IsTrue(problems.Count == 0, "test setup problem(s) - fix the sentinel table:\n" + string.Join("\n", problems));

                // SettingsStore.Save/Load also touch a CUEConfig in the same file; give it real
                // (default) config instances on both sides so only the AppSettings half is under test.
                store.Save(new CUEConfig(), sourceApp);

                var loadedApp = new AppSettings();
                store.Load(new CUEConfig(), loadedApp);

                var mismatches = new List<string>();
                foreach (var m in members)
                {
                    object exp = expected[m.Name];
                    object act = m.GetValue(loadedApp);
                    if (!Equals(exp, act))
                        mismatches.Add($"{m.Name}: saved {Describe(exp)}, loaded back {Describe(act)}");
                }

                Assert.IsTrue(mismatches.Count == 0,
                    $"{mismatches.Count} of {members.Count} propert(y/ies) did not round-trip:\n" + string.Join("\n", mismatches));
            }
            finally
            {
                try { Directory.Delete(tempAppData, true); } catch { }
            }
        }

        // ---- test 3: numeric setters vs. the loader's own clamp -------------------------------

        private sealed class NumericCase
        {
            public string Name;
            public int LoaderMin;
            public int LoaderMax;
            public int BelowRange;
            public int AboveRange;
            public Func<SettingsViewModel, int> Get;
            public Action<SettingsViewModel, int> Set;
        }

        // Every numeric setting SettingsViewModel exposes that CUEConfig.Load range-checks on the
        // way back in. SettingsViewModel wraps a representative slice of CUEConfig (see its own
        // top-of-file comment) but MaxAlbumArtSize is presently the ONLY int property it exposes -
        // everything else on that view model is bool or string. Add a case here if that changes.
        private static readonly NumericCase[] Cases =
        {
            new NumericCase
            {
                Name = "MaxAlbumArtSize",
                LoaderMin = 100, LoaderMax = 10000, // CUEConfig.Load: LoadInt32("MaxAlbumArtSize", 100, 10000)
                BelowRange = 50, AboveRange = 12000,
                Get = vm => vm.MaxAlbumArtSize,
                Set = (vm, v) => vm.MaxAlbumArtSize = v,
            },
        };

        // Settings audit finding (see class doc): SettingsViewModel.MaxAlbumArtSize's setter writes
        // straight to CUEConfig.maxAlbumArtSize with no range check, so a value the loader would
        // reject (e.g. 12000) "sticks" for the rest of the running session but silently reverts on
        // the next launch, with no message to the user. Keep this entry ONLY until the setter
        // clamps; delete it the moment it does - the assertion below then requires it to clamp like
        // everything else, so a stale entry cannot rot silently.
        // Empty, and it should stay that way: MaxAlbumArtSize's setter now clamps to the loader's own
        // 100..10000, so the value the page shows after a restart is the value it accepted. An entry
        // here means a setter that takes a value its loader will silently reject.
        private static readonly HashSet<string> KnownUnclamped = new HashSet<string>();

        [TestMethod]
        public void NumericSetters_ClampToTheSameRangeTheLoaderEnforces()
        {
            var config = new CUEConfig();
            var app = new AppSettings();
            var vm = new SettingsViewModel(config, app, new FakeLog(), new EncoderCatalog(new FakeLog(), app));

            var mismatches = new List<string>();
            foreach (var c in Cases)
            {
                bool knownUnclamped = KnownUnclamped.Contains(c.Name);

                c.Set(vm, c.BelowRange);
                int afterBelow = c.Get(vm);
                c.Set(vm, c.AboveRange);
                int afterAbove = c.Get(vm);

                bool belowInRange = afterBelow >= c.LoaderMin && afterBelow <= c.LoaderMax;
                bool aboveInRange = afterAbove >= c.LoaderMin && afterAbove <= c.LoaderMax;

                if (knownUnclamped)
                {
                    // The list must drain: if this now clamps, the entry is stale and must be
                    // deleted from KnownUnclamped (which then makes the `else` branch below cover
                    // it and require it to clamp like everything else).
                    if (belowInRange && aboveInRange)
                        mismatches.Add($"{c.Name}: listed in KnownUnclamped but now clamps to " +
                            $"[{c.LoaderMin}..{c.LoaderMax}] - delete this entry, the setter is fixed");
                }
                else
                {
                    if (!belowInRange)
                        mismatches.Add($"{c.Name}: setting {c.BelowRange} (below the loader's range) left the " +
                            $"view model holding {afterBelow}, outside [{c.LoaderMin}..{c.LoaderMax}]");
                    if (!aboveInRange)
                        mismatches.Add($"{c.Name}: setting {c.AboveRange} (above the loader's range) left the " +
                            $"view model holding {afterAbove}, outside [{c.LoaderMin}..{c.LoaderMax}]");
                }
            }

            Assert.IsTrue(mismatches.Count == 0, string.Join("\n", mismatches));
        }
    }
}
