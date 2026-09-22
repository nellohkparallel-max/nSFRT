namespace SFRThelper.Models
{
    /// <summary>
    /// Commissioned linac / energy / dose-rate triple copied from existing beams.
    /// ViewModels bind this POCO; no VMS types.
    /// </summary>
    public class LinacEnergyOption
    {
        public string MachineId { get; set; }
        public string EnergyModeId { get; set; }
        public string EnergyModeDisplayName { get; set; }
        public string PrimaryFluenceMode { get; set; }
        public int DoseRate { get; set; }
        public string SourcePlanId { get; set; }
        public string Key { get; set; }

        public string Display
        {
            get
            {
                string fluence = string.IsNullOrEmpty(PrimaryFluenceMode) ? string.Empty : "-" + PrimaryFluenceMode;
                return (MachineId ?? "?") + "  " + (EnergyModeId ?? EnergyModeDisplayName ?? "?") + fluence
                    + "  @" + DoseRate + " MU/min";
            }
        }

        public override string ToString()
        {
            return Display;
        }
    }
}
