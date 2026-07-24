using System.Collections.Generic;
using System.IO;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class GzJsonTests
    {
        private string Temp() => Path.Combine(Path.GetTempPath(), "gzjson-" + System.Guid.NewGuid().ToString("N") + ".json.gz");

        [TestMethod]
        public void RoundTrips()
        {
            string p = Temp();
            var data = new List<int> { 1, 2, 3 };
            GzJson.Save(p, data);
            var back = GzJson.Load<List<int>>(p);
            CollectionAssert.AreEqual(data, back);
            File.Delete(p);
        }

        [TestMethod]
        public void SavedFileIsGzip()
        {
            string p = Temp();
            GzJson.Save(p, new List<int> { 9 });
            var bytes = File.ReadAllBytes(p);
            Assert.IsTrue(bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b, "must be gzip");
            File.Delete(p);
        }

        [TestMethod]
        public void LoadsExistingPlainJson()
        {
            string p = Path.Combine(Path.GetTempPath(), "plain-" + System.Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(p, "[4,5,6]");   // an old, uncompressed file
            var back = GzJson.Load<List<int>>(p);
            CollectionAssert.AreEqual(new List<int> { 4, 5, 6 }, back);
            File.Delete(p);
        }

        [TestMethod]
        public void MissingReturnsDefault()
        {
            Assert.IsNull(GzJson.Load<List<int>>(Path.Combine(Path.GetTempPath(), "nope-" + System.Guid.NewGuid().ToString("N") + ".json")));
        }
    }
}
