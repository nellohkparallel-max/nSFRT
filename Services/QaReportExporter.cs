using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SFRThelper.Helpers;
using SFRThelper.Models;

namespace SFRThelper.Services
{
    /// <summary>
    /// Writes a CSV plus a single-page Courier PDF of the SFRT evaluation summary.
    /// No third-party PDF library — the PDF is a minimal PDF 1.4 document.
    /// </summary>
    public static class QaReportExporter
    {
        public static void Export(SFRTEvaluationResult result, SFRTParameters parameters, string csvPath)
        {
            Export(result, parameters, csvPath, null, null);
        }

        public static void Export(
            SFRTEvaluationResult result,
            SFRTParameters parameters,
            string csvPath,
            IEnumerable<SphereModel> spheres,
            string patientStatus)
        {
            if (string.IsNullOrEmpty(csvPath))
                throw new ArgumentException("A destination path is required.", "csvPath");

            string directory = Path.GetDirectoryName(csvPath);
            string stem = Path.GetFileNameWithoutExtension(csvPath);
            if (string.IsNullOrEmpty(directory))
                directory = Environment.CurrentDirectory;

            string csvFull = Path.Combine(directory, stem + ".csv");
            string pdfFull = Path.Combine(directory, stem + ".pdf");

            File.WriteAllText(csvFull, BuildCsv(result, parameters, spheres, patientStatus), Encoding.UTF8);
            File.WriteAllBytes(pdfFull, BuildPdf(BuildReportLines(result, parameters, spheres, patientStatus)));
        }

        public static string BuildCsv(SFRTEvaluationResult result, SFRTParameters parameters)
        {
            return BuildCsv(result, parameters, null, null);
        }

        public static string BuildCsv(
            SFRTEvaluationResult result,
            SFRTParameters parameters,
            IEnumerable<SphereModel> spheres,
            string patientStatus)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Patient," + Csv(patientStatus));
            if (result != null)
                sb.AppendLine("Plan," + Csv(result.PlanDisplayName));
            sb.AppendLine();
            sb.AppendLine("Category,Structure,Metric,Absolute,Relative,Comment");
            if (result != null && result.MetricRows != null)
            {
                foreach (var row in result.MetricRows)
                {
                    sb.Append(Csv(row.Category)).Append(',')
                      .Append(Csv(row.Structure)).Append(',')
                      .Append(Csv(row.Metric)).Append(',')
                      .Append(Csv(row.Absolute)).Append(',')
                      .Append(Csv(row.Relative)).Append(',')
                      .Append(Csv(row.Comment)).AppendLine();
                }
            }

            sb.AppendLine();
            sb.AppendLine("Alerts");
            if (result != null && result.Alerts != null)
            {
                foreach (var alert in result.Alerts)
                    sb.AppendLine(Csv(alert));
            }

            sb.AppendLine();
            sb.AppendLine("Geometry");
            if (parameters != null)
            {
                sb.AppendLine("Preset," + Csv(parameters.Preset.ToString()));
                sb.AppendLine("Diameter_mm," + F(parameters.SphereDiameterMm));
                sb.AppendLine("LateralSpacing_mm," + F(parameters.EffectiveLateralSpacingMm));
                sb.AppendLine("SiSpacing_mm," + F(parameters.EffectiveSiSpacingMm));
                sb.AppendLine("TargetMargin_mm," + F(parameters.TargetInternalMarginMm));
                sb.AppendLine("SkinClearance_mm," + F(parameters.ExternalBoundaryClearanceMm));
                sb.AppendLine("Packing," + Csv(parameters.PackingMode.ToString()));
                sb.AppendLine("Yaw_deg," + F(parameters.GridRotationDeg));
                sb.AppendLine("Maximization," + (parameters.EnableSphereMaximization ? "true" : "false"));
                sb.AppendLine("Strategy," + Csv(parameters.OptimizationStrategy.ToString()));
            }

            if (result != null)
            {
                sb.AppendLine();
                sb.AppendLine("Delivery");
                sb.AppendLine("DoseGridMax_mm," + F(result.DoseGridMaxMm));
                sb.AppendLine("TotalMU," + F(result.TotalMu));
                sb.AppendLine("MU_per_Gy," + F(result.MuPerGy));
                sb.AppendLine("Overmodulated," + (result.Overmodulated ? "true" : "false"));
                sb.AppendLine("MeanGradient_Gy_per_mm," + F(result.MeanGradientGyPerMm));
                sb.AppendLine("MinValleyTrough_Gy," + F(result.MinValleyTroughGy));
                sb.AppendLine("gEUD_target_a-10," + F(result.TargetGeudAMinus10));
                sb.AppendLine("gEUD_valley_a1," + F(result.ValleyGeudA1));
                sb.AppendLine("gEUD_valley_a2," + F(result.ValleyGeudA2));
            }

            sb.AppendLine();
            sb.AppendLine("PeakCenters");
            sb.AppendLine("Id,X_mm,Y_mm,Z_mm,R_mm");
            if (spheres != null)
            {
                foreach (var s in spheres)
                {
                    if (s == null)
                        continue;
                    sb.Append(Csv(s.Id)).Append(',')
                      .Append(F(s.X)).Append(',')
                      .Append(F(s.Y)).Append(',')
                      .Append(F(s.Z)).Append(',')
                      .Append(F(s.Radius)).AppendLine();
                }
            }

            return sb.ToString();
        }

        public static string[] BuildReportLines(SFRTEvaluationResult result, SFRTParameters parameters)
        {
            return BuildReportLines(result, parameters, null, null);
        }

        public static string[] BuildReportLines(
            SFRTEvaluationResult result,
            SFRTParameters parameters,
            IEnumerable<SphereModel> spheres,
            string patientStatus)
        {
            var lines = new List<string>();
            lines.Add("nSFRT Lattice / SFRT QA Report");
            lines.Add("Generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(patientStatus))
                lines.Add("Patient: " + patientStatus);
            lines.Add(string.Empty);
            if (result != null)
            {
                lines.Add("Plan: " + (result.PlanDisplayName ?? ""));
                lines.Add("PVDR_mean: " + SfrtMetricsCalculator.FormatRatio(result.PvdrMean)
                    + "   PVDR_10/90: " + SfrtMetricsCalculator.FormatRatio(result.Pvdr10_90)
                    + "   PVDR_5/95: " + SfrtMetricsCalculator.FormatRatio(result.Pvdr5_95));
                lines.Add("Volume fraction: " + SfrtMetricsCalculator.FormatPercent(result.VolumeFractionPercent));
                lines.Add("gEUD target a=-10: " + SfrtMetricsCalculator.FormatGy(result.TargetGeudAMinus10)
                    + "   valley a=1: " + SfrtMetricsCalculator.FormatGy(result.ValleyGeudA1)
                    + "   valley a=2: " + SfrtMetricsCalculator.FormatGy(result.ValleyGeudA2));
                lines.Add("Dose grid max: " + F(result.DoseGridMaxMm) + " mm"
                    + (result.DoseGridCoarse ? "  [COARSE]" : ""));
                lines.Add("Total MU: " + F(result.TotalMu) + "   MU/Gy: " + F(result.MuPerGy)
                    + (result.Overmodulated ? "  [OVERMODULATED]" : ""));
                lines.Add("Mean gradient: " + SfrtMetricsCalculator.FormatGyPerMm(result.MeanGradientGyPerMm)
                    + "   Min trough: " + SfrtMetricsCalculator.FormatGy(result.MinValleyTroughGy));
                lines.Add("Peak Dmean mean/SD/range: "
                    + SfrtMetricsCalculator.FormatGy(result.PeakDmeanMeanGy) + " / "
                    + SfrtMetricsCalculator.FormatGy(result.PeakDmeanSdGy) + " / "
                    + SfrtMetricsCalculator.FormatGy(result.PeakDmeanRangeGy));
            }
            if (parameters != null)
            {
                lines.Add(string.Empty);
                lines.Add("Preset: " + parameters.Preset
                    + "   D=" + F(parameters.SphereDiameterMm) + " mm"
                    + "   dxy=" + F(parameters.EffectiveLateralSpacingMm)
                    + "   dSI=" + F(parameters.EffectiveSiSpacingMm)
                    + "   yaw=" + F(parameters.GridRotationDeg) + " deg");
            }
            if (spheres != null)
            {
                lines.Add(string.Empty);
                lines.Add("Peak coordinates (mm):");
                int shown = 0;
                foreach (var s in spheres)
                {
                    if (s == null || shown >= 24)
                        break;
                    lines.Add(string.Format(CultureInfo.InvariantCulture,
                        "  {0}: ({1:F1}, {2:F1}, {3:F1})  r={4:F1}",
                        s.Id, s.X, s.Y, s.Z, s.Radius));
                    shown++;
                }
            }
            lines.Add(string.Empty);
            lines.Add("Alerts:");
            if (result != null && result.Alerts != null && result.Alerts.Count > 0)
            {
                foreach (var alert in result.Alerts)
                    lines.Add(" - " + alert);
            }
            else
            {
                lines.Add(" - none");
            }
            return lines.ToArray();
        }

        private static byte[] BuildPdf(string[] lines)
        {
            var content = new StringBuilder();
            content.Append("BT /F1 9 Tf 36 760 Td\n");
            int count = 0;
            foreach (var raw in lines)
            {
                if (count >= 70)
                    break;
                string line = EscapePdf(raw ?? string.Empty);
                if (line.Length > 110)
                    line = line.Substring(0, 110);
                content.Append("(").Append(line).Append(") Tj\n0 -11 Td\n");
                count++;
            }
            content.Append("ET\n");
            string stream = content.ToString();
            int streamLen = Encoding.ASCII.GetByteCount(stream);

            var pdf = new StringBuilder();
            pdf.Append("%PDF-1.4\n");
            int[] offsets = new int[6];
            offsets[1] = pdf.Length;
            pdf.Append("1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n");
            offsets[2] = pdf.Length;
            pdf.Append("2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n");
            offsets[3] = pdf.Length;
            pdf.Append("3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >> endobj\n");
            offsets[4] = pdf.Length;
            pdf.Append("4 0 obj << /Length ").Append(streamLen).Append(" >> stream\n");
            pdf.Append(stream);
            pdf.Append("endstream endobj\n");
            offsets[5] = pdf.Length;
            pdf.Append("5 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Courier >> endobj\n");
            int xref = pdf.Length;
            pdf.Append("xref\n0 6\n0000000000 65535 f \n");
            for (int i = 1; i <= 5; i++)
                pdf.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            pdf.Append("trailer << /Size 6 /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            return Encoding.ASCII.GetBytes(pdf.ToString());
        }

        private static string EscapePdf(string text)
        {
            return text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        }

        private static string Csv(string value)
        {
            if (value == null)
                return "";
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private static string F(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "";
            return value.ToString("F3", CultureInfo.InvariantCulture);
        }
    }
}
