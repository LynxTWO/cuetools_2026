namespace CUETools.Ripper.SCSI
{
    /// <summary>
    /// Pure, hardware-independent core of the secure-ripping sector vote. Keeping this in the
    /// shipping path gives the deterministic TestRipper corpus a real production oracle instead
    /// of a copied algorithm that can silently drift.
    /// </summary>
    internal static class SecureSectorVote
    {
        internal const int BytesPerSector = 4 * 588;
        internal const int C2GroupsPerSector = BytesPerSector / 8;

        /// <summary>
        /// Reconstruct one sector from the accumulated clean/C2 bit lanes. Returns true when any
        /// byte lacks the winning margin required by <paramref name="correctionQuality"/>.
        /// <para><paramref name="unconfidentBytes"/>, when given, records WHICH bytes fell short
        /// (1 = the vote could not confirm this byte), indexed exactly like
        /// <paramref name="destination"/>. Salvage concealment needs that map: the reconstructed
        /// byte is still the best guess, but a guess the vote never confirmed is audible garbage
        /// unless something conceals it (R119).</para>
        /// </summary>
        internal static bool CorrectSector(
            long[,,] userData,
            byte[,] c2Count,
            byte[] destination,
            int sectorPosition,
            int passCount,
            int correctionQuality,
            byte[] unconfidentBytes = null)
        {
            bool hasLowConfidenceByte = false;
            const int c2WeightDivisor = 128;
            int errorLimit =
                c2WeightDivisor * (1 + correctionQuality) - 1;
            int destinationOffset = sectorPosition * BytesPerSector;

            for (int byteIndex = 0; byteIndex < BytesPerSector; byteIndex++)
            {
                long cleanVotes = userData[sectorPosition, 0, byteIndex];
                long c2Votes = userData[sectorPosition, 1, byteIndex];
                byte flaggedPasses =
                    c2Count[sectorPosition, byteIndex >> 3];
                int average =
                    (passCount - flaggedPasses) * c2WeightDivisor +
                    flaggedPasses;
                int bestValue = 0;
                bool byteUnconfident = false;

                for (int bit = 0; bit < 8; bit++)
                {
                    int margin = average - 2 * (int)(
                        (cleanVotes & 0xff) * c2WeightDivisor +
                        (c2Votes & 0xff));
                    int sign = margin >> 31;
                    byteUnconfident |= (margin ^ sign) < errorLimit;
                    bestValue += sign & (1 << bit);
                    cleanVotes >>= 8;
                    c2Votes >>= 8;
                }
                hasLowConfidenceByte |= byteUnconfident;

                destination[destinationOffset + byteIndex] =
                    (byte)bestValue;
                if (unconfidentBytes != null &&
                    destinationOffset + byteIndex < unconfidentBytes.Length)
                    unconfidentBytes[destinationOffset + byteIndex] =
                        (byte)(byteUnconfident ? 1 : 0);
            }

            return hasLowConfidenceByte;
        }
    }
}
