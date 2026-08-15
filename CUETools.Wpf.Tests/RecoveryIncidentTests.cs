using System.Collections.Generic;
using System.IO;
using CUETools.Wpf.Accuracy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class RecoveryIncidentTests
    {
        private static DriveRecoveryIncidentStore NewStore() =>
            new DriveRecoveryIncidentStore(Path.Combine(
                Path.GetTempPath(),
                "ri-" + System.Guid.NewGuid().ToString("N") + ".json"));

        private static DriveRecoveryIncident Incident(string cure) =>
            new DriveRecoveryIncident
            {
                TimestampUtc = "2026-08-15T04:00:00Z",
                Trigger = "cache-defeat",
                TriggerContext = "storm-batches=2; storm-pinpoints=17",
                RungsAttempted = new List<string>
                {
                    RecoveryLadderPolicy.CableReplugRung,
                    RecoveryLadderPolicy.PowerCycleRung,
                },
                CuringRung = cure,
            };

        [TestMethod]
        public void StoreRoundTripsAcrossInstances()
        {
            var store = NewStore();
            Assert.AreEqual(0, store.GetHistory("SIG A").Count);
            store.Append("SIG A", Incident(RecoveryLadderPolicy.PowerCycleRung));
            store.Append("SIG A", Incident(""));
            store.Append("SIG B", Incident(RecoveryLadderPolicy.CableReplugRung));

            var history = store.GetHistory("SIG A");
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual(RecoveryLadderPolicy.PowerCycleRung, history[0].CuringRung);
            Assert.AreEqual("", history[1].CuringRung);
            Assert.AreEqual("cache-defeat", history[0].Trigger);
            Assert.AreEqual(2, history[0].RungsAttempted.Count);
            Assert.AreEqual(1, store.GetHistory("SIG B").Count);
            Assert.AreEqual(0, store.GetHistory("SIG C").Count);
        }

        [TestMethod]
        public void LadderDefaultsToCableThenPower()
        {
            var order = RecoveryLadderPolicy.RungOrder(
                new List<DriveRecoveryIncident>());
            Assert.AreEqual(2, order.Count);
            Assert.AreEqual(RecoveryLadderPolicy.CableReplugRung, order[0]);
            Assert.AreEqual(RecoveryLadderPolicy.PowerCycleRung, order[1]);
        }

        [TestMethod]
        public void OneCureDoesNotChangeTheLead()
        {
            var history = new List<DriveRecoveryIncident>
            {
                Incident(RecoveryLadderPolicy.PowerCycleRung),
            };
            Assert.IsNull(RecoveryLadderPolicy.ProvenCure(history));
            Assert.AreEqual(
                RecoveryLadderPolicy.CableReplugRung,
                RecoveryLadderPolicy.RungOrder(history)[0]);
        }

        [TestMethod]
        public void TwoConsecutiveSameRungCuresLead()
        {
            var history = new List<DriveRecoveryIncident>
            {
                Incident(RecoveryLadderPolicy.PowerCycleRung),
                Incident(RecoveryLadderPolicy.PowerCycleRung),
            };
            Assert.AreEqual(
                RecoveryLadderPolicy.PowerCycleRung,
                RecoveryLadderPolicy.ProvenCure(history));
            var order = RecoveryLadderPolicy.RungOrder(history);
            // The proven cure leads and the full ladder stays reachable.
            Assert.AreEqual(RecoveryLadderPolicy.PowerCycleRung, order[0]);
            Assert.AreEqual(RecoveryLadderPolicy.CableReplugRung, order[1]);
            Assert.AreEqual(2, order.Count);
        }

        [TestMethod]
        public void UncuredOrMixedIncidentsBreakTheStreak()
        {
            // Most recent incident uncured: no proven cure, whatever came before.
            var uncuredLast = new List<DriveRecoveryIncident>
            {
                Incident(RecoveryLadderPolicy.PowerCycleRung),
                Incident(RecoveryLadderPolicy.PowerCycleRung),
                Incident(""),
            };
            Assert.IsNull(RecoveryLadderPolicy.ProvenCure(uncuredLast));

            // Alternating cures never reach two consecutive.
            var mixed = new List<DriveRecoveryIncident>
            {
                Incident(RecoveryLadderPolicy.PowerCycleRung),
                Incident(RecoveryLadderPolicy.CableReplugRung),
            };
            Assert.IsNull(RecoveryLadderPolicy.ProvenCure(mixed));

            // An older uncured incident does not block a newer two-cure streak.
            var recovered = new List<DriveRecoveryIncident>
            {
                Incident(""),
                Incident(RecoveryLadderPolicy.CableReplugRung),
                Incident(RecoveryLadderPolicy.CableReplugRung),
            };
            Assert.AreEqual(
                RecoveryLadderPolicy.CableReplugRung,
                RecoveryLadderPolicy.ProvenCure(recovered));
        }
    }
}
