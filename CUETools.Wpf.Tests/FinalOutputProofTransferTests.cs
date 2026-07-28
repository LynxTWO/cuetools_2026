using System;
using System.Collections.Generic;
using System.IO;
using CUETools.Codecs;
using CUETools.Processor;
using CUETools.Wpf.Accuracy;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class FinalOutputProofTransferTests
{
    private const int SampleCount = 257;
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "cuetools-proof-transfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [TestMethod]
    public void ProvedTransferCopiesTheFrozenAudioHandleAndSidecars()
    {
        string source = Path.Combine(_root, "source");
        string destination = Path.Combine(_root, "destination");
        LosslessOutputProof proof = CreateProvedWav(
            source,
            Path.Combine("Disc 1", "01.wav"),
            seed: 11);
        File.WriteAllText(Path.Combine(source, "album.cue"), "sidecar");

        RipService.CopyDirectoryRecursiveVerified(
            source,
            destination,
            new[] { proof },
            "wav");

        proof.VerifyFile(destination);
        Assert.AreEqual(
            "sidecar",
            File.ReadAllText(Path.Combine(destination, "album.cue")));
    }

    [TestMethod]
    public void ProvedTransferRejectsChangedSourceBytes()
    {
        string source = Path.Combine(_root, "source");
        string destination = Path.Combine(_root, "destination");
        LosslessOutputProof proof = CreateProvedWav(
            source,
            "01.wav",
            seed: 17);
        File.AppendAllText(
            proof.GetConstrainedPath(source),
            "changed");

        Assert.ThrowsException<InvalidDataException>(() =>
            RipService.CopyDirectoryRecursiveVerified(
                source,
                destination,
                new[] { proof },
                "wav"));
    }

    [TestMethod]
    public void ProvedTransferRejectsMissingDuplicateAndExtraProofPaths()
    {
        string source = Path.Combine(_root, "source");
        LosslessOutputProof proof = CreateProvedWav(
            source,
            "01.wav",
            seed: 23);

        Assert.ThrowsException<InvalidDataException>(() =>
            RipService.CopyDirectoryRecursiveVerified(
                source,
                Path.Combine(_root, "missing"),
                Array.Empty<LosslessOutputProof>(),
                "wav"));
        Assert.ThrowsException<InvalidDataException>(() =>
            RipService.CopyDirectoryRecursiveVerified(
                source,
                Path.Combine(_root, "duplicate"),
                new[] { proof, proof },
                "wav"));

        File.Copy(
            proof.GetConstrainedPath(source),
            Path.Combine(source, "02.wav"));
        Assert.ThrowsException<InvalidDataException>(() =>
            RipService.CopyDirectoryRecursiveVerified(
                source,
                Path.Combine(_root, "extra"),
                new[] { proof },
                "wav"));
    }

    [TestMethod]
    public void ProvedTransferRejectsUnprovedRegisteredAudioFormat()
    {
        string source = Path.Combine(_root, "source");
        LosslessOutputProof proof = CreateProvedWav(
            source,
            "01.wav",
            seed: 27);
        File.Copy(
            proof.GetConstrainedPath(source),
            Path.Combine(source, "unproved.flac"));

        Assert.ThrowsException<InvalidDataException>(() =>
            RipService.CopyDirectoryRecursiveVerified(
                source,
                Path.Combine(_root, "destination"),
                new[] { proof },
                "wav",
                new[] { "wav", "flac" }));
    }

    [TestMethod]
    public void OutputProofSnapshotRejectsDestPathOrderMismatch()
    {
        string source = Path.Combine(_root, "source");
        LosslessOutputProof first = CreateProvedWav(
            source,
            "01.wav",
            seed: 28);
        LosslessOutputProof second = CreateProvedWav(
            source,
            "02.wav",
            seed: 29);
        string[] expected =
        {
            first.GetConstrainedPath(source),
            second.GetConstrainedPath(source),
        };

        Assert.ThrowsException<InvalidDataException>(() =>
            RipService.SnapshotAndValidateOutputProofs(
                expected,
                source,
                new[] { second, first }));
    }

    [TestMethod]
    public void ExistingWriterCannotRaceAProvedTransfer()
    {
        string source = Path.Combine(_root, "source");
        LosslessOutputProof proof = CreateProvedWav(
            source,
            "01.wav",
            seed: 29);
        using var writer = new FileStream(
            proof.GetConstrainedPath(source),
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite);

        Assert.ThrowsException<IOException>(() =>
            RipService.CopyDirectoryRecursiveVerified(
                source,
                Path.Combine(_root, "destination"),
                new[] { proof },
                "wav"));
    }

    [TestMethod]
    public void HeldAcceptAnywayRevalidatesProofBeforePublication()
    {
        string source = Path.Combine(_root, "held-source");
        LosslessOutputProof proof = CreateProvedWav(
            source,
            "01.wav",
            seed: 31);
        File.AppendAllText(
            proof.GetConstrainedPath(source),
            "changed after the Copy read");
        RipService service = CreateRipService();
        var held = new TestCopyRunResult
        {
            Ok = true,
            Outcome = TestCopyOutcome.Held,
            ReadsUsed = 2,
            HeldTracks = new[] { 0 },
            CopyStagingDir = source,
            OutputRelDir = "Accepted Album",
            Format = "wav",
            OutputVerificationKnown = true,
            LosslessOutput = true,
            OutputVerificationPerformed = true,
            OutputVerificationDetail = "test proof",
            OutputProofs = new[] { proof },
        };
        string outputBase = Path.Combine(_root, "library");

        Assert.AreEqual(
            "",
            service.CommitCopyReadAnyway(held, outputBase));
        Assert.IsFalse(
            Directory.Exists(Path.Combine(outputBase, "Accepted Album")));
    }

    [TestMethod]
    public void CleanHeldAcceptAnywayCarriesProofToPublishedRoot()
    {
        string source = Path.Combine(_root, "held-source");
        LosslessOutputProof proof = CreateProvedWav(
            source,
            "01.wav",
            seed: 37);
        RipService service = CreateRipService();
        var held = new TestCopyRunResult
        {
            Ok = true,
            Outcome = TestCopyOutcome.Held,
            ReadsUsed = 2,
            HeldTracks = new[] { 0 },
            CopyStagingDir = source,
            OutputRelDir = Path.Combine("Box Set", "Disc 1"),
            ArtifactStem = "Artist - Box Set (Disc 1)",
            Format = "wav",
            OutputVerificationKnown = true,
            LosslessOutput = true,
            OutputVerificationPerformed = true,
            OutputVerificationDetail = "test proof",
            OutputProofs = new[] { proof },
        };

        string published = service.CommitCopyReadAnyway(
            held,
            Path.Combine(_root, "library"));

        Assert.IsFalse(
            string.IsNullOrEmpty(published),
            ReadDiagnosticLog());
        proof.VerifyFile(published);
        Assert.IsTrue(
            File.Exists(Path.Combine(
                published,
                "Artist - Box Set (Disc 1) - Test & Copy.log")));
    }

    [TestMethod]
    public void CommitWindowMutationFailsAndQuarantinesPublishedAlbum()
    {
        string source = Path.Combine(_root, "held-source");
        LosslessOutputProof proof = CreateProvedWav(
            source,
            "01.wav",
            seed: 41);
        RipService service = CreateRipService();
        service.AfterProofDirectoryMoveForTest = published =>
        {
            string audio = proof.GetConstrainedPath(published);
            File.Move(audio, audio + ".replaced");
            File.WriteAllText(
                audio,
                "changed in the publication window");
        };
        var held = new TestCopyRunResult
        {
            Ok = true,
            Outcome = TestCopyOutcome.Held,
            ReadsUsed = 2,
            HeldTracks = new[] { 0 },
            CopyStagingDir = source,
            OutputRelDir = "Window Race Album",
            ArtifactStem = "Artist - Window Race Album",
            Format = "wav",
            OutputVerificationKnown = true,
            LosslessOutput = true,
            OutputVerificationPerformed = true,
            OutputVerificationDetail = "test proof",
            OutputProofs = new[] { proof },
        };
        string outputBase = Path.Combine(_root, "library");

        Assert.AreEqual(
            "",
            service.CommitCopyReadAnyway(held, outputBase));
        Assert.IsFalse(
            Directory.Exists(
                Path.Combine(outputBase, "Window Race Album")));
        string[] incomplete = Directory.GetDirectories(
            outputBase,
            ".cuetools-incomplete-published-*",
            SearchOption.TopDirectoryOnly);
        Assert.AreEqual(
            1,
            incomplete.Length,
            ReadDiagnosticLog());
        Assert.IsTrue(
            File.Exists(
                Path.Combine(
                    incomplete[0],
                    AlbumOutputTransaction.ProofFailureMarkerName)));
        StringAssert.Contains(
            File.ReadAllText(
                Path.Combine(
                    incomplete[0],
                    "Artist - Window Race Album - Test & Copy.log")),
            "verification invalidated");
    }

    private LosslessOutputProof CreateProvedWav(
        string root,
        string relativePath,
        int seed)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(path) ??
            throw new InvalidOperationException(
                "The fixture output has no parent directory."));

        byte[] pcmBytes = CreatePcmBytes(SampleCount, seed);
        var buffer = new AudioBuffer(
            AudioPCMConfig.RedBook,
            pcmBytes,
            SampleCount);
        byte[] pcmDigest;
        using (var fingerprint = new LosslessPcmFingerprint())
        {
            fingerprint.Append(buffer);
            pcmDigest = fingerprint.Complete();
        }

        var settings =
            new CUETools.Codecs.WAV.EncoderSettings(
                AudioPCMConfig.RedBook);
        var encoder =
            new CUETools.Codecs.WAV.AudioEncoder(
                settings,
                path);
        encoder.FinalSampleCount = SampleCount;
        encoder.Write(buffer);
        encoder.Close();

        return LosslessOutputProof.CreateVerified(
            root,
            path,
            "WAV transfer fixture",
            AudioPCMConfig.RedBook,
            SampleCount,
            pcmDigest,
            (decoderPath, input) =>
                new CUETools.Codecs.WAV.AudioDecoder(
                    new CUETools.Codecs.WAV.DecoderSettings(),
                    decoderPath,
                    input));
    }

    private RipService CreateRipService()
    {
        string state = Path.Combine(_root, "state");
        Directory.CreateDirectory(state);
        var log = new DiagnosticLog(
            Path.Combine(state, "diagnostic.log"));
        var app = new AppSettings();
        var config = new CUEConfig();
        var calibration = new DriveCalibrationStore(
            Path.Combine(state, "drive-calibration.json"));
        return new RipService(
            config,
            log,
            app,
            new EncoderCatalog(
                log,
                app,
                Path.Combine(state, "encoders")),
            calibration,
            new VerifyHistoryStore(
                Path.Combine(state, "verify-history.json.gz")),
            new DriveCalibrationService(log, calibration));
    }

    private string ReadDiagnosticLog()
    {
        string path = Path.Combine(
            _root,
            "state",
            "diagnostic.log");
        return File.Exists(path)
            ? File.ReadAllText(path)
            : "The diagnostic log was not created.";
    }

    private static byte[] CreatePcmBytes(
        int sampleCount,
        int seed)
    {
        byte[] bytes =
            new byte[sampleCount * AudioPCMConfig.RedBook.BlockAlign];
        int offset = 0;
        for (int sample = 0; sample < sampleCount; sample++)
        {
            for (int channel = 0; channel < 2; channel++)
            {
                short value = unchecked((short)(
                    (sample * 109 + channel * 8191 + seed) & 0xffff));
                bytes[offset++] = unchecked((byte)value);
                bytes[offset++] = unchecked((byte)(value >> 8));
            }
        }
        return bytes;
    }
}
