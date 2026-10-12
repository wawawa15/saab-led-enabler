namespace SaabDidTool.Models
{
    /// <summary>
    /// Confirmed bit positions for the UEC and REC bulb-outage/substitution
    /// bytes. The Encode* methods are the exact mapping provided (verbatim)
    /// - the Decode* methods are their inverse, used to populate the
    /// checklist from a fresh read.
    /// </summary>
    public static class GmlanBitMap
    {
        // ---- UEC (Front): single byte ----

        public static byte EncodeUecMask(
            bool parking, bool fog, bool low,
            bool high, bool turn)
        {
            byte mask = 0;

            if (parking) mask |= 0x80;
            if (fog) mask |= 0x40;
            if (low) mask |= 0x20;
            if (high) mask |= 0x10;
            if (turn) mask |= 0x01;

            return mask;
        }

        public static (bool parking, bool fog, bool low, bool high, bool turn) DecodeUecMask(byte b)
        {
            return (
                parking: (b & 0x80) != 0,
                fog: (b & 0x40) != 0,
                low: (b & 0x20) != 0,
                high: (b & 0x10) != 0,
                turn: (b & 0x01) != 0
            );
        }

        // ---- REC (Rear): two bytes - note Turn Signals lives in byte 2, not byte 1 ----

        public static (byte b1, byte b2) EncodeRecMask(
            bool tail, bool fog, bool stop,
            bool reverse, bool plate, bool turn)
        {
            byte b1 = 0;

            if (tail) b1 |= 0x80;
            if (fog) b1 |= 0x40;
            if (stop) b1 |= 0x20;
            if (reverse) b1 |= 0x10;
            if (plate) b1 |= 0x08;

            // Preserve unrelated bits in byte 2.
            byte b2 = 0xF4;
            if (turn) b2 |= 0x08;

            return (b1, b2);
        }

        /// <summary>
        /// Inverse of EncodeRecMask. existingB2 should be the byte 2 value
        /// actually read from the car (not the hardcoded 0xF4 baseline),
        /// so writes can preserve whatever else lives in that byte for this
        /// specific vehicle.
        /// </summary>
        public static (bool tail, bool fog, bool stop, bool reverse, bool plate, bool turn) DecodeRecMask(byte b1, byte b2)
        {
            return (
                tail: (b1 & 0x80) != 0,
                fog: (b1 & 0x40) != 0,
                stop: (b1 & 0x20) != 0,
                reverse: (b1 & 0x10) != 0,
                plate: (b1 & 0x08) != 0,
                turn: (b2 & 0x08) != 0
            );
        }
    }
}
