using System.ComponentModel;

namespace SFRThelper.Models
{
    /// <summary>
    /// One selected avoidance organ with clearance used for V_valid and optional PO Dmax.
    /// </summary>
    public class AvoidanceOarEntry : INotifyPropertyChanged
    {
        private string _structureId = StructureListItem.NoneId;
        private double _clearanceMm = SFRTParameters.DefaultOarClearanceMm;
        private double _doseLimitGy = 14.0;

        public string StructureId
        {
            get { return _structureId; }
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? StructureListItem.NoneId : value;
                if (_structureId == next)
                    return;
                _structureId = next;
                OnPropertyChanged(nameof(StructureId));
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public double ClearanceMm
        {
            get { return _clearanceMm; }
            set
            {
                if (NearlyEqual(_clearanceMm, value))
                    return;
                _clearanceMm = value;
                OnPropertyChanged(nameof(ClearanceMm));
            }
        }

        public double DoseLimitGy
        {
            get { return _doseLimitGy; }
            set
            {
                if (NearlyEqual(_doseLimitGy, value))
                    return;
                _doseLimitGy = value;
                OnPropertyChanged(nameof(DoseLimitGy));
            }
        }

        public bool IsSelected
        {
            get
            {
                return !string.IsNullOrWhiteSpace(StructureId)
                    && StructureId != StructureListItem.NoneId;
            }
        }

        public double ExpansionMm(double sphereRadiusMm)
        {
            return ClearanceMm + sphereRadiusMm;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }

        private static bool NearlyEqual(double a, double b)
        {
            return System.Math.Abs(a - b) < 1e-12;
        }
    }
}
