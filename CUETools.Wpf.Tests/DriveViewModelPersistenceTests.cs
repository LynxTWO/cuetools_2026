using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CUETools.Ripper.SCSI;
using CUETools.Wpf.Accuracy;
using CUETools.Wpf.Models;
using CUETools.Wpf.Services;
using CUETools.Wpf.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class DriveViewModelPersistenceTests
    {
        [TestMethod]
        public async Task CorruptCalibrationRemainsVisibleWithoutStrandingDrivePage()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "drive-vm-corrupt-calibration-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "drive-calibration.json");
            byte[] corrupt = System.Text.Encoding.UTF8.GetBytes("{truncated");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, corrupt);

            try
            {
                var calibration = new DriveCalibrationService(
                    new NullLog(),
                    new DriveCalibrationStore(path));
                var viewModel = new DriveViewModel(
                    new FakeDriveService(),
                    calibration,
                    autoDetect: false);

                await viewModel.DetectForTestAsync();

                Assert.IsTrue(viewModel.HasDetails);
                Assert.IsFalse(viewModel.HasCal);
                Assert.IsFalse(viewModel.IsBusyForTest);
                StringAssert.Contains(viewModel.Status, "calibration is unreadable");
                CollectionAssert.AreEqual(corrupt, File.ReadAllBytes(path));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [TestMethod]
        public async Task SelectedDriveChangeCannotLeaveFirstDriveDetailsVisible()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "drive-vm-selection-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "drive-calibration.json");
            Directory.CreateDirectory(directory);

            try
            {
                var store = new DriveCalibrationStore(path);
                store.Save(new DriveCalibration
                {
                    DriveSignature = "DRIVE K",
                    CacheDefeat = "Flush:786432",
                    CacheConfidence = CalConfidence.Confirmed,
                    ReadOffsetKnown = true,
                    ReadOffsetSamples = 6,
                    OverreadLeadIn = true,
                    RipperVersion = "2026.2.0",
                    CalibratedUtc = DateTime.UtcNow,
                });
                var drives = new FakeDriveService();
                var viewModel = new DriveViewModel(
                    drives,
                    new DriveCalibrationService(new NullLog(), store),
                    autoDetect: false);

                await viewModel.DetectForTestAsync();
                Assert.AreEqual("H:", viewModel.DriveLetter);
                Assert.AreEqual("DRIVE H", viewModel.Details.ARName);
                Assert.IsFalse(viewModel.HasCal);

                drives.SelectedDrive = 'K';
                DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                while ((viewModel.IsBusyForTest || viewModel.Details == null) &&
                       DateTime.UtcNow < deadline)
                    await Task.Delay(10);

                Assert.AreEqual("K:", viewModel.DriveLetter);
                Assert.AreEqual("DRIVE K", viewModel.Details.ARName);
                Assert.IsTrue(viewModel.HasCal);
                Assert.AreEqual("Flush:786432", viewModel.Cal.CacheDefeat);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [TestMethod]
        public void DriveSelectorIsLockedForTheFullHardwareOwnershipScope()
        {
            var drives = new FakeDriveService();
            var viewModel = new DriveViewModel(
                drives,
                new DriveCalibrationService(
                    new NullLog(),
                    new DriveCalibrationStore(Path.Combine(
                        Path.GetTempPath(),
                        "unused-drive-calibration-" + Guid.NewGuid().ToString("N")))),
                autoDetect: false);

            drives.SetRipInProgress(true);
            Assert.IsFalse(viewModel.IsDriveSelectionEnabled);

            viewModel.SelectedDrive = 'K';
            Assert.AreEqual('H', viewModel.SelectedDrive);
            Assert.AreEqual('H', drives.SelectedDrive);
            StringAssert.Contains(viewModel.Status, "cannot be changed");

            drives.SetRipInProgress(false);
            Assert.IsTrue(viewModel.IsDriveSelectionEnabled);
        }

        private sealed class FakeDriveService : IDriveService
        {
            private char _selectedDrive = 'H';
            public char SelectedDrive
            {
                get => _selectedDrive;
                set
                {
                    if (_selectedDrive == value) return;
                    _selectedDrive = value;
                    SelectedDriveChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            public event EventHandler SelectedDriveChanged;
            private bool _ripInProgress;
            public bool RipInProgress => _ripInProgress;
            public event EventHandler RipInProgressChanged;
            public void SetRipInProgress(bool value)
            {
                if (_ripInProgress == value) return;
                _ripInProgress = value;
                RipInProgressChanged?.Invoke(this, EventArgs.Empty);
            }
            public IReadOnlyList<char> GetDrives() => new[] { 'H', 'K' };
            public DiscInfo ReadDisc(char drive, Action<string> onStatus = null) => null;
            public DriveDetails GetDriveDetails(char drive) => new DriveDetails
            {
                Valid = true,
                Letter = drive,
                Model = "Test optical drive",
                ARName = "DRIVE " + drive,
            };
            public DriveTrayState GetTrayState(char drive) => DriveTrayState.ClosedWithDisc;
            public void OpenTray(char drive) { }
            public void CloseTray(char drive) { }
        }

        private sealed class NullLog : IDiagnosticLog
        {
            public string LogPath => "";
            public void Info(string category, string message) { }
            public void Warn(string category, string message) { }
            public void Error(string category, string message, Exception ex = null) { }
            public void Redact(params string[] sensitive) { }
        }
    }
}
