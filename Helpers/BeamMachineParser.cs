using System;
using SFRThelper.Models;

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

        /// <summary>
        /// Plan automation is restricted to 10X-FFF (any machine / dose rate).
        /// </summary>
        public static bool IsTenXFff(string energyModeId, string primaryFluenceMode, string energyModeDisplayName)
        {
            string energy = energyModeId ?? string.Empty;
            string fluence = primaryFluenceMode ?? string.Empty;
            string display = energyModeDisplayName ?? string.Empty;

            if (string.Equals(energy, "10X", StringComparison.OrdinalIgnoreCase)
                && string.Equals(fluence, "FFF", StringComparison.OrdinalIgnoreCase))
                return true;

            if (display.IndexOf("10X-FFF", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (string.Equals(energy, "10X-FFF", StringComparison.OrdinalIgnoreCase))
                return true;

            string splitEnergy;
            string splitFluence;
            SplitEnergyMode(display, out splitEnergy, out splitFluence);
            return string.Equals(splitEnergy, "10X", StringComparison.OrdinalIgnoreCase)
                && string.Equals(splitFluence, "FFF", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsTenXFff(LinacEnergyOption option)
        {
            if (option == null)
                return false;
            return IsTenXFff(option.EnergyModeId, option.PrimaryFluenceMode, option.EnergyModeDisplayName);
        }
    }
}
