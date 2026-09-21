using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SFRThelper.Models
{
    public class SFRTParameters : INotifyPropertyChanged, INotifyDataErrorInfo, IDataErrorInfo
    {
        public const double DefaultOarClearanceMm = 10.0;
        public const double DefaultTargetClearanceMm = 5.0;
        public const double DefaultSphereRadiusMm = 5.0;
        public const double DefaultCenterSpacingMm = 15.0;
        public const double VolumeFractionMinPercent = 1.0;
        public const double VolumeFractionMaxPercent = 5.0;
        public const double PvdrWarningThreshold = 2.5;

        private double _sphereRadiusMm = DefaultSphereRadiusMm;
        private double _centerSpacingMm = DefaultCenterSpacingMm;
        private double _targetClearanceMm = DefaultTargetClearanceMm;
        private double _oar1ClearanceMm = DefaultOarClearanceMm;
        private double _oar2ClearanceMm = DefaultOarClearanceMm;
        private int _maxSphereCount = 500;
        private string _selectedTargetId;
        private string _oar1StructureId = StructureListItem.NoneId;
        private string _oar2StructureId = StructureListItem.NoneId;
        private PackingGeometryMode _packingMode = PackingGeometryMode.HexagonalClosePacking;
        private SphereGenerationMode _generationMode = SphereGenerationMode.IndividualAndComposite;
        private readonly Dictionary<string, List<string>> _errors = new Dictionary<string, List<string>>();

        public double SphereRadiusMm
        {
            get { return _sphereRadiusMm; }
            set
            {
                if (NearlyEqual(_sphereRadiusMm, value))
                    return;
                _sphereRadiusMm = value;
                OnPropertyChanged(nameof(SphereRadiusMm));
                OnPropertyChanged(nameof(SphereDiameterMm));
                ValidateAll();
            }
        }

        public double SphereDiameterMm
        {
            get { return _sphereRadiusMm * 2.0; }
            set { SphereRadiusMm = value / 2.0; }
        }

        public double CenterSpacingMm
        {
            get { return _centerSpacingMm; }
            set
            {
                if (NearlyEqual(_centerSpacingMm, value))
                    return;
                _centerSpacingMm = value;
                OnPropertyChanged(nameof(CenterSpacingMm));
                ValidateAll();
            }
        }

        /// <summary>Target internal clearance M_target (mm). Combined with radius to contract V_valid.</summary>
        public double TargetClearanceMm
        {
            get { return _targetClearanceMm; }
            set
            {
                if (NearlyEqual(_targetClearanceMm, value))
                    return;
                _targetClearanceMm = value;
                OnPropertyChanged(nameof(TargetClearanceMm));
                ValidateAll();
            }
        }

        /// <summary>Kept for compatibility with earlier builds; maps to <see cref="TargetClearanceMm"/>.</summary>
        public double BoundaryMarginMm
        {
            get { return TargetClearanceMm; }
            set { TargetClearanceMm = value; }
        }

        public double Oar1ClearanceMm
        {
            get { return _oar1ClearanceMm; }
            set
            {
                if (NearlyEqual(_oar1ClearanceMm, value))
                    return;
                _oar1ClearanceMm = value;
                OnPropertyChanged(nameof(Oar1ClearanceMm));
                ValidateAll();
            }
        }

        public double Oar2ClearanceMm
        {
            get { return _oar2ClearanceMm; }
            set
            {
                if (NearlyEqual(_oar2ClearanceMm, value))
                    return;
                _oar2ClearanceMm = value;
                OnPropertyChanged(nameof(Oar2ClearanceMm));
                ValidateAll();
            }
        }

        public string SelectedTargetId
        {
            get { return _selectedTargetId; }
            set
            {
                if (_selectedTargetId == value)
                    return;
                _selectedTargetId = value;
                OnPropertyChanged(nameof(SelectedTargetId));
                ValidateAll();
            }
        }

        /// <summary>Legacy alias used by older callers.</summary>
        public string SelectedPTVId
        {
            get { return SelectedTargetId; }
            set { SelectedTargetId = value; }
        }

        public string Oar1StructureId
        {
            get { return _oar1StructureId; }
            set
            {
                if (_oar1StructureId == value)
                    return;
                _oar1StructureId = value ?? StructureListItem.NoneId;
                OnPropertyChanged(nameof(Oar1StructureId));
                ValidateAll();
            }
        }

        public string Oar2StructureId
        {
            get { return _oar2StructureId; }
            set
            {
                if (_oar2StructureId == value)
                    return;
                _oar2StructureId = value ?? StructureListItem.NoneId;
                OnPropertyChanged(nameof(Oar2StructureId));
                ValidateAll();
            }
        }

        public PackingGeometryMode PackingMode
        {
            get { return _packingMode; }
            set
            {
                if (_packingMode == value)
                    return;
                _packingMode = value;
                OnPropertyChanged(nameof(PackingMode));
            }
        }

        public SphereGenerationMode GenerationMode
        {
            get { return _generationMode; }
            set
            {
                if (_generationMode == value)
                    return;
                _generationMode = value;
                OnPropertyChanged(nameof(GenerationMode));
            }
        }

        public int MaxSphereCount
        {
            get { return _maxSphereCount; }
            set
            {
                if (_maxSphereCount == value)
                    return;
                _maxSphereCount = value;
                OnPropertyChanged(nameof(MaxSphereCount));
                ValidateAll();
            }
        }

        /// <summary>Legacy alias: composite-only generation.</summary>
        public bool SingleStructureOnly
        {
            get { return GenerationMode == SphereGenerationMode.CompositeOnly; }
            set
            {
                GenerationMode = value
                    ? SphereGenerationMode.CompositeOnly
                    : SphereGenerationMode.IndividualAndComposite;
            }
        }

        public bool HasOar1
        {
            get { return !string.IsNullOrWhiteSpace(Oar1StructureId) && Oar1StructureId != StructureListItem.NoneId; }
        }

        public bool HasOar2
        {
            get { return !string.IsNullOrWhiteSpace(Oar2StructureId) && Oar2StructureId != StructureListItem.NoneId; }
        }

        public double TargetContractionMm
        {
            get { return SphereRadiusMm + TargetClearanceMm; }
        }

        public double Oar1ExpansionMm
        {
            get { return Oar1ClearanceMm + SphereRadiusMm; }
        }

        public double Oar2ExpansionMm
        {
            get { return Oar2ClearanceMm + SphereRadiusMm; }
        }

        public double EdgeToEdgeClearanceMm
        {
            get { return CenterSpacingMm - 2.0 * SphereRadiusMm; }
        }

        public bool IsValid
        {
            get { return !HasErrors; }
        }

        public SFRTParameters()
        {
            ValidateAll();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OnErrorsChanged(string propertyName)
        {
            EventHandler<DataErrorsChangedEventArgs> handler = ErrorsChanged;
            if (handler != null)
                handler(this, new DataErrorsChangedEventArgs(propertyName));
            OnPropertyChanged(nameof(IsValid));
            OnPropertyChanged(nameof(Error));
        }

        public bool HasErrors
        {
            get { return _errors.Count > 0; }
        }

        public IEnumerable GetErrors(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                return _errors.SelectMany(k => k.Value).ToList();

            List<string> list;
            if (_errors.TryGetValue(propertyName, out list))
                return list;
            return Enumerable.Empty<string>();
        }

        public string Error
        {
            get
            {
                if (!HasErrors)
                    return string.Empty;
                var sb = new StringBuilder();
                foreach (var pair in _errors)
                {
                    foreach (var message in pair.Value)
                        sb.AppendLine(message);
                }
                return sb.ToString().TrimEnd();
            }
        }

        public string this[string columnName]
        {
            get
            {
                List<string> list;
                if (_errors.TryGetValue(columnName, out list) && list.Count > 0)
                    return list[0];
                return string.Empty;
            }
        }

        public void ValidateAll()
        {
            ValidateProperty(nameof(SelectedTargetId), ValidateTarget);
            ValidateProperty(nameof(SphereRadiusMm), ValidateRadius);
            ValidateProperty(nameof(SphereDiameterMm), ValidateRadius);
            ValidateProperty(nameof(CenterSpacingMm), ValidateSpacing);
            ValidateProperty(nameof(TargetClearanceMm), () => ValidateNonNegative(TargetClearanceMm, "Target clearance"));
            ValidateProperty(nameof(Oar1ClearanceMm), () => ValidateNonNegative(Oar1ClearanceMm, "OAR 1 clearance"));
            ValidateProperty(nameof(Oar2ClearanceMm), () => ValidateNonNegative(Oar2ClearanceMm, "OAR 2 clearance"));
            ValidateProperty(nameof(MaxSphereCount), ValidateMaxCount);
            ValidateProperty(nameof(Oar1StructureId), ValidateOars);
            ValidateProperty(nameof(Oar2StructureId), ValidateOars);
        }

        private List<string> ValidateTarget()
        {
            if (string.IsNullOrWhiteSpace(SelectedTargetId))
                return new List<string> { "Select a target structure." };
            return null;
        }

        private List<string> ValidateRadius()
        {
            if (double.IsNaN(SphereRadiusMm) || SphereRadiusMm <= 0)
                return new List<string> { "Sphere radius / diameter must be greater than 0 mm." };
            return null;
        }

        private List<string> ValidateSpacing()
        {
            var errors = new List<string>();
            if (double.IsNaN(CenterSpacingMm) || CenterSpacingMm <= 0)
                errors.Add("Center-to-center distance must be greater than 0 mm.");
            if (CenterSpacingMm < 2.0 * SphereRadiusMm - 1e-6)
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture,
                    "Center-to-center distance must be ≥ 2 × radius ({0:F1} mm).",
                    2.0 * SphereRadiusMm));
            }
            return errors.Count == 0 ? null : errors;
        }

        private List<string> ValidateNonNegative(double value, string label)
        {
            if (double.IsNaN(value) || value < 0)
                return new List<string> { label + " must be ≥ 0 mm." };
            return null;
        }

        private List<string> ValidateMaxCount()
        {
            if (MaxSphereCount < 1)
                return new List<string> { "Maximum sphere count must be at least 1." };
            if (MaxSphereCount > 5000)
                return new List<string> { "Maximum sphere count is limited to 5000." };
            return null;
        }

        private List<string> ValidateOars()
        {
            var errors = new List<string>();
            if (HasOar1 && string.Equals(Oar1StructureId, SelectedTargetId, StringComparison.OrdinalIgnoreCase))
                errors.Add("OAR Avoidance 1 cannot be the target structure.");
            if (HasOar2 && string.Equals(Oar2StructureId, SelectedTargetId, StringComparison.OrdinalIgnoreCase))
                errors.Add("OAR Avoidance 2 cannot be the target structure.");
            if (HasOar1 && HasOar2 && string.Equals(Oar1StructureId, Oar2StructureId, StringComparison.OrdinalIgnoreCase))
                errors.Add("OAR Avoidance 1 and 2 must be different structures.");
            return errors.Count == 0 ? null : errors;
        }

        private void ValidateProperty(string propertyName, Func<List<string>> validator)
        {
            List<string> newErrors = validator();
            bool hadErrors = _errors.ContainsKey(propertyName);
            if (newErrors == null || newErrors.Count == 0)
            {
                if (hadErrors)
                {
                    _errors.Remove(propertyName);
                    OnErrorsChanged(propertyName);
                }
                return;
            }

            _errors[propertyName] = newErrors;
            OnErrorsChanged(propertyName);
        }

        private static bool NearlyEqual(double a, double b)
        {
            return Math.Abs(a - b) < 1e-12;
        }
    }
}
