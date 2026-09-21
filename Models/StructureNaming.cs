using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SFRThelper.Models
{
    /// <summary>
    /// AAPM TG-263 inspired identifiers. Eclipse structure IDs are limited to 16 characters.
    /// </summary>
    public static class StructureNaming
    {
        public const int MaxIdLength = 16;
        public const string CompositePeaksId = "Lattice_Peaks";
        public const string ValleyId = "Lattice_Valley";
        public const string Ring01Id = "Ring_SFRT_0-1cm";
        public const string Ring13Id = "Ring_SFRT_1-3cm";
        public const string TempValidId = "zzSFRTtmp";

        private static readonly Regex PeakRegex = new Regex(@"^Peak_\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string FormatPeakId(int oneBasedIndex)
        {
            if (oneBasedIndex < 1)
                oneBasedIndex = 1;
            if (oneBasedIndex < 100)
                return "Peak_" + oneBasedIndex.ToString("00", CultureInfo.InvariantCulture);
            return Truncate("Peak_" + oneBasedIndex.ToString(CultureInfo.InvariantCulture));
        }

        public static bool IsIndividualPeakId(string id)
        {
            return !string.IsNullOrEmpty(id) && PeakRegex.IsMatch(id);
        }

        public static string Truncate(string id)
        {
            if (string.IsNullOrEmpty(id))
                return id;
            return id.Length <= MaxIdLength ? id : id.Substring(0, MaxIdLength);
        }

        public static string NextAvailable(string preferred, HashSet<string> existing)
        {
            string baseId = Truncate(preferred);
            if (existing == null || !existing.Contains(baseId))
                return baseId;

            for (int i = 1; i <= 99; i++)
            {
                string suffix = "_" + i.ToString("00", CultureInfo.InvariantCulture);
                int keep = Math.Max(1, MaxIdLength - suffix.Length);
                string truncatedBase = baseId.Length <= keep ? baseId : baseId.Substring(0, keep);
                string candidate = truncatedBase + suffix;
                if (!existing.Contains(candidate))
                    return candidate;
            }

            string fallback = "SFRT_" + DateTime.Now.Millisecond.ToString("D3", CultureInfo.InvariantCulture);
            return Truncate(fallback);
        }
    }
}
