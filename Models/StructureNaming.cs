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
        /// <summary>Union of PeakCore_* for core-focused PO (Grams undercoverage / LatticeHelper cores).</summary>
        public const string CompositeCoresId = "Lattice_Cores";
        public const string ValleyId = "Lattice_Valley";
        /// <summary>Eclipse 16-char ID for Peak_Penumbra_Shell (Grams z_IC / inner control).</summary>
        public const string PenumbraShellId = "Peak_Penumbra";
        /// <summary>Grams z_MC middle control: Peaks.Margin(10 mm) \ Peaks.Margin(5 mm).</summary>
        public const string PenumbraShell2Id = "Peak_Penu2";
        public const string ValleyCoreId = "Valley_Core";
        public const string Ring01Id = "Ring_SFRT_0-1cm";
        public const string Ring13Id = "Ring_SFRT_1-3cm";
        public const string TempValidId = "zzSFRTtmp";
        /// <summary>1 mm gap between target and Ring_SFRT_0-1cm inner edge.</summary>
        public const double RingTargetGapMm = 1.0;
        /// <summary>Grams z_MC inner surface (0.5 cm from Lattice_Peaks).</summary>
        public const double MiddleShellInnerMm = 5.0;
        /// <summary>Grams z_MC outer surface (1.0 cm from Lattice_Peaks).</summary>
        public const double MiddleShellOuterMm = 10.0;
        /// <summary>Inset applied when building sphere cores from CT-slice groups.</summary>
        public const double SphereCoreInsetMm = 1.0;
        /// <summary>Default Peak_Penumbra / z_IC thickness matching Grams 0.5 cm inner control.</summary>
        public const double DefaultInnerShellMm = 5.0;
        /// <summary>TrueBeam 10X-FFF clinical SFRT dose rate (Grams / automated lattice).</summary>
        public const int TenXFffDoseRate = 2400;

        public const string DicomControl = "CONTROL";
        public const string DicomAvoidance = "AVOIDANCE";
        public const string DicomMarker = "MARKER";

        private static readonly Regex PeakRegex = new Regex(@"^Peak_\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string FormatPeakId(int oneBasedIndex)
        {
            if (oneBasedIndex < 1)
                oneBasedIndex = 1;
            if (oneBasedIndex < 100)
                return "Peak_" + oneBasedIndex.ToString("00", CultureInfo.InvariantCulture);
            return Truncate("Peak_" + oneBasedIndex.ToString(CultureInfo.InvariantCulture));
        }

        public static string FormatPeakLayerId(int oneBasedIndex)
        {
            if (oneBasedIndex < 1)
                oneBasedIndex = 1;
            return Truncate("PeakLayer_" + oneBasedIndex.ToString("00", CultureInfo.InvariantCulture));
        }

        public static string FormatPeakCoreId(int oneBasedIndex)
        {
            if (oneBasedIndex < 1)
                oneBasedIndex = 1;
            return Truncate("PeakCore_" + oneBasedIndex.ToString("00", CultureInfo.InvariantCulture));
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
