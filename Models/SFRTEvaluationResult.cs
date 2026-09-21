using System.Collections.Generic;

namespace SFRThelper.Models
{
    public class EvaluationMetricRow
    {
        public string Category { get; set; }
        public string Structure { get; set; }
        public string Metric { get; set; }
        public string Absolute { get; set; }
        public string Relative { get; set; }
        public string Comment { get; set; }
    }

    public class PeakDoseRow
    {
        public string Id { get; set; }
        public double VolumeCc { get; set; }
        public double DmaxGy { get; set; }
        public double DmeanGy { get; set; }
        public double DminGy { get; set; }
    }

    public class OarDoseRow
    {
        public string Id { get; set; }
        public string Role { get; set; }
        public double VolumeCc { get; set; }
        public double DmaxGy { get; set; }
        public double D003CcGy { get; set; }
    }

    public class StructureDoseSnapshot
    {
        public string Id { get; set; }
        public double VolumeCc { get; set; }
        public bool HasDose { get; set; }
        public double DmaxGy { get; set; }
        public double DmeanGy { get; set; }
        public double DminGy { get; set; }
        public double D2Gy { get; set; }
        public double D5Gy { get; set; }
        public double D10Gy { get; set; }
        public double D90Gy { get; set; }
        public double D95Gy { get; set; }
        public double D98Gy { get; set; }
        public double DmaxPercent { get; set; }
        public double DmeanPercent { get; set; }
        public double DminPercent { get; set; }
        public double D2Percent { get; set; }
        public double D5Percent { get; set; }
        public double D10Percent { get; set; }
        public double D90Percent { get; set; }
        public double D95Percent { get; set; }
        public double D98Percent { get; set; }
        public double V100Percent { get; set; }
        public double D003CcGy { get; set; }
    }

    public class SFRTEvaluationResult
    {
        public string PlanDisplayName { get; set; }
        public bool IsPlanSum { get; set; }
        public double? PrescriptionDoseGy { get; set; }
        public string TargetId { get; set; }
        public string PeaksId { get; set; }
        public string ValleyId { get; set; }

        public double PvdrMean { get; set; }
        public double Pvdr10_90 { get; set; }
        public double Pvdr5_95 { get; set; }
        public double VolumeFractionPercent { get; set; }

        public StructureDoseSnapshot Target { get; set; }
        public StructureDoseSnapshot Peaks { get; set; }
        public StructureDoseSnapshot Valley { get; set; }
        public List<PeakDoseRow> IndividualPeaks { get; set; }
        public List<OarDoseRow> Oars { get; set; }
        public List<EvaluationMetricRow> MetricRows { get; set; }
        public List<string> Alerts { get; set; }
        public string ErrorMessage { get; set; }
        public bool Success { get; set; }

        public double PeakDmeanHomogeneityPercent { get; set; }
        public double PeakDmaxCvPercent { get; set; }

        public SFRTEvaluationResult()
        {
            IndividualPeaks = new List<PeakDoseRow>();
            Oars = new List<OarDoseRow>();
            MetricRows = new List<EvaluationMetricRow>();
            Alerts = new List<string>();
        }

        public bool HasAlerts
        {
            get { return Alerts != null && Alerts.Count > 0; }
        }

        public string AlertText
        {
            get
            {
                if (!string.IsNullOrEmpty(ErrorMessage))
                    return ErrorMessage;
                if (Alerts == null || Alerts.Count == 0)
                    return "No dosimetric alerts. PVDR and volume fraction are within configured checks.";
                return string.Join("\n", Alerts.ToArray());
            }
        }
    }
}
