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

        public const double DefaultExternalClearanceMm = 5.0;
        public const double DefaultGridRotationDeg = 45.0;
        public const double DoseGridWarningMm = 1.25;
        public const double OvermodulationMuPerGy = 400.0;

        private double _sphereRadiusMm = DefaultSphereRadiusMm;
        private double _centerSpacingMm = DefaultCenterSpacingMm;
        private double _lateralSpacingMm = DefaultCenterSpacingMm;
        private double _siSpacingMm = DefaultCenterSpacingMm;
        private double _targetClearanceMm = DefaultTargetClearanceMm;
        private double _oar1ClearanceMm = DefaultOarClearanceMm;
        private double _oar2ClearanceMm = DefaultOarClearanceMm;
        private double _externalClearanceMm = DefaultExternalClearanceMm;
        private double _gridRotationDeg = DefaultGridRotationDeg;
        private int _maxSphereCount = 500;
        private bool _enableSphereMaximization = true;
        private int _maxIterations = 100;
        private SphereMaximizationStrategy _optimizationStrategy = SphereMaximizationStrategy.RigidPhaseShift;
        private string _selectedTargetId;
        private string _oar1StructureId = StructureListItem.NoneId;
        private string _oar2StructureId = StructureListItem.NoneId;
        private PackingGeometryMode _packingMode = PackingGeometryMode.HexagonalClosePacking;
        private SphereGenerationMode _generationMode = SphereGenerationMode.IndividualAndComposite;
        private ClinicalProtocolPreset _preset = ClinicalProtocolPreset.Custom;
        private bool _isDirectionalSpacing;
        private bool _applyingPreset;
        private bool _generateTuningStructures = true;
        private double _penumbraShellThicknessMm = 3.0;
        private double _concentricRing1Mm = 10.0;
        private double _concentricRing2Mm = 30.0;
        private double _prescriptionDoseGy = 20.0;
        private int _fractionCount = 1;
        private PoObjectivePreset _poObjectivePreset = PoObjectivePreset.UniversityOfMiami;
        private bool _autoEnableJawTracking = true;
        private bool _clearExistingObjectives = true;
        private double _valleyUpperPercentOfRx = 30.0;
        private double _penumbraUpperPercentOfRx = 55.0;
        private double _oar1DoseLimitGy = 14.0;
        private double _oar2DoseLimitGy = 14.0;
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
                MarkCustomIfEdited();
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
                if (!_isDirectionalSpacing)
                {
                    _lateralSpacingMm = value;
                    _siSpacingMm = value;
                    OnPropertyChanged(nameof(LateralSpacingMm));
                    OnPropertyChanged(nameof(SiSpacingMm));
                }
                OnPropertyChanged(nameof(CenterSpacingMm));
                MarkCustomIfEdited();
                ValidateAll();
            }
        }

        public bool IsDirectionalSpacing
        {
            get { return _isDirectionalSpacing; }
            set
            {
                if (_isDirectionalSpacing == value)
                    return;
                _isDirectionalSpacing = value;
                if (!value)
                {
                    _lateralSpacingMm = _centerSpacingMm;
                    _siSpacingMm = _centerSpacingMm;
                    OnPropertyChanged(nameof(LateralSpacingMm));
                    OnPropertyChanged(nameof(SiSpacingMm));
                }
                OnPropertyChanged(nameof(IsDirectionalSpacing));
                OnPropertyChanged(nameof(IsUniversalSpacing));
                MarkCustomIfEdited();
                ValidateAll();
            }
        }

        public bool IsUniversalSpacing
        {
            get { return !_isDirectionalSpacing; }
        }

        public double LateralSpacingMm
        {
            get { return _lateralSpacingMm; }
            set
            {
                if (NearlyEqual(_lateralSpacingMm, value))
                    return;
                _lateralSpacingMm = value;
                OnPropertyChanged(nameof(LateralSpacingMm));
                MarkCustomIfEdited();
                ValidateAll();
            }
        }

        public double SiSpacingMm
        {
            get { return _siSpacingMm; }
            set
            {
                if (NearlyEqual(_siSpacingMm, value))
                    return;
                _siSpacingMm = value;
                OnPropertyChanged(nameof(SiSpacingMm));
                MarkCustomIfEdited();
                ValidateAll();
            }
        }

        public double EffectiveLateralSpacingMm
        {
            get { return _isDirectionalSpacing ? _lateralSpacingMm : _centerSpacingMm; }
        }

        public double EffectiveSiSpacingMm
        {
            get { return _isDirectionalSpacing ? _siSpacingMm : _centerSpacingMm; }
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
                OnPropertyChanged(nameof(TargetInternalMarginMm));
                MarkCustomIfEdited();
                ValidateAll();
            }
        }

        public double TargetInternalMarginMm
        {
            get { return TargetClearanceMm; }
            set { TargetClearanceMm = value; }
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
                OnPropertyChanged(nameof(AvoidanceOar1ClearanceMm));
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
                OnPropertyChanged(nameof(AvoidanceOar2ClearanceMm));
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
                OnPropertyChanged(nameof(AvoidanceOar1Id));
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
                OnPropertyChanged(nameof(AvoidanceOar2Id));
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
                MarkCustomIfEdited();
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

        public bool EnableSphereMaximization
        {
            get { return _enableSphereMaximization; }
            set
            {
                if (_enableSphereMaximization == value)
                    return;
                _enableSphereMaximization = value;
                OnPropertyChanged(nameof(EnableSphereMaximization));
            }
        }

        public int MaxIterations
        {
            get { return _maxIterations; }
            set
            {
                if (_maxIterations == value)
                    return;
                _maxIterations = value;
                OnPropertyChanged(nameof(MaxIterations));
                ValidateAll();
            }
        }

        public SphereMaximizationStrategy OptimizationStrategy
        {
            get { return _optimizationStrategy; }
            set
            {
                if (_optimizationStrategy == value)
                    return;
                _optimizationStrategy = value;
                OnPropertyChanged(nameof(OptimizationStrategy));
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

        public double ExternalBoundaryClearanceMm
        {
            get { return _externalClearanceMm; }
            set
            {
                if (NearlyEqual(_externalClearanceMm, value))
                    return;
                _externalClearanceMm = value;
                OnPropertyChanged(nameof(ExternalBoundaryClearanceMm));
                ValidateAll();
            }
        }

        public double SkinContractionMm
        {
            get { return SphereRadiusMm + ExternalBoundaryClearanceMm; }
        }

        public double GridRotationDeg
        {
            get { return _gridRotationDeg; }
            set
            {
                double clamped = value;
                if (clamped < 0) clamped = 0;
                if (clamped > 90) clamped = 90;
                if (NearlyEqual(_gridRotationDeg, clamped))
                    return;
                _gridRotationDeg = clamped;
                OnPropertyChanged(nameof(GridRotationDeg));
                MarkCustomIfEdited();
            }
        }

        public ClinicalProtocolPreset Preset
        {
            get { return _preset; }
            set
            {
                if (_preset == value)
                    return;
                _preset = value;
                OnPropertyChanged(nameof(Preset));
            }
        }

        public string AvoidanceOar1Id
        {
            get { return Oar1StructureId; }
            set { Oar1StructureId = value; }
        }

        public string AvoidanceOar2Id
        {
            get { return Oar2StructureId; }
            set { Oar2StructureId = value; }
        }

        public double AvoidanceOar1ClearanceMm
        {
            get { return Oar1ClearanceMm; }
            set { Oar1ClearanceMm = value; }
        }

        public double AvoidanceOar2ClearanceMm
        {
            get { return Oar2ClearanceMm; }
            set { Oar2ClearanceMm = value; }
        }

        public bool GenerateTuningStructures
        {
            get { return _generateTuningStructures; }
            set
            {
                if (_generateTuningStructures == value)
                    return;
                _generateTuningStructures = value;
                OnPropertyChanged(nameof(GenerateTuningStructures));
                ValidateAll();
            }
        }

        /// <summary>d_shell / d_margin for Peak_Penumbra and Valley_Core (mm). Default 3.0, range 1–5.</summary>
        public double PenumbraShellThicknessMm
        {
            get { return _penumbraShellThicknessMm; }
            set
            {
                if (NearlyEqual(_penumbraShellThicknessMm, value))
                    return;
                _penumbraShellThicknessMm = value;
                OnPropertyChanged(nameof(PenumbraShellThicknessMm));
                ValidateAll();
            }
        }

        public double ConcentricRing1Mm
        {
            get { return _concentricRing1Mm; }
            set
            {
                if (NearlyEqual(_concentricRing1Mm, value))
                    return;
                _concentricRing1Mm = value;
                OnPropertyChanged(nameof(ConcentricRing1Mm));
                ValidateAll();
            }
        }

        public double ConcentricRing2Mm
        {
            get { return _concentricRing2Mm; }
            set
            {
                if (NearlyEqual(_concentricRing2Mm, value))
                    return;
                _concentricRing2Mm = value;
                OnPropertyChanged(nameof(ConcentricRing2Mm));
                ValidateAll();
            }
        }

        public double PrescriptionDoseGy
        {
            get { return _prescriptionDoseGy; }
            set
            {
                if (NearlyEqual(_prescriptionDoseGy, value))
                    return;
                _prescriptionDoseGy = value;
                OnPropertyChanged(nameof(PrescriptionDoseGy));
                ValidateAll();
            }
        }

        public int FractionCount
        {
            get { return _fractionCount; }
            set
            {
                if (_fractionCount == value)
                    return;
                _fractionCount = value;
                OnPropertyChanged(nameof(FractionCount));
                ValidateAll();
            }
        }

        /// <summary>Legacy alias for <see cref="FractionCount"/>.</summary>
        public int Fractions
        {
            get { return FractionCount; }
            set { FractionCount = value; }
        }

        public PoObjectivePreset PoObjectivePreset
        {
            get { return _poObjectivePreset; }
            set
            {
                if (_poObjectivePreset == value)
                    return;
                _poObjectivePreset = value;
                OnPropertyChanged(nameof(PoObjectivePreset));
            }
        }

        public bool AutoEnableJawTracking
        {
            get { return _autoEnableJawTracking; }
            set
            {
                if (_autoEnableJawTracking == value)
                    return;
                _autoEnableJawTracking = value;
                OnPropertyChanged(nameof(AutoEnableJawTracking));
            }
        }

        public bool ClearExistingObjectives
        {
            get { return _clearExistingObjectives; }
            set
            {
                if (_clearExistingObjectives == value)
                    return;
                _clearExistingObjectives = value;
                OnPropertyChanged(nameof(ClearExistingObjectives));
                OnPropertyChanged(nameof(AppendObjectives));
            }
        }

        public bool AppendObjectives
        {
            get { return !_clearExistingObjectives; }
            set { ClearExistingObjectives = !value; }
        }

        public double ValleyUpperPercentOfRx
        {
            get { return _valleyUpperPercentOfRx; }
            set
            {
                if (NearlyEqual(_valleyUpperPercentOfRx, value))
                    return;
                _valleyUpperPercentOfRx = value;
                OnPropertyChanged(nameof(ValleyUpperPercentOfRx));
                MarkPoCustomIfEdited();
                ValidateAll();
            }
        }

        public double PenumbraUpperPercentOfRx
        {
            get { return _penumbraUpperPercentOfRx; }
            set
            {
                if (NearlyEqual(_penumbraUpperPercentOfRx, value))
                    return;
                _penumbraUpperPercentOfRx = value;
                OnPropertyChanged(nameof(PenumbraUpperPercentOfRx));
                MarkPoCustomIfEdited();
                ValidateAll();
            }
        }

        public double Oar1DoseLimitGy
        {
            get { return _oar1DoseLimitGy; }
            set
            {
                if (NearlyEqual(_oar1DoseLimitGy, value))
                    return;
                _oar1DoseLimitGy = value;
                OnPropertyChanged(nameof(Oar1DoseLimitGy));
                ValidateAll();
            }
        }

        public double Oar2DoseLimitGy
        {
            get { return _oar2DoseLimitGy; }
            set
            {
                if (NearlyEqual(_oar2DoseLimitGy, value))
                    return;
                _oar2DoseLimitGy = value;
                OnPropertyChanged(nameof(Oar2DoseLimitGy));
                ValidateAll();
            }
        }

        public void BeginPresetApply()
        {
            _applyingPreset = true;
        }

        public void EndPresetApply()
        {
            _applyingPreset = false;
            ValidateAll();
            OnPropertyChanged(nameof(Preset));
            OnPropertyChanged(nameof(PoObjectivePreset));
        }

        public double EdgeToEdgeClearanceMm
        {
            get { return EffectiveLateralSpacingMm - 2.0 * SphereRadiusMm; }
        }

        public double EdgeToEdgeSiClearanceMm
        {
            get { return EffectiveSiSpacingMm - 2.0 * SphereRadiusMm; }
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
            ValidateProperty(nameof(LateralSpacingMm), ValidateSpacing);
            ValidateProperty(nameof(SiSpacingMm), ValidateSpacing);
            ValidateProperty(nameof(TargetClearanceMm), () => ValidateNonNegative(TargetClearanceMm, "Target clearance"));
            ValidateProperty(nameof(ExternalBoundaryClearanceMm), () => ValidateNonNegative(ExternalBoundaryClearanceMm, "External/skin clearance"));
            ValidateProperty(nameof(Oar1ClearanceMm), () => ValidateNonNegative(Oar1ClearanceMm, "OAR 1 clearance"));
            ValidateProperty(nameof(Oar2ClearanceMm), () => ValidateNonNegative(Oar2ClearanceMm, "OAR 2 clearance"));
            ValidateProperty(nameof(GridRotationDeg), ValidateRotation);
            ValidateProperty(nameof(MaxSphereCount), ValidateMaxCount);
            ValidateProperty(nameof(MaxIterations), ValidateMaxIterations);
            ValidateProperty(nameof(Oar1StructureId), ValidateOars);
            ValidateProperty(nameof(Oar2StructureId), ValidateOars);
            ValidateProperty(nameof(PenumbraShellThicknessMm), ValidatePenumbraShell);
            ValidateProperty(nameof(ConcentricRing1Mm), ValidateRings);
            ValidateProperty(nameof(ConcentricRing2Mm), ValidateRings);
            ValidateProperty(nameof(PrescriptionDoseGy), ValidatePrescription);
            ValidateProperty(nameof(FractionCount), ValidateFractions);
            ValidateProperty(nameof(Oar1DoseLimitGy), () => ValidateNonNegative(Oar1DoseLimitGy, "OAR 1 Dmax"));
            ValidateProperty(nameof(Oar2DoseLimitGy), () => ValidateNonNegative(Oar2DoseLimitGy, "OAR 2 Dmax"));
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
            double dxy = EffectiveLateralSpacingMm;
            double dz = EffectiveSiSpacingMm;
            double min = 2.0 * SphereRadiusMm;
            if (double.IsNaN(dxy) || dxy <= 0 || double.IsNaN(dz) || dz <= 0)
                errors.Add("Center-to-center distance must be greater than 0 mm.");
            if (dxy < min - 1e-6)
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture,
                    "Lateral spacing must be ≥ 2 × radius ({0:F1} mm).", min));
            }
            if (dz < min - 1e-6)
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture,
                    "SI spacing must be ≥ 2 × radius ({0:F1} mm).", min));
            }
            return errors.Count == 0 ? null : errors;
        }

        private List<string> ValidateRotation()
        {
            if (double.IsNaN(GridRotationDeg) || GridRotationDeg < 0 || GridRotationDeg > 90)
                return new List<string> { "Grid rotation must be between 0° and 90°." };
            return null;
        }

        private void MarkCustomIfEdited()
        {
            if (_applyingPreset)
                return;
            if (_preset != ClinicalProtocolPreset.Custom)
            {
                _preset = ClinicalProtocolPreset.Custom;
                OnPropertyChanged(nameof(Preset));
            }
        }

        private void MarkPoCustomIfEdited()
        {
            if (_applyingPreset)
                return;
            if (_poObjectivePreset != PoObjectivePreset.Custom)
            {
                _poObjectivePreset = PoObjectivePreset.Custom;
                OnPropertyChanged(nameof(PoObjectivePreset));
            }
        }

        private List<string> ValidatePenumbraShell()
        {
            if (double.IsNaN(PenumbraShellThicknessMm) || PenumbraShellThicknessMm < 1.0 - 1e-9 || PenumbraShellThicknessMm > 5.0 + 1e-9)
                return new List<string> { "Penumbra shell thickness must be between 1.0 and 5.0 mm." };
            return null;
        }

        private List<string> ValidateRings()
        {
            var errors = new List<string>();
            if (double.IsNaN(ConcentricRing1Mm) || ConcentricRing1Mm <= 0)
                errors.Add("Inner concentric ring radius must be greater than 0 mm.");
            if (double.IsNaN(ConcentricRing2Mm) || ConcentricRing2Mm <= 0)
                errors.Add("Outer concentric ring radius must be greater than 0 mm.");
            if (ConcentricRing2Mm <= ConcentricRing1Mm + 1e-6)
                errors.Add("Outer ring (Ring 2) must be larger than inner ring (Ring 1).");
            return errors.Count == 0 ? null : errors;
        }

        private List<string> ValidatePrescription()
        {
            if (double.IsNaN(PrescriptionDoseGy) || PrescriptionDoseGy <= 0)
                return new List<string> { "Prescription dose (D_rx) must be greater than 0 Gy." };
            return null;
        }

        private List<string> ValidateFractions()
        {
            if (FractionCount < 1)
                return new List<string> { "Fraction count (N_fx) must be at least 1." };
            return null;
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

        private List<string> ValidateMaxIterations()
        {
            if (MaxIterations < 10)
                return new List<string> { "Max iterations must be at least 10." };
            if (MaxIterations > 1000)
                return new List<string> { "Max iterations is limited to 1000." };
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
