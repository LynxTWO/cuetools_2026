using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CUETools.Wpf.Services;
using CUETools.Wpf.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class VerifyAlbumWorkspaceTests
{
    [TestMethod]
    public async Task MultiDiscVerificationKeepsOrderAndCompletedFailures()
    {
        var first = Source("disc1.cue", 1, 2);
        var second = Source("disc2.cue", 2, 2);
        var service = new FakeVerifyService(path => path == first.Path
            ? Verified("Album", "Artist", 1, 2)
            : new VerifyFilesResult { Error = "bad manifest", Source = path });
        var viewModel = Create(service, first, second);

        await viewModel.VerifyAllAsync();

        CollectionAssert.AreEqual(
            new[] { first.Path, second.Path },
            service.VerifiedPaths.ToArray());
        Assert.IsTrue(viewModel.HasResult);
        Assert.IsTrue(viewModel.Discs[0].DatabaseConfirmed);
        Assert.IsTrue(viewModel.Discs[1].Failed);
        StringAssert.Contains(viewModel.OverviewHeadline, "could not be verified");
        StringAssert.Contains(viewModel.StatusText, "1 failed disc");
    }

    [TestMethod]
    public async Task AlbumOverviewCombinesMetadataDurationAndTrackEvidence()
    {
        var source = Source("album.cue", 1, 1);
        var result = new VerifyFilesResult
        {
            Ok = true,
            Album = "Guitar Romance",
            Artist = "Jack Jezzro",
            Year = "1996",
            Label = "Green Hill",
            CatalogNumber = "GHD5021",
            Barcode = "0123456789012",
            Country = "US",
            Genre = "Jazz",
            TocId = "test-toc-id",
            DurationSeconds = 3_661,
            TrackCount = 2,
            Accurate = true,
            ArConfidence = 5,
            ArTotal = 8,
            CtdbConfidence = 4,
            CtdbTotal = 9,
            Status = "verified",
            Tracks = new[]
            {
                new VerifyTrackResult
                {
                    Number = 1,
                    Title = "First",
                    Crc32 = "1234ABCD",
                    ArConfidence = 4,
                    ArTotal = 7
                },
                new VerifyTrackResult
                {
                    Number = 2,
                    Title = "Second",
                    Crc32 = "5678EF90",
                    CtdbConfidence = 9
                }
            }
        };
        var viewModel = Create(new FakeVerifyService(_ => result), source);

        await viewModel.VerifyAllAsync();

        VerifyDiscViewModel disc = viewModel.Discs.Single();
        Assert.AreEqual("Jack Jezzro - Guitar Romance", viewModel.AlbumHeading);
        Assert.AreEqual("1:01:01", viewModel.DurationText);
        Assert.AreEqual("2 tracks", viewModel.TrackCountText);
        Assert.AreEqual("Green Hill | GHD5021", disc.LabelText);
        Assert.AreEqual("1996 | US", disc.ReleaseText);
        Assert.AreEqual("test-toc-id", disc.TocText);
        Assert.AreEqual(2, disc.Tracks.Count);
        Assert.AreEqual("1234ABCD", disc.Tracks[0].Crc32);
        Assert.IsTrue(disc.Tracks[0].Confirmed);
        Assert.IsTrue(disc.Tracks[1].Confirmed);
    }

    [TestMethod]
    public async Task StopWaitsForCurrentDiscAndKeepsItsEvidence()
    {
        var first = Source("disc1.cue", 1, 2);
        var second = Source("disc2.cue", 2, 2);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var service = new FakeVerifyService(path =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return Verified("Album", "Artist", 1, 2);
        });
        var viewModel = Create(service, first, second);

        Task running = viewModel.VerifyAllAsync();
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)), "First disc did not start.");
        viewModel.StopCommand.Execute(null);
        release.Set();
        await running;

        Assert.AreEqual(1, service.VerifiedPaths.Count);
        Assert.IsTrue(viewModel.Discs[0].HasResult);
        Assert.IsFalse(viewModel.Discs[1].HasResult);
        StringAssert.Contains(viewModel.StatusText, "Stopped after 1 of 2 discs");
    }

    [TestMethod]
    public async Task RepairIsScopedToSelectedDiscAndSurfacesPublishedCopy()
    {
        var first = Source("disc1.cue", 1, 2);
        var second = Source("disc2.cue", 2, 2);
        var service = new FakeVerifyService(_ => new VerifyFilesResult
        {
            Ok = true,
            Album = "Set",
            HasErrors = true,
            CanRecover = true,
            RepairSamples = 86,
            RepairSectors = 6,
            RepairWorstStripeErrors = 2,
            RepairStripeCapacity = 4,
            Status = "repairable"
        })
        {
            RepairResult = new VerifyFilesResult
            {
                Ok = true,
                Album = "Set",
                Accurate = true,
                ArConfidence = 3,
                RepairApplied = true,
                OutputPath = @"C:\archive\Set - repaired",
                Status = "repaired copy verified"
            }
        };
        var viewModel = Create(service, first, second);
        await viewModel.VerifyAllAsync();

        await viewModel.RepairDiscAsync(viewModel.Discs[1], confirm: false);

        CollectionAssert.AreEqual(new[] { second.Path }, service.RepairedPaths.ToArray());
        Assert.IsFalse(viewModel.Discs[0].RepairApplied);
        Assert.IsTrue(viewModel.Discs[1].RepairApplied);
        Assert.AreEqual(@"C:\archive\Set - repaired", viewModel.Discs[1].RepairOutput);
    }

    [TestMethod]
    public async Task RepairFailureKeepsCompletedVerificationAndRetryCapability()
    {
        var source = Source("disc1.cue", 1, 1);
        var service = new FakeVerifyService(_ => new VerifyFilesResult
        {
            Ok = true,
            Album = "Set",
            HasErrors = true,
            CanRecover = true,
            RepairSamples = 86,
            RepairSectors = 6,
            Status = "repairable"
        })
        {
            RepairResult = new VerifyFilesResult { Error = "independent verification failed" }
        };
        var viewModel = Create(service, source);
        await viewModel.VerifyAllAsync();
        VerifyFilesResult verified = viewModel.Discs[0].Result;

        await viewModel.RepairDiscAsync(viewModel.Discs[0], confirm: false);

        Assert.AreSame(verified, viewModel.Discs[0].Result);
        Assert.IsTrue(viewModel.Discs[0].HasResult);
        Assert.IsTrue(viewModel.Discs[0].CanRepair);
        Assert.IsFalse(viewModel.Discs[0].RepairApplied);
        StringAssert.Contains(viewModel.Discs[0].StatusText, "completed verification evidence was kept");
    }

    private static VerifyViewModel Create(
        FakeVerifyService service,
        params VerificationDiscSource[] sources)
    {
        var viewModel = new VerifyViewModel(
            service,
            new ReportStore(),
            new StaticDiscovery(sources));
        Assert.IsTrue(viewModel.LoadSources(new[] { "selection" }));
        return viewModel;
    }

    private static VerificationDiscSource Source(
        string path,
        int disc,
        int total) => new()
    {
        Path = path,
        RelativePath = path,
        DisplayName = $"Disc {disc}",
        Kind = VerificationSourceKind.CueSheet,
        DiscNumber = disc,
        TotalDiscs = total
    };

    private static VerifyFilesResult Verified(
        string album,
        string artist,
        int disc,
        int total) => new()
    {
        Ok = true,
        Album = album,
        Artist = artist,
        DiscNumber = disc.ToString(),
        TotalDiscs = total.ToString(),
        Accurate = true,
        ArConfidence = 5,
        ArTotal = 8,
        CtdbConfidence = 4,
        CtdbTotal = 9,
        TrackCount = 1,
        DurationSeconds = 60,
        Status = "verified"
    };

    private sealed class StaticDiscovery : IVerificationSourceDiscovery
    {
        private readonly VerificationDiscSource[] _sources;

        public StaticDiscovery(VerificationDiscSource[] sources) => _sources = sources;

        public VerificationSourceDiscoveryResult Discover(IEnumerable<string> paths)
            => new()
            {
                SourceSet = new VerificationSourceSet
                {
                    RootPath = "album-root",
                    DisplayName = "Album folder",
                    Discs = _sources
                }
            };
    }

    private sealed class FakeVerifyService : IVerifyService
    {
        private readonly Func<string, VerifyFilesResult> _verify;

        public FakeVerifyService(Func<string, VerifyFilesResult> verify) => _verify = verify;

        public ConcurrentQueue<string> VerifiedPaths { get; } = new();
        public ConcurrentQueue<string> RepairedPaths { get; } = new();
        public VerifyFilesResult RepairResult { get; init; } = new() { Error = "not configured" };

        public VerifyFilesResult Verify(string path, Action<double, string> onProgress)
        {
            VerifiedPaths.Enqueue(path);
            onProgress(0.5, "checking");
            return _verify(path);
        }

        public VerifyFilesResult Repair(string path, Action<double, string> onProgress)
        {
            RepairedPaths.Enqueue(path);
            onProgress(0.5, "repairing");
            return RepairResult;
        }
    }
}
