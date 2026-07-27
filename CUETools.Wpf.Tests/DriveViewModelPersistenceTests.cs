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

        private sealed class FakeDriveService : IDriveService
        {
            public char SelectedDrive { get; set; } = 'H';
            public bool RipInProgress => false;
            public IReadOnlyList<char> GetDrives() => new[] { 'H' };
            public DiscInfo ReadDisc(char drive, Action<string> onStatus = null) => null;
            public DriveDetails GetDriveDetails(char drive) => new DriveDetails
            {
                Valid = true,
                Letter = drive,
                Model = "Test optical drive",
                ARName = "TEST DRIVE",
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
