namespace CUETools.Ripper.SCSI
{
    /// <summary>
    /// Pure error concealment for samples the secure vote could never confirm (R119). No SCSI,
    /// no drive state, so it is unit-tested with no hardware.
    /// <para>Why this exists: a CD player conceals uncorrectable samples - it interpolates across
    /// them, or mutes - which is why a scratched disc still sounds like music. A CD-ROM drive
    /// reading in data mode does not. It hands back whatever came off the disc and uses C2
    /// pointers to say which samples it could not correct. The first salvage build asked the
    /// drive to stop reporting C2 at all, on the assumption that its output was already
    /// concealed; the result was a capture with fourteen times the sample-glitch rate of a good
    /// rip, audibly pulsing and unlistenable. The fix is the opposite of that assumption: keep
    /// C2 on so bad samples are known, and conceal the ones that never converge here.</para>
    /// </summary>
    public static class SampleConcealment
    {
        /// <summary>Bytes per stereo 16-bit frame.</summary>
        public const int BytesPerFrame = 4;

        /// <summary>Runs longer than this are muted rather than interpolated: a ramp drawn
        /// between two points a quarter second apart is its own artifact, while a short fade to
        /// silence is what a player does with damage this wide.</summary>
        public const int MaxInterpolatedFrames = 588;

        /// <summary>Conceal every frame whose bytes the vote could not confirm.
        /// <paramref name="pcm"/> is 16-bit little-endian stereo, <paramref name="unconfident"/>
        /// is the byte map from the vote (1 = unconfirmed), both indexed identically. Returns the
        /// number of concealed frames, per channel summed.</summary>
        public static int Conceal(byte[] pcm, byte[] unconfident, int frameCount)
        {
            if (pcm == null || unconfident == null || frameCount <= 0)
                return 0;
            int usable = System.Math.Min(
                frameCount,
                System.Math.Min(pcm.Length, unconfident.Length) / BytesPerFrame);
            int concealed = 0;
            for (int channel = 0; channel < 2; channel++)
                concealed += ConcealChannel(pcm, unconfident, usable, channel);
            return concealed;
        }

        private static int ConcealChannel(byte[] pcm, byte[] unconfident, int frameCount, int channel)
        {
            int concealed = 0;
            int frame = 0;
            while (frame < frameCount)
            {
                if (!IsBad(unconfident, frame, channel)) { frame++; continue; }

                int runStart = frame;
                while (frame < frameCount && IsBad(unconfident, frame, channel))
                    frame++;
                int runEnd = frame;                       // exclusive
                int runLength = runEnd - runStart;

                bool hasBefore = runStart > 0;
                bool hasAfter = runEnd < frameCount;
                short before = hasBefore ? ReadSample(pcm, runStart - 1, channel) : (short)0;
                short after = hasAfter ? ReadSample(pcm, runEnd, channel) : (short)0;

                if (!hasBefore && !hasAfter)
                {
                    // Nothing anywhere to anchor on: silence is the only honest output.
                    for (int i = runStart; i < runEnd; i++) WriteSample(pcm, i, channel, 0);
                }
                else if (runLength > MaxInterpolatedFrames)
                {
                    // Too wide to draw across: fade the edges into silence.
                    int fade = MaxInterpolatedFrames / 4;
                    for (int i = runStart; i < runEnd; i++)
                    {
                        int fromStart = i - runStart, fromEnd = runEnd - 1 - i;
                        short value = 0;
                        if (hasBefore && fromStart < fade)
                            value = (short)(before * (fade - fromStart) / fade);
                        else if (hasAfter && fromEnd < fade)
                            value = (short)(after * (fade - fromEnd) / fade);
                        WriteSample(pcm, i, channel, value);
                    }
                }
                else
                {
                    short from = hasBefore ? before : after;
                    short to = hasAfter ? after : before;
                    int steps = runLength + 1;
                    for (int i = runStart; i < runEnd; i++)
                    {
                        int step = i - runStart + 1;
                        int value = from + (to - from) * step / steps;
                        WriteSample(pcm, i, channel, (short)value);
                    }
                }
                concealed += runLength;
            }
            return concealed;
        }

        private static bool IsBad(byte[] unconfident, int frame, int channel)
        {
            int at = frame * BytesPerFrame + channel * 2;
            return unconfident[at] != 0 || unconfident[at + 1] != 0;
        }

        private static short ReadSample(byte[] pcm, int frame, int channel)
        {
            int at = frame * BytesPerFrame + channel * 2;
            return (short)(pcm[at] | (pcm[at + 1] << 8));
        }

        private static void WriteSample(byte[] pcm, int frame, int channel, short value)
        {
            int at = frame * BytesPerFrame + channel * 2;
            pcm[at] = (byte)(value & 0xff);
            pcm[at + 1] = (byte)((value >> 8) & 0xff);
        }
    }
}
