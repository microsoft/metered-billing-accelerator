// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.RuntimeCS;

public static class PartitionIDHashExtensions
{
    /// <summary>
    /// Calculate the partition ID based on partition key and number of partitions.
    /// </summary>
    /// <param name="partitionKey"></param>
    /// <param name="partitionCount"></param>
    /// <returns></returns>
    public static short DeterminePartitionId(this string partitionKey, short partitionCount)
    {
        static IList<int> Ranges(short partitionCount)
        {
            List<int> ranges = new(partitionCount);

            int count = short.MaxValue;
            int partitionsPerRangeBase = (int)Math.Floor((decimal)count / (decimal)partitionCount);
            int remainingPartitions = count - (partitionCount * partitionsPerRangeBase);

            for (int i = 0, end = -1; i < partitionCount - 1; i++)
            {
                int partitiontPerRange =
                    i < remainingPartitions
                    ? partitionsPerRangeBase + 1
                    : partitionsPerRangeBase;

                end = (int)Math.Min(end + partitiontPerRange, count - 1);
                ranges.Add(end);
            }

            ranges.Add(count - 1);

            return ranges;
        }

        static short ToLogical(string partitionKey)
        {
            static (UInt32, UInt32, UInt64) hash(byte[] bytes, UInt32 pc = 0, UInt32 pb = 0)
            {
                static (uint, uint, ulong) combine(uint c, uint b)
                {
                    var r = (ulong)c + (((ulong)b) << 32);
                    return (c, b, r);
                }

                static void mix(ref uint a, ref uint b, ref uint c)
                {
                    a -= c; a ^= (c << 4) | (c >> 28); c += b;
                    b -= a; b ^= (a << 6) | (a >> 26); a += c;
                    c -= b; c ^= (b << 8) | (b >> 24); b += a;
                    a -= c; a ^= (c << 16) | (c >> 16); c += b;
                    b -= a; b ^= (a << 19) | (a >> 13); a += c;
                    c -= b; c ^= (b << 4) | (b >> 28); b += a;
                }

                static void final_mix(ref uint a, ref uint b, ref uint c)
                {
                    c ^= b; c -= (b << 14) | (b >> 18);
                    a ^= c; a -= (c << 11) | (c >> 21);
                    b ^= a; b -= (a << 25) | (a >> 7);
                    c ^= b; c -= (b << 16) | (b >> 16);
                    a ^= c; a -= (c << 4) | (c >> 28);
                    b ^= a; b -= (a << 14) | (a >> 18);
                    c ^= b; c -= (b << 24) | (b >> 8);
                }

                var initial = (uint)(0xdeadbeef + bytes.Length + pc);
                UInt32 a = initial;
                UInt32 b = initial;
                UInt32 c = initial;
                c += pb;

                int index = 0, size = bytes.Length;
                while (size > 12)
                {
                    a += BitConverter.ToUInt32(bytes, index);
                    b += BitConverter.ToUInt32(bytes, index + 4);
                    c += BitConverter.ToUInt32(bytes, index + 8);

                    mix(ref a, ref b, ref c);

                    index += 12;
                    size -= 12;
                }

                switch (size)
                {
                    case 12:
                        c += BitConverter.ToUInt32(bytes, index + 8);
                        b += BitConverter.ToUInt32(bytes, index + 4);
                        a += BitConverter.ToUInt32(bytes, index);
                        break;
                    case 11:
                        c += ((uint)bytes[index + 10]) << 16;
                        goto case 10;
                    case 10:
                        c += ((uint)bytes[index + 9]) << 8;
                        goto case 9;
                    case 9:
                        c += (uint)bytes[index + 8];
                        goto case 8;
                    case 8:
                        b += BitConverter.ToUInt32(bytes, index + 4);
                        a += BitConverter.ToUInt32(bytes, index);
                        break;
                    case 7:
                        b += ((uint)bytes[index + 6]) << 16;
                        goto case 6;
                    case 6:
                        b += ((uint)bytes[index + 5]) << 8;
                        goto case 5;
                    case 5:
                        b += (uint)bytes[index + 4];
                        goto case 4;
                    case 4:
                        a += BitConverter.ToUInt32(bytes, index);
                        break;
                    case 3:
                        a += ((uint)bytes[index + 2]) << 16;
                        goto case 2;
                    case 2:
                        a += ((uint)bytes[index + 1]) << 8;
                        goto case 1;
                    case 1:
                        a += (uint)bytes[index];
                        break;
                    case 0:
                        return combine(c, b);
                }

                final_mix(ref a, ref b, ref c);

                return combine(c, b);
            }

            if (partitionKey == null) { return 0; }

            (uint hash1, uint hash2, _) = hash(
                bytes: System.Text.Encoding.ASCII.GetBytes(
                    partitionKey.ToUpper(System.Globalization.CultureInfo.InvariantCulture)),
                pc: 0,
                pb: 0);

            return (short)Math.Abs((hash1 ^ hash2) % short.MaxValue);
        }

        static short ToPartitionId(IList<int> ranges, short partition)
        {
            var lower = 0;
            var upper = ranges.Count - 1;
            while (lower < upper)
            {
                int middle = (lower + upper) >> 1;

                if (partition > ranges[middle])
                {
                    lower = middle + 1;
                }
                else
                {
                    upper = middle;
                }
            }

            return (short)lower;
        }

        return ToPartitionId(
            ranges: Ranges(partitionCount),
            partition: ToLogical(partitionKey));
    }
}
