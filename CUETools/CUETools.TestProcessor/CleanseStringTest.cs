using Microsoft.VisualStudio.TestTools.UnitTesting;
using CUETools.Processor;

namespace CUETools.TestProcessor
{
    /// <summary>
    /// R11 (SC4): CleanseString must neutralize Windows-hostile path components -
    /// trailing dots/spaces (silently trimmed by the OS) and reserved DOS device names.
    /// </summary>
    [TestClass]
    public class CleanseStringTest
    {
        private static string Cleanse(string s)
        {
            return new CUEConfig().CleanseString(s);
        }

        [TestMethod]
        public void TrailingDotsAndSpacesBecomeUnderscores()
        {
            Assert.AreEqual("Album_", Cleanse("Album."));
            Assert.AreEqual("Album__", Cleanse("Album.."));
            Assert.AreEqual("Title_", Cleanse("Title "));
            Assert.AreEqual("Mix__", Cleanse("Mix. "));
        }

        [TestMethod]
        public void ReservedDeviceNamesArePrefixed()
        {
            Assert.AreEqual("_CON", Cleanse("CON"));
            Assert.AreEqual("_nul", Cleanse("nul"));       // case-insensitive
            Assert.AreEqual("_COM1", Cleanse("COM1"));
            Assert.AreEqual("_LPT9", Cleanse("LPT9"));
            Assert.AreEqual("_NUL.mp3", Cleanse("NUL.mp3")); // base is reserved even with an extension
        }

        [TestMethod]
        public void OrdinaryNamesAreUnchanged()
        {
            Assert.AreEqual("Album", Cleanse("Album"));
            Assert.AreEqual("The Beatles", Cleanse("The Beatles"));
            Assert.AreEqual("CONcert", Cleanse("CONcert")); // contains but is not CON
            Assert.AreEqual("COM10", Cleanse("COM10"));     // COM10 is not a reserved name
        }
    }
}
