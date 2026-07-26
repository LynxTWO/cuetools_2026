using CUETools.Codecs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Runtime.CompilerServices;

namespace CUETools.TestCodecs
{
    [TestClass]
    public class ExceptionRelayTest
    {
        [TestMethod]
        public void RelayPreservesBackgroundProducerFrame()
        {
            Exception captured;
            try
            {
                ThrowFromProducer();
                throw new AssertFailedException("The producer did not throw.");
            }
            catch (InvalidOperationException ex)
            {
                captured = ex;
            }

            InvalidOperationException relayed =
                Assert.ThrowsException<InvalidOperationException>(
                    delegate { ExceptionRelay.Throw(captured); });

            Assert.AreSame(captured, relayed);
            StringAssert.Contains(relayed.StackTrace, "ThrowFromProducer");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowFromProducer()
        {
            throw new InvalidOperationException("synthetic producer failure");
        }
    }
}
