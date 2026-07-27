using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CUETools.Codecs;
using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class RepairPreservationIntegrationTests
{
    private const int OneSecond = 44100;
    private string _root;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "cuetools-repair-preservation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        string tempPrefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullRoot = Path.GetFullPath(_root);
        if (fullRoot.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(fullRoot).StartsWith(
                "cuetools-repair-preservation-",
                StringComparison.Ordinal)
            && Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }
    }

    [TestMethod]
    public void TrackRepairLayout_PreservesBasenamesTagsCustomFieldsAndArtwork()
    {
        string firstName = "01 - First & Original.flac";
        string secondName = "02.Second (Original).flac";
        string first = Path.Combine(_root, firstName);
        string second = Path.Combine(_root, secondName);
        WriteFlac(first, OneSecond, seed: 101);
        WriteFlac(second, OneSecond, seed: 202);
        WriteTags(first, "First Source Title", 1, "custom-first");
        WriteTags(second, "Second Source Title", 2, "custom-second");
        string cuePath = Path.Combine(_root, "source album.cue");
        File.WriteAllText(
            cuePath,
            "PERFORMER \"Preserved Artist\"" + Environment.NewLine +
            "TITLE \"Preserved Album\"" + Environment.NewLine +
            "FILE \"" + firstName + "\" WAVE" + Environment.NewLine +
            "  TRACK 01 AUDIO" + Environment.NewLine +
            "    TITLE \"First Source Title\"" + Environment.NewLine +
            "    PERFORMER \"Preserved Artist\"" + Environment.NewLine +
            "    INDEX 01 00:00:00" + Environment.NewLine +
            "FILE \"" + secondName + "\" WAVE" + Environment.NewLine +
            "  TRACK 02 AUDIO" + Environment.NewLine +
            "    TITLE \"Second Source Title\"" + Environment.NewLine +
            "    PERFORMER \"Preserved Artist\"" + Environment.NewLine +
            "    INDEX 01 00:00:00" + Environment.NewLine);

        string firstHash = HashFile(first);
        string secondHash = HashFile(second);
        string outputRoot = Path.Combine(_root, "stage");
        CUESheet sheet = CreateRepairEncode(
            cuePath,
            outputRoot,
            CUEStyle.GapsAppended);
        try
        {
            CollectionAssert.AreEqual(
                new[] { firstName, secondName },
                sheet.DestPaths.Select(Path.GetFileName).ToArray());

            sheet.Go();

            Assert.IsTrue(sheet.FinalOutputVerifiedAfterMetadata);
            Assert.AreEqual(2, sheet.FinalOutputProofs.Count);
            AssertPreservedTags(
                sheet.DestPaths[0],
                "First Source Title",
                1,
                "custom-first");
            AssertPreservedTags(
                sheet.DestPaths[1],
                "Second Source Title",
                2,
                "custom-second");
            Assert.AreEqual(firstHash, HashFile(first));
            Assert.AreEqual(secondHash, HashFile(second));
        }
        finally
        {
            sheet.Close();
        }
    }

    [TestMethod]
    public void ImageRepairLayout_PreservesImageBasenameTagsAndArtwork()
    {
        string imageName = "Damaged Disc - Original Name.flac";
        string image = Path.Combine(_root, imageName);
        WriteFlac(image, OneSecond * 2, seed: 303);
        WriteTags(image, "Image Source Title", 0, "custom-image");
        string cuePath = Path.Combine(_root, "image source.cue");
        File.WriteAllText(
            cuePath,
            "PERFORMER \"Preserved Artist\"" + Environment.NewLine +
            "TITLE \"Preserved Album\"" + Environment.NewLine +
            "FILE \"" + imageName + "\" WAVE" + Environment.NewLine +
            "  TRACK 01 AUDIO" + Environment.NewLine +
            "    TITLE \"First\"" + Environment.NewLine +
            "    INDEX 01 00:00:00" + Environment.NewLine +
            "  TRACK 02 AUDIO" + Environment.NewLine +
            "    TITLE \"Second\"" + Environment.NewLine +
            "    INDEX 01 00:01:00" + Environment.NewLine);

        string sourceHash = HashFile(image);
        CUESheet sheet = CreateRepairEncode(
            cuePath,
            Path.Combine(_root, "stage"),
            CUEStyle.SingleFile);
        try
        {
            Assert.AreEqual(1, sheet.DestPaths.Length);
            Assert.AreEqual(imageName, Path.GetFileName(sheet.DestPaths[0]));

            sheet.Go();

            Assert.IsTrue(sheet.FinalOutputVerifiedAfterMetadata);
            Assert.AreEqual(1, sheet.FinalOutputProofs.Count);
            AssertPreservedTags(
                sheet.DestPaths[0],
                expectedTitle: null,
                expectedTrack: 0,
                expectedCustom: "custom-image");
            Assert.AreEqual(sourceHash, HashFile(image));
        }
        finally
        {
            sheet.Close();
        }
    }

    private static CUESheet CreateRepairEncode(
        string cuePath,
        string outputRoot,
        CUEStyle style)
    {
        var sourceConfig = new CUEConfig
        {
            autoCorrectFilenames = false,
            detectHDCD = false,
            separateDecodingThread = false,
            createEACLOG = false,
            writeArLogOnConvert = false,
            writeArTagsOnEncode = false,
            embedLog = false,
            extractLog = true,
            keepOriginalFilenames = false,
            writeBasicTagsFromCUEData = false,
            copyBasicTags = false,
            copyUnknownTags = false,
            CopyAlbumArt = false,
            embedAlbumArt = false,
            extractAlbumArt = true
        };
        CUEConfig config = VerifyService.CreateRepairConfig(sourceConfig);
        var encoder = new CUETools.Codecs.Flake.EncoderSettings
        {
            EncoderMode = "0",
            DoVerify = true,
            DoMD5 = true
        };
        var decoder = new CUETools.Codecs.Flake.DecoderSettings();
        config.formats["flac"] = new CUEToolsFormat(
            "flac",
            CUEToolsTagger.TagLibSharp,
            true,
            false,
            true,
            true,
            new AudioEncoderSettingsViewModel(encoder),
            null,
            new AudioDecoderSettingsViewModel(decoder));

        var sheet = new CUESheet(config);
        sheet.Open(cuePath);
        sheet.Action = CUEAction.Encode;
        sheet.OutputStyle = style;
        sheet.VerifyFinalOutputAfterMetadata = true;
        sheet.GenerateFilenames(
            AudioEncoderType.Lossless,
            "flac",
            Path.Combine(outputRoot, "album.cue"));
        return sheet;
    }

    private static void WriteFlac(string path, int sampleCount, int seed)
    {
        var settings = new CUETools.Codecs.Flake.EncoderSettings
        {
            PCM = AudioPCMConfig.RedBook,
            EncoderMode = "0",
            Padding = 2048,
            DoVerify = true,
            DoMD5 = true
        };
        var destination = new CUETools.Codecs.Flake.AudioEncoder(settings, path);
        destination.FinalSampleCount = sampleCount;
        destination.Write(new AudioBuffer(
            AudioPCMConfig.RedBook,
            CreatePcmBytes(sampleCount, seed),
            sampleCount));
        destination.Close();
    }

    private static byte[] CreatePcmBytes(int sampleCount, int seed)
    {
        byte[] bytes =
            new byte[sampleCount * AudioPCMConfig.RedBook.BlockAlign];
        int offset = 0;
        for (int sample = 0; sample < sampleCount; sample++)
        {
            for (int channel = 0; channel < 2; channel++)
            {
                int value = ((sample + seed) * 257 + channel * 8191) & 0xffff;
                short signed = unchecked((short)(value - 32768));
                bytes[offset++] = unchecked((byte)signed);
                bytes[offset++] = unchecked((byte)(signed >> 8));
            }
        }
        return bytes;
    }

    private static void WriteTags(
        string path,
        string title,
        uint track,
        string custom)
    {
        using TagLib.File file = TagLib.File.Create(path);
        file.Tag.Title = title;
        file.Tag.Album = "Preserved Album";
        file.Tag.Performers = new[] { "Preserved Artist" };
        file.Tag.AlbumArtists = new[] { "Preserved Album Artist" };
        file.Tag.Genres = new[] { "Preserved Genre" };
        file.Tag.Comment = "Preserved Comment";
        file.Tag.Year = 2026;
        file.Tag.Track = track;
        file.Tag.TrackCount = track == 0 ? 0u : 2u;
        file.Tag.Disc = 1;
        file.Tag.DiscCount = 1;
        file.Tag.Pictures = new TagLib.IPicture[]
        {
            new TagLib.Picture(new TagLib.ByteVector(
                new byte[] { 0x43, 0x55, 0x45, 0x54, 0x6f, 0x6f, 0x6c, 0x73 }))
            {
                Type = TagLib.PictureType.FrontCover,
                MimeType = "application/octet-stream",
                Description = "Preserved artwork"
            }
        };
        var xiph = (TagLib.Ogg.XiphComment)file.GetTag(
            TagLib.TagTypes.Xiph,
            create: true);
        xiph.SetField("CUETOOLS_PRESERVATION_TEST", new[] { custom });
        file.Save();
    }

    private static void AssertPreservedTags(
        string path,
        string expectedTitle,
        uint expectedTrack,
        string expectedCustom)
    {
        using TagLib.File file = TagLib.File.Create(path);
        if (expectedTitle != null)
            Assert.AreEqual(expectedTitle, file.Tag.Title);
        Assert.AreEqual("Preserved Album", file.Tag.Album);
        CollectionAssert.AreEqual(
            new[] { "Preserved Artist" },
            file.Tag.Performers);
        CollectionAssert.AreEqual(
            new[] { "Preserved Album Artist" },
            file.Tag.AlbumArtists);
        CollectionAssert.AreEqual(
            new[] { "Preserved Genre" },
            file.Tag.Genres);
        Assert.AreEqual("Preserved Comment", file.Tag.Comment);
        Assert.AreEqual(2026u, file.Tag.Year);
        if (expectedTrack != 0)
            Assert.AreEqual(expectedTrack, file.Tag.Track);
        Assert.AreEqual(1u, file.Tag.Disc);
        Assert.AreEqual(1u, file.Tag.DiscCount);
        Assert.AreEqual(1, file.Tag.Pictures.Length);
        CollectionAssert.AreEqual(
            new byte[] { 0x43, 0x55, 0x45, 0x54, 0x6f, 0x6f, 0x6c, 0x73 },
            file.Tag.Pictures[0].Data.Data);

        var xiph = (TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph);
        CollectionAssert.AreEqual(
            new[] { expectedCustom },
            xiph.GetField("CUETOOLS_PRESERVATION_TEST"));
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
