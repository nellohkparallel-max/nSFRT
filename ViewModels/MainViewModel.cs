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

        private bool _isBusy;
        private string _statusMessage = "Ready";
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
        public IReadOnlyList<PackingOption> PackingOptions { get; private set; }
        public IReadOnlyList<GenerationModeOption> GenerationModeOptions { get; private set; }

        public MainViewModel(IESAPIService esapi)
        {
            if (esapi == null)
                throw new ArgumentNullException("esapi");

            _esapi = esapi;
            _sphereOptimizer = new SphereOptimizer();
            _evaluationService = new SFRTEvaluationService(esapi);

            Parameters = new SFRTParameters();
            AvailableTargets = new ObservableCollection<StructureListItem>();
            AvailableOARs = new ObservableCollection<StructureListItem>();
            GeneratedSpheres = new ObservableCollection<SphereModel>();
            CurrentContours = new ObservableCollection<PointCollection>();
            AxialSpheres = new ObservableCollection<AxialSphereVisual>();
            AvailablePlans = new ObservableCollection<PlanListItem>();
            EvaluationMetrics = new ObservableCollection<EvaluationMetricRow>();
            IndividualPeakDoses = new ObservableCollection<PeakDoseRow>();

            PackingOptions = new[]
            {
                new PackingOption { Value = PackingGeometryMode.SimpleCubic, Display = "Simple Cubic" },
                new PackingOption { Value = PackingGeometryMode.HexagonalClosePacking, Display = "HCP / FCC" }
            };
            GenerationModeOptions = new[]
            {
                new GenerationModeOption { Value = SphereGenerationMode.IndividualAndComposite, Display = "Individual Peak_xx + Lattice_Peaks" },
                new GenerationModeOption { Value = SphereGenerationMode.CompositeOnly, Display = "Composite Lattice_Peaks only" }
            };

            PreviewLatticeCommand = new AsyncRelayCommand(PreviewLatticeAsync, () => CanPreview);
            GenerateStructuresCommand = new AsyncRelayCommand(GenerateStructuresAsync, () => CanCreateStructures);
            CancelCommand = new RelayCommand(CancelWork, () => IsBusy);
            SliceUpCommand = new RelayCommand(MoveSliceUp, () => !IsBusy);
            SliceDownCommand = new RelayCommand(MoveSliceDown, () => !IsBusy);
            RegenerateRemainingCommand = new AsyncRelayCommand(RegenerateRemainingAsync, () => CanRegenerateRemaining);
            EvaluatePlanCommand = new AsyncRelayCommand(EvaluatePlanAsync, () => CanEvaluate);
            RefreshPlansCommand = new RelayCommand(LoadPlans);

            Parameters.PropertyChanged += Parameters_PropertyChanged;
            Parameters.ErrorsChanged += Parameters_ErrorsChanged;
            GeneratedSpheres.CollectionChanged += GeneratedSpheres_CollectionChanged;

            _image = _esapi.GetImageGeometry();
            LoadCatalogs();
            RefreshLocalFeasibility();
            StatusMessage = _esapi.GetPatientStatus();
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
                OnPropertyChanged(nameof(CanRegenerateRemaining));
                OnPropertyChanged(nameof(CanEvaluate));
                OnPropertyChanged(nameof(IsIdle));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsIdle
        {
            get { return !IsBusy; }
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

        public bool CanRegenerateRemaining
        {
            get { return !IsBusy && GeneratedSpheres.Any(s => s.IsEdited); }
        }

        public bool CanEvaluate
        {
            get { return !IsBusy && SelectedPlan != null && SelectedPlan.IsDoseValid; }
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

        public ICommand PreviewLatticeCommand { get; private set; }
        public ICommand GenerateStructuresCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand SliceUpCommand { get; private set; }
        public ICommand SliceDownCommand { get; private set; }
        public ICommand RegenerateRemainingCommand { get; private set; }
        public ICommand EvaluatePlanCommand { get; private set; }
        public ICommand RefreshPlansCommand { get; private set; }

        private void LoadCatalogs()
        {
            AvailableTargets.Clear();
            foreach (var item in _esapi.GetTargetStructures())
                AvailableTargets.Add(item);

            AvailableOARs.Clear();
            foreach (var item in _esapi.GetAvoidanceStructures())
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
            foreach (var plan in _esapi.GetEvaluablePlans())
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
                || e.PropertyName == nameof(SFRTParameters.Oar2ClearanceMm))
            {
                InvalidateGeometry();
                RefreshLocalFeasibility();
                return;
            }

            if (e.PropertyName == nameof(SFRTParameters.CenterSpacingMm)
                || e.PropertyName == nameof(SFRTParameters.PackingMode)
                || e.PropertyName == nameof(SFRTParameters.MaxSphereCount))
            {
                RefreshLocalFeasibility();
            }
        }

        private void Parameters_ErrorsChanged(object sender, DataErrorsChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CanPreview));
            OnPropertyChanged(nameof(CanCreateStructures));
            CommandManager.InvalidateRequerySuggested();
        }

        private void GeneratedSpheres_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CanCreateStructures));
            OnPropertyChanged(nameof(CanRegenerateRemaining));
            RefreshViewer();
        }

        private void InvalidateGeometry()
        {
            _geometry = null;
        }

        private void OnTargetChanged()
        {
            var info = _esapi.GetStructureInfo(Parameters.SelectedTargetId);
            if (info == null || info.IsEmpty)
                return;
            _image = _esapi.GetImageGeometry();
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

        private async Task RunLatticeAsync(bool keepEdited)
        {
            if (!Parameters.IsValid)
            {
                StatusMessage = Parameters.Error;
                return;
            }

            IsBusy = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var progress = new Progress<string>(m => StatusMessage = m);
            try
            {
                List<SphereModel> fixedSpheres = keepEdited
                    ? GeneratedSpheres.Where(s => s.IsEdited).ToList()
                    : null;

                StatusMessage = "Phase 1: extracting V_valid on the ESAPI thread...";
                LatticeGeometryContext geometry = _esapi.ExtractLatticeGeometry(Parameters, progress, token);
                token.ThrowIfCancellationRequested();
                _geometry = geometry;
                if (!geometry.IsValid)
                {
                    GeneratedSpheres.Clear();
                    Feasibility = SfrtMetricsCalculator.BuildFeasibility(geometry, Parameters, 0);
                    StatusMessage = geometry.Message;
                    return;
                }

                StatusMessage = "Phase 2: packing lattice on a worker thread...";
                LatticePackingResult packing = await Task.Run(() =>
                    _sphereOptimizer.GenerateLattice(geometry, Parameters, fixedSpheres, token), token).ConfigureAwait(true);

                GeneratedSpheres.Clear();
                foreach (var sphere in packing.Spheres)
                    GeneratedSpheres.Add(sphere);

                Feasibility = SfrtMetricsCalculator.BuildFeasibility(geometry, Parameters, packing.SphereCount);
                OnPropertyChanged(nameof(FeasibilityText));
                StatusMessage = packing.Message;

                if (_geometry != null)
                    CurrentSliceIndex = _image.GetNearestSliceIndex(_geometry.CenterOfMass.Z);
                RefreshViewer();
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

            string reason;
            if (!_esapi.CanModifyStructureSet(out reason))
            {
                StatusMessage = reason;
                return;
            }

            IsBusy = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var progress = new Progress<string>(m => StatusMessage = m);
            await Task.Yield();
            try
            {
                StatusMessage = "Phase 3: committing Peak / Lattice_Peaks / Lattice_Valley structures...";
                var spheres = GeneratedSpheres.ToList();
                // Contour commit must stay on the ESAPI STA / UI thread.
                StructureCreationResult result = _esapi.CreateLatticeStructures(spheres, Parameters, progress, token);
                StatusMessage = result.Message;
                LoadCatalogs();
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
            try
            {
                StatusMessage = "Evaluating SFRT dosimetry...";
                string key = SelectedPlan.Key;
                SFRTParameters parameters = Parameters;
                await Task.Yield();
                // DVH queries must run on the ESAPI STA thread.
                SFRTEvaluationResult result = _evaluationService.Evaluate(key, parameters);

                Evaluation = result;
                EvaluationMetrics.Clear();
                foreach (var row in result.MetricRows)
                    EvaluationMetrics.Add(row);
                IndividualPeakDoses.Clear();
                foreach (var peak in result.IndividualPeaks)
                    IndividualPeakDoses.Add(peak);

                StatusMessage = result.Success
                    ? "Evaluation complete. PVDR_mean = " + SfrtMetricsCalculator.FormatRatio(result.PvdrMean)
                    : result.ErrorMessage;
                OnPropertyChanged(nameof(EvaluationAlertText));
                OnPropertyChanged(nameof(HasEvaluationAlerts));
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
            bool valid = _sphereOptimizer.IsValidEditedSpherePosition(
                candidate,
                SelectedSphere,
                GeneratedSpheres.ToList(),
                mask,
                Parameters.CenterSpacingMm);

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

            var contoursMm = _esapi.GetStructureContoursOnSlice(Parameters.SelectedTargetId, CurrentSliceIndex);
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
    }
}
