using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CUETools.Wpf.Accuracy;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class DriveRecoveryLadderTests
    {
        /// <summary>Fake probe: hands out scripted verdicts in order and counts
        /// calls, so the ladder's own logic is what is under test.</summary>
        private sealed class ScriptedProbe : IDriveRecoveryProbe
        {
            private readonly Queue<DriveRecoveryProbeReport> _reports = new();

            public int SnapshotCalls { get; private set; }
            public int VerifyCalls { get; private set; }
            public bool CanVerify { get; set; } = true;
            public bool SnapshotReturnsNull { get; set; }

            public ScriptedProbe Then(
                DriveRecoveryProbeResult result,
                char resolved = 'B',
                string detail = "")
            {
                _reports.Enqueue(new DriveRecoveryProbeReport
                {
                    Result = result,
                    ResolvedDrive = resolved,
                    Detail = detail,
                });
                return this;
            }

            public DriveRecoveryFingerprint? Snapshot(char drive)
            {
                SnapshotCalls++;
                return SnapshotReturnsNull
                    ? null
                    : new DriveRecoveryFingerprint
                    {
                        Letter = drive,
                        SrNode = "sr1",
                        Vendor = "TESTVEN",
                        Model = "TESTMODEL",
                    };
            }

            public Task<DriveRecoveryProbeReport> VerifyRungAsync(
                DriveRecoveryFingerprint fingerprint,
                TimeSpan timeout,
                CancellationToken ct = default)
            {
                VerifyCalls++;
                return Task.FromResult(_reports.Count > 0
                    ? _reports.Dequeue()
                    : new DriveRecoveryProbeReport
                    {
                        Result = DriveRecoveryProbeResult.StillUnresponsive,
                    });
            }
        }

        private static string TempStorePath() =>
            Path.Combine(
                Path.GetTempPath(),
                "rl-" + Guid.NewGuid().ToString("N") + ".json");

        private static DriveRecoveryLadder NewLadder(
            ScriptedProbe probe,
            DriveRecoveryIncidentStore? store = null,
            string signature = "TESTVEN TESTMODEL") =>
            new DriveRecoveryLadder(
                signature,
                'B',
                "payload-storm",
                "storm-batches=2; storm-pinpoints=17",
                probe,
                store);

        private static Task<DriveRecoveryLadderState> Verify(DriveRecoveryLadder ladder) =>
            ladder.VerifyCurrentRungAsync(TimeSpan.FromMilliseconds(1));

        [TestMethod]
        public async Task RungAdvancesInOrderOnAStillWedgedDrive()
        {
            var probe = new ScriptedProbe()
                .Then(DriveRecoveryProbeResult.StillUnresponsive);
            var ladder = NewLadder(probe);

            Assert.AreEqual(RecoveryLadderPolicy.CableReplugRung, ladder.CurrentRung);
            DriveRecoveryLadderState state = await Verify(ladder);

            Assert.AreEqual(DriveRecoveryLadderState.AwaitingUser, state);
            Assert.AreEqual(RecoveryLadderPolicy.PowerCycleRung, ladder.CurrentRung);
            CollectionAssert.AreEqual(
                new[] { RecoveryLadderPolicy.CableReplugRung },
                new List<string>(ladder.RungsAttempted));
        }

        [TestMethod]
        public async Task CureStopsTheLadderAndNamesTheCuringRung()
        {
            var probe = new ScriptedProbe()
                .Then(DriveRecoveryProbeResult.StillUnresponsive)
                .Then(DriveRecoveryProbeResult.Responsive, resolved: 'C');
            var ladder = NewLadder(probe);

            await Verify(ladder);
            DriveRecoveryLadderState state = await Verify(ladder);

            Assert.AreEqual(DriveRecoveryLadderState.Cured, state);
            Assert.AreEqual(RecoveryLadderPolicy.PowerCycleRung, ladder.CuringRung);
            // The drive came back at a different node, so the letter moved.
            Assert.AreEqual('C', ladder.ResolvedDrive);
            Assert.IsTrue(ladder.IsTerminal);
            // A terminal ladder never probes again.
            await Verify(ladder);
            Assert.AreEqual(2, probe.VerifyCalls);
        }

        [TestMethod]
        public async Task AnAnsweringDriveWithNoDiscCountsAsCured()
        {
            // The ladder is about the drive, not the media.
            var probe = new ScriptedProbe().Then(DriveRecoveryProbeResult.NoDisc);
            var ladder = NewLadder(probe);

            Assert.AreEqual(DriveRecoveryLadderState.Cured, await Verify(ladder));
            Assert.AreEqual(RecoveryLadderPolicy.CableReplugRung, ladder.CuringRung);
        }

        [TestMethod]
        public async Task OneIncidentIsRecordedWithTheAttemptOrderAndCure()
        {
            string path = TempStorePath();
            var probe = new ScriptedProbe()
                .Then(DriveRecoveryProbeResult.StillUnresponsive)
                .Then(DriveRecoveryProbeResult.Responsive);
            var ladder = NewLadder(probe, new DriveRecoveryIncidentStore(path));

            await Verify(ladder);
            await Verify(ladder);

            Assert.IsTrue(ladder.IncidentRecorded);
            var reread = new DriveRecoveryIncidentStore(path);
            var history = reread.GetHistory("TESTVEN TESTMODEL");
            Assert.AreEqual(1, history.Count);
            Assert.AreEqual(RecoveryLadderPolicy.PowerCycleRung, history[0].CuringRung);
            Assert.AreEqual("payload-storm", history[0].Trigger);
            CollectionAssert.AreEqual(
                new[]
                {
                    RecoveryLadderPolicy.CableReplugRung,
                    RecoveryLadderPolicy.PowerCycleRung,
                },
                new List<string>(history[0].RungsAttempted));
            File.Delete(path);
        }

        [TestMethod]
        public async Task AnExhaustedLadderIsUncuredAndRecordsAnEmptyCure()
        {
            string path = TempStorePath();
            var probe = new ScriptedProbe()
                .Then(DriveRecoveryProbeResult.StillUnresponsive)
                .Then(DriveRecoveryProbeResult.StillUnresponsive);
            var ladder = NewLadder(probe, new DriveRecoveryIncidentStore(path));

            await Verify(ladder);
            Assert.AreEqual(DriveRecoveryLadderState.Uncured, await Verify(ladder));

            var history = new DriveRecoveryIncidentStore(path)
                .GetHistory("TESTVEN TESTMODEL");
            Assert.AreEqual(1, history.Count);
            // "" is the documented uncured sentinel ProvenCure reads.
            Assert.AreEqual("", history[0].CuringRung);
            Assert.IsNull(RecoveryLadderPolicy.ProvenCure(history));
            File.Delete(path);
        }

        [TestMethod]
        public async Task UnverifiedIsNeverACure()
        {
            // An unimplemented platform reports Unverified. Telling a user their
            // still-wedged drive is fixed would be the worst possible outcome.
            var probe = new ScriptedProbe()
                .Then(DriveRecoveryProbeResult.Unverified)
                .Then(DriveRecoveryProbeResult.Unverified);
            var ladder = NewLadder(probe);

            await Verify(ladder);
            Assert.AreEqual(DriveRecoveryLadderState.Uncured, await Verify(ladder));
            Assert.AreEqual("", ladder.CuringRung);
        }

        [TestMethod]
        public async Task PermissionDeniedNeitherAdvancesNorRecords()
        {
            string path = TempStorePath();
            var probe = new ScriptedProbe()
                .Then(DriveRecoveryProbeResult.PermissionDenied);
            var ladder = NewLadder(probe, new DriveRecoveryIncidentStore(path));

            Assert.AreEqual(DriveRecoveryLadderState.PermissionsBlocked, await Verify(ladder));
            // Recording an uncured incident here would break this drive's
            // proven-cure streak over a permissions problem.
            Assert.IsFalse(ladder.IncidentRecorded);
            Assert.IsFalse(File.Exists(path));
        }

        [TestMethod]
        public async Task AProvenCureLeadsAndTheFullLadderStaysReachable()
        {
            string path = TempStorePath();
            var seed = new DriveRecoveryIncidentStore(path);
            for (int i = 0; i < 2; i++)
                seed.Append("TESTVEN TESTMODEL", new DriveRecoveryIncident
                {
                    TimestampUtc = "2026-08-15T04:00:00Z",
                    Trigger = "payload-storm",
                    RungsAttempted = new List<string>
                    {
                        RecoveryLadderPolicy.CableReplugRung,
                        RecoveryLadderPolicy.PowerCycleRung,
                    },
                    CuringRung = RecoveryLadderPolicy.PowerCycleRung,
                });

            var probe = new ScriptedProbe().Then(DriveRecoveryProbeResult.Responsive);
            var ladder = NewLadder(probe, new DriveRecoveryIncidentStore(path));

            Assert.AreEqual(RecoveryLadderPolicy.PowerCycleRung, ladder.ProvenCure);
            Assert.AreEqual(RecoveryLadderPolicy.PowerCycleRung, ladder.CurrentRung);
            Assert.AreEqual(2, ladder.RungOrder.Count);
            // D-061: leading with a proven cure never removes a rung.
            Assert.IsTrue(ladder.SkipToRung(RecoveryLadderPolicy.CableReplugRung));
            Assert.AreEqual(RecoveryLadderPolicy.CableReplugRung, ladder.CurrentRung);
            Assert.IsFalse(ladder.SkipToRung("no-such-rung"));

            Assert.AreEqual(DriveRecoveryLadderState.Cured, await Verify(ladder));
            Assert.AreEqual(RecoveryLadderPolicy.CableReplugRung, ladder.CuringRung);
            File.Delete(path);
        }

        [TestMethod]
        public async Task ACorruptStoreDoesNotStrandTheLadderOrRepairTheFile()
        {
            string path = TempStorePath();
            File.WriteAllText(path, "{truncated");
            byte[] before = File.ReadAllBytes(path);

            var probe = new ScriptedProbe().Then(DriveRecoveryProbeResult.Responsive);
            var ladder = NewLadder(probe, new DriveRecoveryIncidentStore(path));

            Assert.IsTrue(ladder.HistoryUnreadable);
            Assert.AreEqual(RecoveryLadderPolicy.CableReplugRung, ladder.CurrentRung);
            Assert.AreEqual(DriveRecoveryLadderState.Cured, await Verify(ladder));
            // The cure verdict stands even though the memory could not be written.
            Assert.IsFalse(ladder.IncidentRecorded);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            File.Delete(path);
        }

        [TestMethod]
        public async Task AnEmptySignatureRunsTheLadderButPersistsNothing()
        {
            string path = TempStorePath();
            var probe = new ScriptedProbe().Then(DriveRecoveryProbeResult.Responsive);
            var ladder = NewLadder(
                probe,
                new DriveRecoveryIncidentStore(path),
                signature: "   ");

            Assert.AreEqual(DriveRecoveryLadderState.Cured, await Verify(ladder));
            Assert.IsFalse(ladder.IncidentRecorded);
            Assert.IsFalse(File.Exists(path));
        }

        [TestMethod]
        public async Task NoIdentityMeansUncuredRatherThanProbingAGuess()
        {
            var probe = new ScriptedProbe { SnapshotReturnsNull = true };
            var ladder = NewLadder(probe);

            Assert.IsFalse(ladder.Begin());
            Assert.AreEqual(DriveRecoveryLadderState.Uncured, await Verify(ladder));
            Assert.AreEqual(0, probe.VerifyCalls);
        }

        [TestMethod]
        public void AbandonIsTerminalAndRecordedOnce()
        {
            string path = TempStorePath();
            var ladder = NewLadder(
                new ScriptedProbe(),
                new DriveRecoveryIncidentStore(path));

            ladder.Abandon();
            ladder.Abandon();

            Assert.AreEqual(DriveRecoveryLadderState.Abandoned, ladder.State);
            Assert.AreEqual(
                1,
                new DriveRecoveryIncidentStore(path)
                    .GetHistory("TESTVEN TESTMODEL").Count);
            File.Delete(path);
        }

        [TestMethod]
        public void TheLadderCannotSeeTheFailedTransaction()
        {
            // D-062 is structural: a ladder that cannot reach the old operation
            // cannot resume it. Retry is always a fresh calibrated run.
            foreach (var field in typeof(DriveRecoveryLadder).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public))
            {
                string name = field.FieldType.Name;
                Assert.AreNotEqual("VerifyResult", name);
                Assert.AreNotEqual("TestCopyRunResult", name);
                Assert.AreNotEqual("CUESheet", name);
                Assert.AreNotEqual("TestCopyStagingWorkspace", name);
            }
        }

        [TestMethod]
        public void TheUnsupportedProbeNeverClaimsACure()
        {
            var probe = new UnsupportedDriveRecoveryProbe();
            Assert.IsFalse(probe.CanVerify);
            Assert.IsNull(probe.Snapshot('B'));
            DriveRecoveryProbeReport report = probe
                .VerifyRungAsync(new DriveRecoveryFingerprint(), TimeSpan.Zero)
                .GetAwaiter()
                .GetResult();
            Assert.AreEqual(DriveRecoveryProbeResult.Unverified, report.Result);
        }
    }
}
