using System;

namespace SFRThelper.Helpers
{
    /// <summary>
    /// 3-D Sobol' quasi-random sequence (Joe–Kuo direction numbers, 32-bit).
    /// Used for rigid unit-cell phase-shift sampling. No VMS types.
    /// </summary>
    public static class SobolSequence
    {
        private static readonly uint[] Dim1 = BuildVanDerCorput();
        private static readonly uint[] Dim2 = BuildDimension(new uint[] { 1u }, 0, 1);
        private static readonly uint[] Dim3 = BuildDimension(new uint[] { 1u, 3u }, 1, 2);

        public static void UnitCube(int index, out double x, out double y, out double z)
        {
            uint n = (uint)(index + 1);
            x = Component(n, Dim1);
            y = Component(n, Dim2);
            z = Component(n, Dim3);
        }

        private static double Component(uint index, uint[] direction)
        {
            uint x = 0;
            int bit = 0;
            uint n = index;
            while (n != 0 && bit < direction.Length)
            {
                if ((n & 1) != 0)
                    x ^= direction[bit];
                n >>= 1;
                bit++;
            }
            return x / 4294967296.0;
        }

        private static uint[] BuildVanDerCorput()
        {
            var v = new uint[32];
            for (int i = 0; i < 32; i++)
                v[i] = 1u << (31 - i);
            return v;
        }

        /// <summary>
        /// Direction numbers from primitive polynomial degree s and coefficient a.
        /// </summary>
        private static uint[] BuildDimension(uint[] m, uint a, int s)
        {
            var v = new uint[32];
            for (int i = 0; i < s && i < m.Length; i++)
                v[i] = m[i] << (31 - i);

            for (int i = s; i < 32; i++)
            {
                uint acc = v[i - s] ^ (v[i - s] >> s);
                uint aa = a;
                for (int k = 1; k < s; k++)
                {
                    if ((aa & 1) != 0)
                        acc ^= v[i - k];
                    aa >>= 1;
                }
                v[i] = acc;
            }
            return v;
        }
    }
}
