using System.Collections.Generic;
using System.Linq;
using SFRThelper.Helpers;
using SFRThelper.Models;

namespace SFRThelper.Services
{
    public class SFRTEvaluationService
    {
        private readonly IESAPIService _esapi;

        public SFRTEvaluationService(IESAPIService esapi)
        {
            _esapi = esapi;
        }

        public SFRTEvaluationResult Evaluate(string planKey, SFRTParameters parameters)
        {
            var result = new SFRTEvaluationResult();
            if (string.IsNullOrEmpty(planKey))
            {
                result.ErrorMessage = "Select a calculated plan or plan sum.";
                return result;
            }

            EvaluationDoseContext ctx = _esapi.ExtractEvaluationDoseContext(planKey, parameters);
            if (ctx == null)
            {
                result.ErrorMessage = "Failed to extract dose context.";
                return result;
            }
            if (!string.IsNullOrEmpty(ctx.ErrorMessage) && ctx.Peaks == null)
            {
                result.ErrorMessage = ctx.ErrorMessage;
                result.PlanDisplayName = ctx.Plan != null ? ctx.Plan.DisplayName : null;
                return result;
            }

            result.PlanDisplayName = ctx.Plan != null ? ctx.Plan.DisplayName : planKey;
            result.IsPlanSum = ctx.Plan != null && ctx.Plan.IsPlanSum;
            result.PrescriptionDoseGy = ctx.Plan != null ? ctx.Plan.PrescriptionDoseGy : null;
            result.Target = ctx.Target;
            result.Peaks = ctx.Peaks;
            result.Valley = ctx.Valley;
            result.TargetId = ctx.Target != null ? ctx.Target.Id : parameters.SelectedTargetId;
            result.PeaksId = ctx.Peaks != null ? ctx.Peaks.Id : StructureNaming.CompositePeaksId;
            result.ValleyId = ctx.Valley != null ? ctx.Valley.Id : StructureNaming.ValleyId;

            if (ctx.Peaks != null && ctx.Valley != null)
                result.PvdrMean = SfrtMetricsCalculator.Pvdr(ctx.Peaks.DmeanGy, ctx.Valley.DmeanGy);
            if (ctx.Target != null)
            {
                result.Pvdr10_90 = SfrtMetricsCalculator.Pvdr(ctx.Target.D10Gy, ctx.Target.D90Gy);
                result.Pvdr5_95 = SfrtMetricsCalculator.Pvdr(ctx.Target.D5Gy, ctx.Target.D95Gy);
            }

            double peaksVol = ctx.Peaks != null ? ctx.Peaks.VolumeCc : double.NaN;
            double targetVol = ctx.Target != null ? ctx.Target.VolumeCc : double.NaN;
            result.VolumeFractionPercent = SfrtMetricsCalculator.VolumeFractionPercent(peaksVol, targetVol);

            if (ctx.IndividualPeaks != null)
            {
                foreach (var peak in ctx.IndividualPeaks.OrderBy(p => p.Id))
                {
                    result.IndividualPeaks.Add(new PeakDoseRow
                    {
                        Id = peak.Id,
                        VolumeCc = peak.VolumeCc,
                        DmaxGy = peak.DmaxGy,
                        DmeanGy = peak.DmeanGy,
                        DminGy = peak.DminGy
                    });
                }
                result.PeakDmeanHomogeneityPercent = SfrtMetricsCalculator.CoefficientOfVariationPercent(
                    result.IndividualPeaks.Select(p => p.DmeanGy));
                result.PeakDmaxCvPercent = SfrtMetricsCalculator.CoefficientOfVariationPercent(
                    result.IndividualPeaks.Select(p => p.DmaxGy));
            }

            if (ctx.Oars != null)
            {
                int index = 1;
                foreach (var oar in ctx.Oars)
                {
                    result.Oars.Add(new OarDoseRow
                    {
                        Id = oar.Id,
                        Role = "OAR Avoidance " + index,
                        VolumeCc = oar.VolumeCc,
                        DmaxGy = oar.DmaxGy,
                        D003CcGy = oar.D003CcGy
                    });
                    index++;
                }
            }

            result.MetricRows = BuildRows(result);
            result.Alerts = BuildAlerts(result, ctx.ErrorMessage);
            result.Success = result.Peaks != null && result.Valley != null;
            return result;
        }

        private static List<EvaluationMetricRow> BuildRows(SFRTEvaluationResult result)
        {
            var rows = new List<EvaluationMetricRow>();
            AddPvdr(rows, "PVDR", "Lattice_Peaks / Lattice_Valley", "PVDR_mean", result.PvdrMean, "Dmean(peaks) / Dmean(valley)");
            AddPvdr(rows, "PVDR", result.TargetId ?? "Target", "PVDR_10/90", result.Pvdr10_90, "D10%(target) / D90%(target)");
            AddPvdr(rows, "PVDR", result.TargetId ?? "Target", "PVDR_5/95", result.Pvdr5_95, "D5%(target) / D95%(target)");
            rows.Add(new EvaluationMetricRow
            {
                Category = "Geometry",
                Structure = result.PeaksId,
                Metric = "Volume fraction",
                Absolute = SfrtMetricsCalculator.FormatPercent(result.VolumeFractionPercent),
                Relative = SfrtMetricsCalculator.IsVolumeFractionOutOfRange(result.VolumeFractionPercent)
                    ? "outside 1.0–5.0%" : "within 1.0–5.0%",
                Comment = "Volume(Lattice_Peaks) / Volume(Target)"
            });

            AddStructureRows(rows, "Peaks", result.Peaks, includeV100: true, valleySet: false);
            AddStructureRows(rows, "Valley", result.Valley, includeV100: false, valleySet: true);

            if (result.Target != null)
            {
                rows.Add(Metric("Target", result.Target.Id, "Volume", SfrtMetricsCalculator.FormatCc(result.Target.VolumeCc), "", ""));
            }

            foreach (var oar in result.Oars)
            {
                rows.Add(Metric("OAR", oar.Id, "Dmax", SfrtMetricsCalculator.FormatGy(oar.DmaxGy), "", oar.Role));
                rows.Add(Metric("OAR", oar.Id, "D0.03cc", SfrtMetricsCalculator.FormatGy(oar.D003CcGy), "", oar.Role));
            }

            if (result.IndividualPeaks.Count > 0)
            {
                rows.Add(Metric("Peaks", "Peak_xx", "Dmean CV",
                    SfrtMetricsCalculator.FormatPercent(result.PeakDmeanHomogeneityPercent), "",
                    "Inter-peak mean-dose coefficient of variation"));
                rows.Add(Metric("Peaks", "Peak_xx", "Dmax CV",
                    SfrtMetricsCalculator.FormatPercent(result.PeakDmaxCvPercent), "",
                    "Inter-peak max-dose coefficient of variation"));
            }

            return rows;
        }

        private static void AddStructureRows(
            List<EvaluationMetricRow> rows, string category, StructureDoseSnapshot snap, bool includeV100, bool valleySet)
        {
            if (snap == null)
                return;
            rows.Add(Metric(category, snap.Id, "Volume", SfrtMetricsCalculator.FormatCc(snap.VolumeCc), "", ""));
            rows.Add(Metric(category, snap.Id, "Dmax", SfrtMetricsCalculator.FormatGy(snap.DmaxGy), SfrtMetricsCalculator.FormatPercent(snap.DmaxPercent), ""));
            rows.Add(Metric(category, snap.Id, "Dmean", SfrtMetricsCalculator.FormatGy(snap.DmeanGy), SfrtMetricsCalculator.FormatPercent(snap.DmeanPercent), ""));
            rows.Add(Metric(category, snap.Id, "Dmin", SfrtMetricsCalculator.FormatGy(snap.DminGy), SfrtMetricsCalculator.FormatPercent(snap.DminPercent), ""));
            if (valleySet)
            {
                rows.Add(Metric(category, snap.Id, "D2%", SfrtMetricsCalculator.FormatGy(snap.D2Gy), SfrtMetricsCalculator.FormatPercent(snap.D2Percent), ""));
                rows.Add(Metric(category, snap.Id, "D10%", SfrtMetricsCalculator.FormatGy(snap.D10Gy), SfrtMetricsCalculator.FormatPercent(snap.D10Percent), ""));
            }
            else
            {
                rows.Add(Metric(category, snap.Id, "D95%", SfrtMetricsCalculator.FormatGy(snap.D95Gy), SfrtMetricsCalculator.FormatPercent(snap.D95Percent), ""));
                rows.Add(Metric(category, snap.Id, "D98%", SfrtMetricsCalculator.FormatGy(snap.D98Gy), SfrtMetricsCalculator.FormatPercent(snap.D98Percent), ""));
                if (includeV100)
                    rows.Add(Metric(category, snap.Id, "V100%", SfrtMetricsCalculator.FormatPercent(snap.V100Percent), "", "Relative to plan prescription"));
            }
        }

        private static void AddPvdr(List<EvaluationMetricRow> rows, string category, string structure, string metric, double value, string comment)
        {
            rows.Add(new EvaluationMetricRow
            {
                Category = category,
                Structure = structure,
                Metric = metric,
                Absolute = SfrtMetricsCalculator.FormatRatio(value),
                Relative = SfrtMetricsCalculator.IsPvdrLow(value) ? "below 2.5" : "OK",
                Comment = comment
            });
        }

        private static EvaluationMetricRow Metric(string category, string structure, string metric, string abs, string rel, string comment)
        {
            return new EvaluationMetricRow
            {
                Category = category,
                Structure = structure,
                Metric = metric,
                Absolute = abs,
                Relative = rel,
                Comment = comment
            };
        }

        private static List<string> BuildAlerts(SFRTEvaluationResult result, string contextError)
        {
            var alerts = new List<string>();
            if (!string.IsNullOrEmpty(contextError))
                alerts.Add(contextError);
            if (SfrtMetricsCalculator.IsPvdrLow(result.PvdrMean))
                alerts.Add("PVDR_mean is " + SfrtMetricsCalculator.FormatRatio(result.PvdrMean) + " (threshold 2.5).");
            if (SfrtMetricsCalculator.IsPvdrLow(result.Pvdr10_90))
                alerts.Add("PVDR_10/90 is " + SfrtMetricsCalculator.FormatRatio(result.Pvdr10_90) + " (threshold 2.5).");
            if (SfrtMetricsCalculator.IsVolumeFractionOutOfRange(result.VolumeFractionPercent))
                alerts.Add("Peak volume fraction is " + SfrtMetricsCalculator.FormatPercent(result.VolumeFractionPercent)
                    + " (clinical window 1.0–5.0%).");
            return alerts;
        }
    }
}
