using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SFRThelper.Models;

namespace SFRThelper.Helpers
{
    /// <summary>
    /// Pure dosimetric arithmetic used by the evaluation service. No VMS types.
    /// </summary>
    public static class SfrtMetricsCalculator
    {
        public static double Pvdr(double numerator, double denominator)
        {
            if (double.IsNaN(numerator) || double.IsNaN(denominator) || Math.Abs(denominator) < 1e-9)
                return double.NaN;
            return numerator / denominator;
        }

        public static double VolumeFractionPercent(double peaksCc, double targetCc)
        {
            if (double.IsNaN(peaksCc) || double.IsNaN(targetCc) || targetCc <= 0)
                return double.NaN;
            return 100.0 * peaksCc / targetCc;
        }

        public static bool IsVolumeFractionOutOfRange(double percent)
        {
            if (double.IsNaN(percent))
                return true;
            return percent < SFRTParameters.VolumeFractionMinPercent
                || percent > SFRTParameters.VolumeFractionMaxPercent;
        }

        public static bool IsPvdrLow(double pvdr)
        {
            if (double.IsNaN(pvdr))
                return true;
            return pvdr < SFRTParameters.PvdrWarningThreshold;
        }

        public static double CoefficientOfVariationPercent(IEnumerable<double> values)
        {
            if (values == null)
                return double.NaN;
            var list = values.Where(v => !double.IsNaN(v)).ToList();
            if (list.Count == 0)
                return double.NaN;
            double mean = list.Average();
            if (Math.Abs(mean) < 1e-9)
                return 0;
            double variance = list.Sum(v => (v - mean) * (v - mean)) / list.Count;
            return 100.0 * Math.Sqrt(variance) / mean;
        }

        public static FeasibilitySummary BuildFeasibility(
            LatticeGeometryContext geometry,
            SFRTParameters parameters,
            int sphereCount)
        {
            var summary = new FeasibilitySummary();
            double r = parameters.SphereRadiusMm;
            double sphereCc = sphereCount * (4.0 / 3.0) * Math.PI * r * r * r / 1000.0;
            double targetCc = geometry != null ? geometry.TargetVolumeCc : 0;
            double validCc = geometry != null ? geometry.ValidVolumeCc : 0;
            double fraction = VolumeFractionPercent(sphereCc, targetCc);

            summary.EstimatedSphereCount = sphereCount;
            summary.SphereVolumeCc = sphereCc;
            summary.TargetVolumeCc = targetCc;
            summary.ValidVolumeCc = validCc;
            summary.VolumeFractionPercent = fraction;
            summary.EdgeToEdgeClearanceMm = parameters.EdgeToEdgeClearanceMm;
            summary.EdgeToEdgeSiClearanceMm = parameters.EdgeToEdgeSiClearanceMm;
            summary.TargetClearanceMm = parameters.TargetClearanceMm;
            summary.ExternalClearanceMm = parameters.ExternalBoundaryClearanceMm;
            summary.BodyId = geometry != null ? geometry.BodyId : null;
            summary.Oar1ClearanceMm = parameters.Oar1ClearanceMm;
            summary.Oar2ClearanceMm = parameters.Oar2ClearanceMm;
            summary.Oar1Id = parameters.HasOar1 ? parameters.Oar1StructureId : null;
            summary.Oar2Id = parameters.HasOar2 ? parameters.Oar2StructureId : null;
            summary.PackingMode = parameters.PackingMode;
            summary.VolumeFractionOutOfRange = IsVolumeFractionOutOfRange(fraction);
            summary.SpacingInvalid = parameters.EffectiveLateralSpacingMm < 2.0 * parameters.SphereRadiusMm - 1e-6
                || parameters.EffectiveSiSpacingMm < 2.0 * parameters.SphereRadiusMm - 1e-6;
            summary.HasValidVolume = geometry != null && geometry.IsValid;

            if (summary.SpacingInvalid)
                summary.Warnings.Add("Center-to-center distance is less than one sphere diameter.");
            if (geometry == null)
            {
                summary.Warnings.Add("Preview the lattice to estimate sphere count and volume fraction from V_valid.");
            }
            else if (!summary.HasValidVolume)
            {
                summary.Warnings.Add("V_valid is empty. Reduce radius or clearances, or check OAR overlap with the target.");
            }
            if (summary.VolumeFractionOutOfRange && sphereCount > 0)
            {
                summary.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "Estimated volume fraction {0:F2}% is outside the 1.0–5.0% clinical window.",
                    fraction));
            }
            if (geometry != null && sphereCount == 0)
                summary.Warnings.Add("No candidate sphere centers lie strictly inside V_valid.");

            return summary;
        }

        public static string FormatGy(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "n/a";
            return value.ToString("F2", CultureInfo.InvariantCulture) + " Gy";
        }

        public static string FormatPercent(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "n/a";
            return value.ToString("F1", CultureInfo.InvariantCulture) + " %";
        }

        public static string FormatCc(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "n/a";
            return value.ToString("F2", CultureInfo.InvariantCulture) + " cc";
        }

        public static double Geud(IList<DvhBin> bins, double a)
        {
            if (bins == null || bins.Count < 2 || Math.Abs(a) < 1e-9)
                return double.NaN;

            double weighted = 0;
            double volume = 0;
            for (int i = 0; i < bins.Count - 1; i++)
            {
                double dV = bins[i].CumulativeVolume - bins[i + 1].CumulativeVolume;
                if (dV < 0)
                    dV = -dV;
                if (dV <= 0)
                    continue;
                double dose = bins[i].DoseGy;
                if (dose < 0)
                    dose = 0;
                weighted += dV * Math.Pow(dose, a);
                volume += dV;
            }
            if (volume <= 0)
                return double.NaN;
            return Math.Pow(weighted / volume, 1.0 / a);
        }

        public static double Mean(IEnumerable<double> values)
        {
            var list = values == null ? null : values.Where(v => !double.IsNaN(v)).ToList();
            if (list == null || list.Count == 0)
                return double.NaN;
            return list.Average();
        }

        public static double StdDev(IEnumerable<double> values)
        {
            var list = values == null ? null : values.Where(v => !double.IsNaN(v)).ToList();
            if (list == null || list.Count == 0)
                return double.NaN;
            double mean = list.Average();
            double variance = list.Sum(v => (v - mean) * (v - mean)) / list.Count;
            return Math.Sqrt(variance);
        }

        public static double Range(IEnumerable<double> values)
        {
            var list = values == null ? null : values.Where(v => !double.IsNaN(v)).ToList();
            if (list == null || list.Count == 0)
                return double.NaN;
            return list.Max() - list.Min();
        }

        public static string FormatGyPerMm(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "n/a";
            return value.ToString("F2", CultureInfo.InvariantCulture) + " Gy/mm";
        }

        public static string FormatRatio(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "n/a";
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }
    }
}
