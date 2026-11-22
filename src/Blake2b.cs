// BLAKE2b (RFC 7693), unkeyed, any output length up to 64 bytes. WireGuard for Windows publishes BLAKE2b-256 hashes of its
// installers in download.wireguard.com/windows-client/latest.sig; .NET Framework has no BLAKE2, so it's here (small and tested
// against RFC 7693's "abc" vector).
using System;
using System.IO;

namespace HallowVPN
{
    public static class Blake2b
    {
        static readonly ulong[] IV = { 0x6A09E667F3BCC908UL, 0xBB67AE8584CAA73BUL, 0x3C6EF372FE94F82BUL, 0xA54FF53A5F1D36F1UL, 0x510E527FADE682D1UL, 0x9B05688C2B3E6C1FUL, 0x1F83D9ABFB41BD6BUL, 0x5BE0CD19137E2179UL };
        static readonly byte[,] Sigma = {
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }, { 14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3 },
            { 11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4 }, { 7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8 },
            { 9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13 }, { 2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9 },
            { 12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11 }, { 13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10 },
            { 6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5 }, { 10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0 } };
        static ulong R(ulong x, int n) { return (x >> n) | (x << (64 - n)); }
        static void Compress(ulong[] h, byte[] block, ulong t, bool last)
        {
            var m = new ulong[16]; for (int i = 0; i < 16; i++) m[i] = BitConverter.ToUInt64(block, i * 8);
            var v = new ulong[16]; Array.Copy(h, v, 8); Array.Copy(IV, 0, v, 8, 8);
            v[12] ^= t; if (last) v[14] = ~v[14];
            for (int r = 0; r < 12; r++)
            {
                int s = r % 10;
                G(v, 0, 4, 8, 12, m[Sigma[s, 0]], m[Sigma[s, 1]]); G(v, 1, 5, 9, 13, m[Sigma[s, 2]], m[Sigma[s, 3]]);
                G(v, 2, 6, 10, 14, m[Sigma[s, 4]], m[Sigma[s, 5]]); G(v, 3, 7, 11, 15, m[Sigma[s, 6]], m[Sigma[s, 7]]);
                G(v, 0, 5, 10, 15, m[Sigma[s, 8]], m[Sigma[s, 9]]); G(v, 1, 6, 11, 12, m[Sigma[s, 10]], m[Sigma[s, 11]]);
                G(v, 2, 7, 8, 13, m[Sigma[s, 12]], m[Sigma[s, 13]]); G(v, 3, 4, 9, 14, m[Sigma[s, 14]], m[Sigma[s, 15]]);
            }
            for (int i = 0; i < 8; i++) h[i] ^= v[i] ^ v[i + 8];
        }
        static void G(ulong[] v, int a, int b, int c, int d, ulong x, ulong y)
        {
            v[a] = v[a] + v[b] + x; v[d] = R(v[d] ^ v[a], 32); v[c] = v[c] + v[d]; v[b] = R(v[b] ^ v[c], 24);
            v[a] = v[a] + v[b] + y; v[d] = R(v[d] ^ v[a], 16); v[c] = v[c] + v[d]; v[b] = R(v[b] ^ v[c], 63);
        }
        public static byte[] Hash(Stream s, int outLen)
        {
            var h = (ulong[])IV.Clone(); h[0] ^= 0x01010000UL ^ (ulong)outLen;
            var buf = new byte[128]; int fill = 0; ulong t = 0; var chunk = new byte[65536]; int n;
            while ((n = s.Read(chunk, 0, chunk.Length)) > 0)
                for (int i = 0; i < n; i++)
                {
                    if (fill == 128) { t += 128; Compress(h, buf, t, false); fill = 0; }
                    buf[fill++] = chunk[i];
                }
            t += (ulong)fill; for (int i = fill; i < 128; i++) buf[i] = 0;
            Compress(h, buf, t, true);
            var r = new byte[outLen]; for (int i = 0; i < outLen; i++) r[i] = (byte)(h[i / 8] >> (8 * (i % 8))); return r;
        }
        public static string Hex(byte[] b) { var sb = new System.Text.StringBuilder(); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }
        public static string HashFile(string path, int outLen) { using (var f = File.OpenRead(path)) return Hex(Hash(f, outLen)); }
    }
}
