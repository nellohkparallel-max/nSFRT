using System;

namespace SFRThelper.Helpers
{
    /// <summary>
    /// Splits Eclipse EnergyModeDisplayName values such as "6X-FFF" into
    /// energyModeId + primaryFluenceMode for ExternalBeamMachineParameters.
    /// The 5th constructor argument is fluence (not MLC id); MLC stays null so ESAPI
    /// binds the machine's default commissioned MLC.
    /// </summary>
    public static class BeamMachineParser
    {
        public static void SplitEnergyMode(string displayName, out string energyModeId, out string primaryFluenceMode)
        {
            energyModeId = displayName ?? string.Empty;
            primaryFluenceMode = null;
            if (string.IsNullOrWhiteSpace(displayName))
                return;

            int dash = displayName.LastIndexOf('-');
            if (dash <= 0 || dash >= displayName.Length - 1)
                return;

            string suffix = displayName.Substring(dash + 1);
            if (IsFluenceSuffix(suffix))
            {
                energyModeId = displayName.Substring(0, dash);
                primaryFluenceMode = suffix;
            }
        }

        public static bool IsFluenceSuffix(string suffix)
        {
            if (string.IsNullOrEmpty(suffix))
                return false;
            return string.Equals(suffix, "FFF", StringComparison.OrdinalIgnoreCase)
                || string.Equals(suffix, "SRS", StringComparison.OrdinalIgnoreCase)
                || string.Equals(suffix, "EEE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(suffix, "FILTERFREE", StringComparison.OrdinalIgnoreCase);
        }

        public static string OptionKey(string machineId, string energyMode, int doseRate, string fluence)
        {
            return (machineId ?? string.Empty) + "|"
                + (energyMode ?? string.Empty) + "|"
                + doseRate.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|"
                + (fluence ?? string.Empty);
        }
    }
}
