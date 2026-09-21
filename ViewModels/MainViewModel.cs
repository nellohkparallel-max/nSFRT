using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SFRThelper.Helpers;
using SFRThelper.Models;
using SFRThelper.Services;

namespace SFRThelper.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IESAPIService _esapi;
        private readonly SphereOptimizer _sphereOptimizer;
        private readonly SFRTEvaluationService _evaluationService;
        private readonly PlanAutomationService _automation;

        private bool _isBusy;
        private string _statusMessage = "Ready";
        private double _workProgress;
        private int _currentSliceIndex;
        private double _viewerWidth = 500;
        private double _viewerHeight = 500;
        private SphereModel _selectedSphere;
        private LatticeGeometryContext _geometry;
        private ImageGeometryDto _image;
        private FeasibilitySummary _feasibility = new FeasibilitySummary();
        private PlanListItem _selectedPlan;
        private SFRTEvaluationResult _evaluation = new SFRTEvaluationResult();
        private CancellationTokenSource _cts;
        private double _viewMinX;
        private double _viewMinY;
        private double _viewScale = 1.0;
        private const double ViewPadding = 20.0;

        public SFRTParameters Parameters { get; private set; }
        public ObservableCollection<StructureListItem> AvailableTargets { get; private set; }
        public ObservableCollection<StructureListItem> AvailableOARs { get; private set; }
        public ObservableCollection<SphereModel> GeneratedSpheres { get; private set; }
        public ObservableCollection<PointCollection> CurrentContours { get; private set; }
        public ObservableCollection<AxialSphereVisual> AxialSpheres { get; private set; }
        public ObservableCollection<PlanListItem> AvailablePlans { get; private set; }
        public ObservableCollection<EvaluationMetricRow> EvaluationMetrics { get; private set; }
        public ObservableCollection<PeakDoseRow> IndividualPeakDoses { get; private set; }
        public ObservableCollection<DoseGradientRow> GradientRows { get; private set; }
        public IReadOnlyList<PackingOption> PackingOptions { get; private set; }
        public IReadOnlyList<GenerationModeOption> GenerationModeOptions { get; private set; }
        public IReadOnlyList<ProtocolOption> ProtocolOptions { get; private set; }
        public IReadOnlyList<SpacingModeOption> SpacingModeOptions { get; private set; }

        public MainViewModel(IESAPIService esapi)
        {
            if (esapi == null)
                throw new ArgumentNullException("esapi");

            _esapi = esapi;
            _sphereOptimizer = new SphereOptimizer();
            _evaluationService = new SFRTEvaluationService(esapi);
            _automation = new PlanAutomationService(esapi);

            Parameters = new SFRTParameters();
            AvailableTargets = new ObservableCollection<StructureListItem>();
            AvailableOARs = new ObservableCollection<StructureListItem>();
            GeneratedSpheres = new ObservableCollection<SphereModel>();
            CurrentContours = new ObservableCollection<PointCollection>();
            AxialSpheres = new ObservableCollection<AxialSphereVisual>();
            AvailablePlans = new ObservableCollection<PlanListItem>();
            EvaluationMetrics = new ObservableCollection<EvaluationMetricRow>();
            IndividualPeakDoses = new ObservableCollection<PeakDoseRow>();
            GradientRows = new ObservableCollection<DoseGradientRow>();

            PackingOptions = new[]
            {
                new PackingOption { Value = PackingGeometryMode.SimpleCubic, Display = "Simple Cubic" },
                new PackingOption { Value = PackingGeometryMode.FaceCenteredCubic, Display = "Face-Centered Cubic (FCC)" },
                new PackingOption { Value = PackingGeometryMode.HexagonalClosePacking, Display = "Hexagonal Close Packing (HCP)" }
            };
            GenerationModeOptions = new[]
            {
                new GenerationModeOption { Value = SphereGenerationMode.IndividualAndComposite, Display = "Individual Peak_xx + Lattice_Peaks" },
                new GenerationModeOption { Value = SphereGenerationMode.CompositeOnly, Display = "Composite Lattice_Peaks only" }
            };
            ProtocolOptions = SFRTProtocolPresets.All.Select(p => new ProtocolOption
            {
                Value = p.Id,
                Display = p.DisplayName,
                Description = p.Description
            }).ToList();
            SpacingModeOptions = new[]
            {
                new SpacingModeOption { IsDirectional = false, Display = "Universal (dx = dy = dz)" },
                new SpacingModeOption { IsDirectional = true, Display = "Directional (dxy ≠ dSI)" }
            };

            PreviewLatticeCommand = new AsyncRelayCommand(PreviewLatticeAsync, () => CanPreview);
            GenerateStructuresCommand = new AsyncRelayCommand(GenerateSpheresAndRingsAsync, () => CanGenerateSpheresAndRings);
            CancelCommand = new RelayCommand(CancelWork, () => IsBusy);
            SliceUpCommand = new RelayCommand(MoveSliceUp, () => !IsBusy);
            SliceDownCommand = new RelayCommand(MoveSliceDown, () => !IsBusy);
            RegenerateRemainingCommand = new AsyncRelayCommand(RegenerateRemainingAsync, () => CanRegenerateRemaining);
            EvaluatePlanCommand = new AsyncRelayCommand(EvaluatePlanAsync, () => CanEvaluate);
            RefreshPlansCommand = new RelayCommand(LoadPlans);
            SeedObjectivesCommand = new AsyncRelayCommand(SeedObjectivesAsync, () => CanAutomate);
            SetupVmatArcsCommand = new AsyncRelayCommand(SetupVmatArcsAsync, () => CanAutomate);
            ExportQaReportCommand = new RelayCommand(ExportQaReport, () => CanExportQa);

            Parameters.PropertyChanged += Parameters_PropertyChanged;
            Parameters.ErrorsChanged += Parameters_ErrorsChanged;
            GeneratedSpheres.CollectionChanged += GeneratedSpheres_CollectionChanged;

            _image = CallEsapi(() => _esapi.GetImageGeometry());
            LoadCatalogs();
            RefreshLocalFeasibility();
            StatusMessage = CallEsapi(() => _esapi.GetPatientStatus());
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            set
            {
                if (!SetProperty(ref _isBusy, value, nameof(IsBusy)))
                    return;
                OnPropertyChanged(nameof(CanPreview));
                OnPropertyChanged(nameof(CanCreateStructures));
                OnPropertyChanged(nameof(CanGenerateSpheresAndRings));
                OnPropertyChanged(nameof(CanRegenerateRemaining));
                OnPropertyChanged(nameof(CanEvaluate));
                OnPropertyChanged(nameof(CanAutomate));
                OnPropertyChanged(nameof(CanExportQa));
                OnPropertyChanged(nameof(IsIdle));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsIdle
        {
            get { return !IsBusy; }
        }

        public double WorkProgress
        {
            get { return _workProgress; }
            set { SetProperty(ref _workProgress, value, nameof(WorkProgress)); }
        }

        public bool CanPreview
        {
            get
            {
                return !IsBusy
                    && Parameters.IsValid
                    && !string.IsNullOrEmpty(Parameters.SelectedTargetId);
            }
        }

        public bool CanCreateStructures
        {
            get { return !IsBusy && GeneratedSpheres.Count > 0 && Parameters.IsValid; }
        }

        public bool CanGenerateSpheresAndRings
        {
            get { return !IsBusy && Parameters.IsValid && !string.IsNullOrEmpty(Parameters.SelectedTargetId); }
        }

        public bool CanRegenerateRemaining
        {
            get { return !IsBusy && GeneratedSpheres.Any(s => s.IsEdited); }
        }

        public bool CanEvaluate
        {
            get { return !IsBusy && SelectedPlan != null && SelectedPlan.IsDoseValid; }
        }

        public bool CanAutomate
        {
            get { return !IsBusy; }
        }

        public bool CanExportQa
        {
            get { return !IsBusy && Evaluation != null && Evaluation.Success; }
        }

        public bool IsPlanCalculated
        {
            get { return SelectedPlan != null && SelectedPlan.IsDoseValid; }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            set { SetProperty(ref _statusMessage, value, nameof(StatusMessage)); }
        }

        public ClinicalProtocolPreset SelectedPreset
        {
            get { return Parameters.Preset; }
            set
            {
                if (Parameters.Preset == value)
                    return;
                if (value != ClinicalProtocolPreset.Custom)
                    SFRTProtocolPresets.Apply(Parameters, value);
                else
                    Parameters.Preset = ClinicalProtocolPreset.Custom;
                OnPropertyChanged(nameof(SelectedPreset));
                OnPropertyChanged(nameof(PresetDescription));
                InvalidateGeometry();
                RefreshLocalFeasibility();
            }
        }

        public string PresetDescription
        {
            get
            {
                SFRTProtocolPreset preset = SFRTProtocolPresets.Find(Parameters.Preset);
                return preset != null ? preset.Description : string.Empty;
            }
        }

        public bool SelectedDirectionalSpacing
        {
            get { return Parameters.IsDirectionalSpacing; }
            set
            {
                Parameters.IsDirectionalSpacing = value;
                OnPropertyChanged(nameof(SelectedDirectionalSpacing));
            }
        }

        public FeasibilitySummary Feasibility
        {
            get { return _feasibility; }
            set
            {
                _feasibility = value ?? new FeasibilitySummary();
                OnPropertyChanged(nameof(Feasibility));
                OnPropertyChanged(nameof(FeasibilityText));
            }
        }

        public string FeasibilityText
        {
            get { return Feasibility != null ? Feasibility.SummaryText : string.Empty; }
        }

        public int CurrentSliceIndex
        {
            get { return _currentSliceIndex; }
            set
            {
                int zSize = _image != null ? Math.Max(0, _image.ZSize - 1) : 0;
                int clamped = Math.Max(0, Math.Min(value, zSize));
                if (!SetProperty(ref _currentSliceIndex, clamped, nameof(CurrentSliceIndex)))
                    return;
                OnPropertyChanged(nameof(CurrentSliceText));
                RefreshViewer();
            }
        }

        public string CurrentSliceText
        {
            get
            {
                double z = _image != null ? _image.GetSliceZ(CurrentSliceIndex) : 0;
                return "Slice " + CurrentSliceIndex + "  (z = " + z.ToString("F1") + " mm)";
            }
        }

        public SphereModel SelectedSphere
        {
            get { return _selectedSphere; }
            set { SetProperty(ref _selectedSphere, value, nameof(SelectedSphere)); }
        }

        public PlanListItem SelectedPlan
        {
            get { return _selectedPlan; }
            set
            {
                if (!SetProperty(ref _selectedPlan, value, nameof(SelectedPlan)))
                    return;
                OnPropertyChanged(nameof(CanEvaluate));
                OnPropertyChanged(nameof(IsPlanCalculated));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public SFRTEvaluationResult Evaluation
        {
            get { return _evaluation; }
            set
            {
                _evaluation = value ?? new SFRTEvaluationResult();
                OnPropertyChanged(nameof(Evaluation));
                OnPropertyChanged(nameof(EvaluationAlertText));
                OnPropertyChanged(nameof(HasEvaluationAlerts));
                OnPropertyChanged(nameof(DoseGridBadge));
                OnPropertyChanged(nameof(VolumeFractionBadge));
                OnPropertyChanged(nameof(OvermodulationBadge));
                OnPropertyChanged(nameof(MeanGradientText));
                OnPropertyChanged(nameof(CanExportQa));
            }
        }

        public string EvaluationAlertText
        {
            get { return Evaluation != null ? Evaluation.AlertText : string.Empty; }
        }

        public bool HasEvaluationAlerts
        {
            get { return Evaluation != null && (Evaluation.HasAlerts || !string.IsNullOrEmpty(Evaluation.ErrorMessage)); }
        }

        public string DoseGridBadge
        {
            get
            {
                if (Evaluation == null || Evaluation.DoseGridMaxMm <= 0)
                    return "Dose grid: n/a";
                return Evaluation.DoseGridCoarse
                    ? "Dose grid: " + Evaluation.DoseGridMaxMm.ToString("F2") + " mm  (coarse > 1.25 mm)"
                    : "Dose grid: " + Evaluation.DoseGridMaxMm.ToString("F2") + " mm  (OK)";
            }
        }

        public string VolumeFractionBadge
        {
            get
            {
                if (Evaluation == null || double.IsNaN(Evaluation.VolumeFractionPercent))
                    return "Volume fraction: n/a";
                string flag = SfrtMetricsCalculator.IsVolumeFractionOutOfRange(Evaluation.VolumeFractionPercent)
                    ? "outside 1–5%" : "ideal 1–5%";
                return "Volume fraction: " + SfrtMetricsCalculator.FormatPercent(Evaluation.VolumeFractionPercent) + "  (" + flag + ")";
            }
        }

        public string OvermodulationBadge
        {
            get
            {
                if (Evaluation == null || double.IsNaN(Evaluation.MuPerGy))
                    return "MU/Gy: n/a";
                return Evaluation.Overmodulated
                    ? "MU/Gy: " + SfrtMetricsCalculator.FormatRatio(Evaluation.MuPerGy) + "  (over-modulated)"
                    : "MU/Gy: " + SfrtMetricsCalculator.FormatRatio(Evaluation.MuPerGy) + "  (OK)";
            }
        }

        public string MeanGradientText
        {
            get
            {
                if (Evaluation == null)
                    return string.Empty;
                return "Mean peak-to-valley gradient: "
                    + SfrtMetricsCalculator.FormatGyPerMm(Evaluation.MeanGradientGyPerMm)
                    + "    Min trough: "
                    + SfrtMetricsCalculator.FormatGy(Evaluation.MinValleyTroughGy);
            }
        }

        public ICommand PreviewLatticeCommand { get; private set; }
        public ICommand GenerateStructuresCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand SliceUpCommand { get; private set; }
        public ICommand SliceDownCommand { get; private set; }
        public ICommand RegenerateRemainingCommand { get; private set; }
        public ICommand EvaluatePlanCommand { get; private set; }
        public ICommand RefreshPlansCommand { get; private set; }
        public ICommand SeedObjectivesCommand { get; private set; }
        public ICommand SetupVmatArcsCommand { get; private set; }
        public ICommand ExportQaReportCommand { get; private set; }

        private void LoadCatalogs()
        {
            AvailableTargets.Clear();
            foreach (var item in CallEsapi(() => _esapi.GetTargetStructures()))
                AvailableTargets.Add(item);

            AvailableOARs.Clear();
            foreach (var item in CallEsapi(() => _esapi.GetAvoidanceStructures()))
                AvailableOARs.Add(item);

            if (AvailableTargets.Count > 0 && string.IsNullOrEmpty(Parameters.SelectedTargetId))
                Parameters.SelectedTargetId = AvailableTargets[0].Id;

            LoadPlans();
            OnTargetChanged();
        }

        private void LoadPlans()
        {
            string previousKey = SelectedPlan != null ? SelectedPlan.Key : null;
            AvailablePlans.Clear();
            foreach (var plan in CallEsapi(() => _esapi.GetEvaluablePlans()))
                AvailablePlans.Add(plan);

            PlanListItem match = AvailablePlans.FirstOrDefault(p => p.Key == previousKey)
                ?? AvailablePlans.FirstOrDefault(p => p.IsDoseValid)
                ?? AvailablePlans.FirstOrDefault();
            SelectedPlan = match;
        }

        private void Parameters_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CanPreview));
            OnPropertyChanged(nameof(CanCreateStructures));
            OnPropertyChanged(nameof(CanGenerateSpheresAndRings));
            if (e.PropertyName == nameof(SFRTParameters.Preset))
            {
                OnPropertyChanged(nameof(SelectedPreset));
                OnPropertyChanged(nameof(PresetDescription));
            }
            if (e.PropertyName == nameof(SFRTParameters.IsDirectionalSpacing))
                OnPropertyChanged(nameof(SelectedDirectionalSpacing));
            CommandManager.InvalidateRequerySuggested();

            if (e.PropertyName == nameof(SFRTParameters.SelectedTargetId))
            {
                OnTargetChanged();
                InvalidateGeometry();
                RefreshLocalFeasibility();
                return;
            }

            if (e.PropertyName == nameof(SFRTParameters.Oar1StructureId)
                || e.PropertyName == nameof(SFRTParameters.Oar2StructureId)
                || e.PropertyName == nameof(SFRTParameters.SphereRadiusMm)
                || e.PropertyName == nameof(SFRTParameters.SphereDiameterMm)
                || e.PropertyName == nameof(SFRTParameters.TargetClearanceMm)
                || e.PropertyName == nameof(SFRTParameters.Oar1ClearanceMm)
                || e.PropertyName == nameof(SFRTParameters.Oar2ClearanceMm)
                || e.PropertyName == nameof(SFRTParameters.ExternalBoundaryClearanceMm)
                || e.PropertyName == nameof(SFRTParameters.CenterSpacingMm)
                || e.PropertyName == nameof(SFRTParameters.LateralSpacingMm)
                || e.PropertyName == nameof(SFRTParameters.SiSpacingMm)
                || e.PropertyName == nameof(SFRTParameters.IsDirectionalSpacing)
                || e.PropertyName == nameof(SFRTParameters.PackingMode)
                || e.PropertyName == nameof(SFRTParameters.GridRotationDeg)
                || e.PropertyName == nameof(SFRTParameters.MaxSphereCount))
            {
                InvalidateGeometry();
                RefreshLocalFeasibility();
            }
        }

        private void Parameters_ErrorsChanged(object sender, DataErrorsChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CanPreview));
            OnPropertyChanged(nameof(CanCreateStructures));
            OnPropertyChanged(nameof(CanGenerateSpheresAndRings));
            CommandManager.InvalidateRequerySuggested();
        }

        private void GeneratedSpheres_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CanCreateStructures));
            OnPropertyChanged(nameof(CanGenerateSpheresAndRings));
            OnPropertyChanged(nameof(CanRegenerateRemaining));
            RefreshViewer();
        }

        private void InvalidateGeometry()
        {
            _geometry = null;
        }

        private void OnTargetChanged()
        {
            var info = CallEsapi(() => _esapi.GetStructureInfo(Parameters.SelectedTargetId));
            if (info == null || info.IsEmpty)
                return;
            _image = CallEsapi(() => _esapi.GetImageGeometry());
            RefreshViewer();
        }

        private void RefreshLocalFeasibility()
        {
            int count = GeneratedSpheres.Count;
            Feasibility = SfrtMetricsCalculator.BuildFeasibility(_geometry, Parameters, count);
            OnPropertyChanged(nameof(FeasibilityText));
        }

        private async Task PreviewLatticeAsync()
        {
            await RunLatticeAsync(false).ConfigureAwait(true);
        }

        private async Task RegenerateRemainingAsync()
        {
            await RunLatticeAsync(true).ConfigureAwait(true);
        }

        private async Task GenerateSpheresAndRingsAsync()
        {
            if (GeneratedSpheres.Count == 0)
            {
                await RunLatticeAsync(false).ConfigureAwait(true);
                if (GeneratedSpheres.Count == 0)
                    return;
            }
            await GenerateStructuresAsync().ConfigureAwait(true);
        }

        private async Task RunLatticeAsync(bool keepEdited)
        {
            if (!Parameters.IsValid)
            {
                StatusMessage = Parameters.Error;
                return;
            }

            IsBusy = true;
            WorkProgress = 0;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var progress = new Progress<string>(m => StatusMessage = m);
            var packProgress = new Progress<double>(p =>
            {
                WorkProgress = 35.0 + Math.Max(0, Math.Min(1, p)) * 55.0;
            });
            try
            {
                List<SphereModel> fixedSpheres = keepEdited
                    ? GeneratedSpheres.Where(s => s.IsEdited).ToList()
                    : null;

                StatusMessage = "Phase 1: extracting V_valid on the ESAPI thread...";
                WorkProgress = 10;
                SFRTParameters parameters = Parameters;
                LatticeGeometryContext geometry = await CallEsapiAsync(
                    () => _esapi.ExtractLatticeGeometry(parameters, progress, token), token).ConfigureAwait(true);
                token.ThrowIfCancellationRequested();
                _geometry = geometry;
                WorkProgress = 35;
                if (!geometry.IsValid)
                {
                    GeneratedSpheres.Clear();
                    Feasibility = SfrtMetricsCalculator.BuildFeasibility(geometry, Parameters, 0);
                    StatusMessage = geometry.Message;
                    return;
                }

                StatusMessage = "Phase 2: packing lattice on a worker thread...";
                LatticePackingResult packing = await Task.Run(() =>
                    _sphereOptimizer.GenerateLattice(geometry, Parameters, fixedSpheres, token, packProgress), token).ConfigureAwait(true);

                GeneratedSpheres.Clear();
                foreach (var sphere in packing.Spheres)
                    GeneratedSpheres.Add(sphere);

                Feasibility = SfrtMetricsCalculator.BuildFeasibility(geometry, Parameters, packing.SphereCount);
                OnPropertyChanged(nameof(FeasibilityText));
                StatusMessage = packing.Message;
                WorkProgress = 90;

                if (_geometry != null && _image != null)
                    CurrentSliceIndex = _image.GetNearestSliceIndex(_geometry.CenterOfMass.Z);
                RefreshViewer();
                WorkProgress = 100;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Lattice generation was cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error during lattice generation: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                DisposeCts();
            }
        }

        private async Task GenerateStructuresAsync()
        {
            if (GeneratedSpheres.Count == 0)
            {
                StatusMessage = "Preview a lattice before generating structures.";
                return;
            }

            string reason = null;
            bool canModify = CallEsapi(() =>
            {
                string r;
                bool ok = _esapi.CanModifyStructureSet(out r);
                reason = r;
                return ok;
            });
            if (!canModify)
            {
                StatusMessage = reason;
                return;
            }

            IsBusy = true;
            WorkProgress = 0;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var progress = new Progress<string>(m => StatusMessage = m);
            try
            {
                StatusMessage = "Phase 3: committing Peak / Lattice_Peaks / Lattice_Valley / rings...";
                var spheres = GeneratedSpheres.ToList();
                SFRTParameters parameters = Parameters;
                WorkProgress = 20;
                StructureCreationResult result = await CallEsapiAsync(
                    () => _esapi.CreateLatticeStructures(spheres, parameters, progress, token), token).ConfigureAwait(true);
                StatusMessage = result.Message;
                WorkProgress = 90;
                LoadCatalogs();
                WorkProgress = 100;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Structure commit was cancelled and rolled back.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error creating structures: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                DisposeCts();
            }
        }

        private async Task EvaluatePlanAsync()
        {
            if (!CanEvaluate)
            {
                StatusMessage = "Select a calculated plan or plan sum.";
                return;
            }

            IsBusy = true;
            WorkProgress = 20;
            try
            {
                StatusMessage = "Evaluating SFRT dosimetry...";
                string key = SelectedPlan.Key;
                SFRTParameters parameters = Parameters;
                SFRTEvaluationResult result = await CallEsapiAsync(
                    () => _evaluationService.Evaluate(key, parameters), CancellationToken.None).ConfigureAwait(true);

                Evaluation = result;
                EvaluationMetrics.Clear();
                foreach (var row in result.MetricRows)
                    EvaluationMetrics.Add(row);
                IndividualPeakDoses.Clear();
                foreach (var peak in result.IndividualPeaks)
                    IndividualPeakDoses.Add(peak);
                GradientRows.Clear();
                foreach (var g in result.Gradients)
                    GradientRows.Add(g);

                StatusMessage = result.Success
                    ? "Evaluation complete. PVDR_mean = " + SfrtMetricsCalculator.FormatRatio(result.PvdrMean)
                    : result.ErrorMessage;
                OnPropertyChanged(nameof(EvaluationAlertText));
                OnPropertyChanged(nameof(HasEvaluationAlerts));
                WorkProgress = 100;
            }
            catch (Exception ex)
            {
                StatusMessage = "Evaluation failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SeedObjectivesAsync()
        {
            IsBusy = true;
            try
            {
                StatusMessage = "Seeding Photon Optimizer objectives...";
                SFRTParameters parameters = Parameters;
                string message = await CallEsapiAsync(() => _automation.SeedPhotonObjectives(parameters), CancellationToken.None)
                    .ConfigureAwait(true);
                StatusMessage = message;
            }
            catch (Exception ex)
            {
                StatusMessage = "PO seeding failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SetupVmatArcsAsync()
        {
            IsBusy = true;
            try
            {
                StatusMessage = "Adding 2-arc coplanar VMAT template...";
                string message = await CallEsapiAsync(() => _automation.SetupVmatArcs(), CancellationToken.None)
                    .ConfigureAwait(true);
                StatusMessage = message;
            }
            catch (Exception ex)
            {
                StatusMessage = "VMAT setup failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExportQaReport()
        {
            if (Evaluation == null || !Evaluation.Success)
            {
                StatusMessage = "Evaluate a calculated plan before exporting a QA report.";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export SFRT QA Report",
                Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = "nSFRT_QA_Report.csv",
                AddExtension = true
            };
            bool? ok = dialog.ShowDialog();
            if (ok != true)
                return;
            try
            {
                QaReportExporter.Export(Evaluation, Parameters, dialog.FileName);
                StatusMessage = "Wrote CSV and PDF next to " + dialog.FileName;
            }
            catch (Exception ex)
            {
                StatusMessage = "QA export failed: " + ex.Message;
            }
        }

        private void CancelWork()
        {
            if (_cts != null)
                _cts.Cancel();
        }

        private void DisposeCts()
        {
            if (_cts == null)
                return;
            _cts.Dispose();
            _cts = null;
        }

        private void MoveSliceUp()
        {
            CurrentSliceIndex++;
        }

        private void MoveSliceDown()
        {
            CurrentSliceIndex--;
        }

        public void UpdateViewerSize(double width, double height)
        {
            _viewerWidth = Math.Max(width, 1);
            _viewerHeight = Math.Max(height, 1);
            RefreshViewer();
        }

        public void SelectSphere(AxialSphereVisual visual)
        {
            foreach (var sphere in GeneratedSpheres)
                sphere.IsSelected = false;

            if (visual != null && visual.Sphere != null)
            {
                visual.Sphere.IsSelected = true;
                SelectedSphere = visual.Sphere;
            }
            else
            {
                SelectedSphere = null;
            }

            RefreshAxialSpheres();
        }

        public bool TryMoveSelectedSphereOnAxialCanvas(Point canvasPoint)
        {
            if (SelectedSphere == null)
                return false;

            var mmPoint = CanvasToMm(canvasPoint);
            var candidate = new Models.Point3D(mmPoint.X, mmPoint.Y, SelectedSphere.Center.Z);
            VoxelMask mask = _geometry != null ? _geometry.ValidVolume : null;
            double spacing = Math.Min(Parameters.EffectiveLateralSpacingMm, Parameters.EffectiveSiSpacingMm);
            bool valid = _sphereOptimizer.IsValidEditedSpherePosition(
                candidate,
                SelectedSphere,
                GeneratedSpheres.ToList(),
                mask,
                spacing);

            if (!valid)
                return false;

            SelectedSphere.Center = candidate;
            SelectedSphere.IsEdited = true;
            OnPropertyChanged(nameof(CanRegenerateRemaining));
            RefreshAxialSpheres();
            return true;
        }

        public void RefreshViewer()
        {
            RefreshContours();
            UpdateViewTransform();
            RefreshAxialSpheres();
        }

        private void RefreshContours()
        {
            CurrentContours.Clear();
            if (string.IsNullOrEmpty(Parameters.SelectedTargetId) || _image == null)
                return;

            string targetId = Parameters.SelectedTargetId;
            int slice = CurrentSliceIndex;
            var contoursMm = CallEsapi(() => _esapi.GetStructureContoursOnSlice(targetId, slice));
            foreach (var contour in contoursMm)
            {
                var points = new PointCollection();
                foreach (var p in contour)
                    points.Add(new Point(p.X, p.Y));
                CurrentContours.Add(points);
            }
        }

        private void UpdateViewTransform()
        {
            if (CurrentContours.Count == 0)
            {
                _viewMinX = 0;
                _viewMinY = 0;
                _viewScale = 1.0;
                return;
            }

            double minX = CurrentContours.SelectMany(c => c).Min(p => p.X);
            double maxX = CurrentContours.SelectMany(c => c).Max(p => p.X);
            double minY = CurrentContours.SelectMany(c => c).Min(p => p.Y);
            double maxY = CurrentContours.SelectMany(c => c).Max(p => p.Y);
            double width = Math.Max(maxX - minX, 1.0);
            double height = Math.Max(maxY - minY, 1.0);
            double scaleX = (_viewerWidth - 2 * ViewPadding) / width;
            double scaleY = (_viewerHeight - 2 * ViewPadding) / height;
            _viewScale = Math.Min(scaleX, scaleY);

            double extraWidth = (_viewerWidth / _viewScale - width) / 2.0;
            double extraHeight = (_viewerHeight / _viewScale - height) / 2.0;
            _viewMinX = minX - extraWidth;
            _viewMinY = minY - extraHeight;
        }

        public List<PointCollection> GetCanvasContours()
        {
            var result = new List<PointCollection>();
            foreach (var contour in CurrentContours)
            {
                var canvasContour = new PointCollection();
                foreach (var point in contour)
                    canvasContour.Add(MmToCanvas(point.X, point.Y));
                result.Add(canvasContour);
            }
            return result;
        }

        private void RefreshAxialSpheres()
        {
            AxialSpheres.Clear();
            if (_image == null)
                return;
            double currentZ = _image.GetSliceZ(CurrentSliceIndex);

            foreach (var sphere in GeneratedSpheres)
            {
                double dz = Math.Abs(sphere.Center.Z - currentZ);
                if (dz > sphere.Radius)
                    continue;

                double sliceRadius = Math.Sqrt(Math.Max(0, sphere.Radius * sphere.Radius - dz * dz));
                var center = MmToCanvas(sphere.Center.X, sphere.Center.Y);
                AxialSpheres.Add(new AxialSphereVisual
                {
                    Sphere = sphere,
                    Center = center,
                    RadiusPixels = sliceRadius * _viewScale,
                    Stroke = sphere.IsEdited ? Brushes.Gold : (sphere.IsSelected ? Brushes.Lime : Brushes.OrangeRed),
                    Fill = sphere.IsEdited
                        ? new SolidColorBrush(Color.FromArgb(80, 255, 215, 0))
                        : new SolidColorBrush(Color.FromArgb(60, 255, 69, 0))
                });
            }
        }

        private Point MmToCanvas(double xMm, double yMm)
        {
            double x = (xMm - _viewMinX) * _viewScale;
            double y = _viewerHeight - ((yMm - _viewMinY) * _viewScale);
            return new Point(x, y);
        }

        private Point CanvasToMm(Point canvasPoint)
        {
            double x = (canvasPoint.X / _viewScale) + _viewMinX;
            double y = ((_viewerHeight - canvasPoint.Y) / _viewScale) + _viewMinY;
            return new Point(x, y);
        }

        private T CallEsapi<T>(Func<T> func)
        {
            if (_esapi.Worker == null || _esapi.Worker.CheckAccess())
                return func();
            return _esapi.Worker.Invoke(func);
        }

        private Task<T> CallEsapiAsync<T>(Func<T> func, CancellationToken token)
        {
            if (_esapi.Worker == null || _esapi.Worker.CheckAccess())
                return Task.FromResult(func());
            return _esapi.Worker.InvokeAsync(func, token);
        }
    }
}
