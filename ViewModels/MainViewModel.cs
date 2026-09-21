using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SFRThelper.Models;
using SFRThelper.Services;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace SFRThelper.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ESAPIService _esapiService;
        private readonly SphereOptimizer _sphereOptimizer;

        private bool _isGenerating;
        private string _statusMessage = "Ready";
        private int _currentSliceIndex;
        private double _viewerWidth = 500;
        private double _viewerHeight = 500;
        private SphereModel _selectedSphere;
        private Structure _selectedStructure;

        private double _viewMinX;
        private double _viewMinY;
        private double _viewScale = 1.0;
        private const double ViewPadding = 20.0;

        public SFRTParameters Parameters { get; }
        public ObservableCollection<string> AvailablePTVs { get; }
        public ObservableCollection<SphereModel> GeneratedSpheres { get; }
        public ObservableCollection<PointCollection> CurrentContours { get; }
        public ObservableCollection<AxialSphereVisual> AxialSpheres { get; }

        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                _isGenerating = value;
                OnPropertyChanged(nameof(IsGenerating));
                OnPropertyChanged(nameof(CanGenerate));
                OnPropertyChanged(nameof(CanCreateStructures));
                OnPropertyChanged(nameof(CanRegenerateRemaining));
            }
        }

        public bool CanGenerate
        {
            get
            {
                return !IsGenerating &&
                       !string.IsNullOrEmpty(Parameters.SelectedPTVId) &&
                       Parameters.SphereRadius > 0 &&
                       Parameters.CenterSpacing > Parameters.SphereRadius * 2;
            }
        }

        public bool CanCreateStructures
        {
            get { return !IsGenerating && GeneratedSpheres.Count > 0; }
        }

        // MainViewModel.cs : replace CanRegenerateRemaining
        public bool CanRegenerateRemaining
        {
            get { return !IsGenerating && GeneratedSpheres.Any(s => s.IsEdited); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        public int CurrentSliceIndex
        {
            get => _currentSliceIndex;
            set
            {
                _currentSliceIndex = value;
                OnPropertyChanged(nameof(CurrentSliceIndex));
                OnPropertyChanged(nameof(CurrentSliceText));
                RefreshViewer();
            }
        }

        public string CurrentSliceText
        {
            get { return "Slice: " + CurrentSliceIndex; }
        }

        public SphereModel SelectedSphere
        {
            get => _selectedSphere;
            set
            {
                _selectedSphere = value;
                OnPropertyChanged(nameof(SelectedSphere));
            }
        }

        public ICommand GenerateSpheresCommand { get; }
        public ICommand CreateStructuresCommand { get; }
        public ICommand SliceUpCommand { get; }
        public ICommand SliceDownCommand { get; }
        public ICommand RegenerateRemainingCommand { get; }

        public MainViewModel(ScriptContext context)
        {
            _esapiService = new ESAPIService(context);
            _sphereOptimizer = new SphereOptimizer();

            Parameters = new SFRTParameters();
            AvailablePTVs = new ObservableCollection<string>();
            GeneratedSpheres = new ObservableCollection<SphereModel>();
            CurrentContours = new ObservableCollection<PointCollection>();
            AxialSpheres = new ObservableCollection<AxialSphereVisual>();

            GenerateSpheresCommand = new DelegateCommand(GenerateSpheres);
            CreateStructuresCommand = new DelegateCommand(CreateStructures);
            SliceUpCommand = new DelegateCommand(MoveSliceUp);
            SliceDownCommand = new DelegateCommand(MoveSliceDown);
            RegenerateRemainingCommand = new DelegateCommand(RegenerateRemaining);

            Parameters.PropertyChanged += Parameters_PropertyChanged;
            GeneratedSpheres.CollectionChanged += GeneratedSpheres_CollectionChanged;

            LoadAvailablePTVs();
        }

        private void Parameters_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CanGenerate));

            if (e.PropertyName == nameof(SFRTParameters.SelectedPTVId))
                OnTargetChanged();
        }

        private void GeneratedSpheres_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CanCreateStructures));
            OnPropertyChanged(nameof(CanRegenerateRemaining));
            RefreshViewer();
        }

        private void LoadAvailablePTVs()
        {
            try
            {
                var ptvs = _esapiService.GetPTVStructures();
                AvailablePTVs.Clear();

                foreach (var ptv in ptvs)
                    AvailablePTVs.Add(ptv.Id);

                if (AvailablePTVs.Any())
                    Parameters.SelectedPTVId = AvailablePTVs.First();

                StatusMessage = "Found " + AvailablePTVs.Count + " target structures";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error loading targets: " + ex.Message;
            }
        }

        private void OnTargetChanged()
        {
            _selectedStructure = _esapiService.GetStructureById(Parameters.SelectedPTVId);
            if (_selectedStructure == null || _selectedStructure.IsEmpty)
                return;

            CurrentSliceIndex = _esapiService.GetNearestSliceIndex(_selectedStructure.CenterPoint.z);
            RefreshViewer();
        }

        private void GenerateSpheres()
        {
            try
            {
                IsGenerating = true;
                StatusMessage = "Generating spheres...";

                _selectedStructure = _esapiService.GetStructureById(Parameters.SelectedPTVId);
                if (_selectedStructure == null)
                {
                    StatusMessage = "Selected target not found.";
                    return;
                }

                if (_selectedStructure.IsEmpty)
                {
                    StatusMessage = "Selected target is empty.";
                    return;
                }

                var spheres = _sphereOptimizer.OptimizeSpherePlacement(
                    _selectedStructure,
                    Parameters.SphereRadius,
                    Parameters.CenterSpacing,
                    Parameters.BoundaryMarginMm,
                    Parameters.MaxIterations);

                GeneratedSpheres.Clear();
                for (int i = 0; i < spheres.Count; i++)
                {
                    spheres[i].Id = "Sphere_" + (i + 1).ToString("D3");
                    GeneratedSpheres.Add(spheres[i]);
                }

                StatusMessage = "Generated " + spheres.Count + " spheres.";
                CurrentSliceIndex = _esapiService.GetNearestSliceIndex(_selectedStructure.CenterPoint.z);
                RefreshViewer();
            }
            catch (Exception ex)
            {
                StatusMessage = "Error during generation: " + ex.Message;
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private void RegenerateRemaining()
        {
            try
            {
                IsGenerating = true;
                StatusMessage = "Regenerating remaining spheres...";

                _selectedStructure = _esapiService.GetStructureById(Parameters.SelectedPTVId);
                if (_selectedStructure == null)
                {
                    StatusMessage = "Selected target not found.";
                    return;
                }

                var fixedSpheres = GeneratedSpheres.Where(s => s.IsEdited).ToList();

                var regenerated = _sphereOptimizer.OptimizeSpherePlacement(
                    _selectedStructure,
                    Parameters.SphereRadius,
                    Parameters.CenterSpacing,
                    Parameters.BoundaryMarginMm,
                    Parameters.MaxIterations,
                    fixedSpheres);

                GeneratedSpheres.Clear();

                int index = 1;
                foreach (var sphere in fixedSpheres)
                {
                    sphere.Id = "Sphere_" + index.ToString("D3");
                    GeneratedSpheres.Add(sphere);
                    index++;
                }

                foreach (var sphere in regenerated)
                {
                    sphere.Id = "Sphere_" + index.ToString("D3");
                    GeneratedSpheres.Add(sphere);
                    index++;
                }

                StatusMessage = "Regenerated with " + fixedSpheres.Count + " fixed spheres and " + regenerated.Count + " new spheres.";
                RefreshViewer();
            }
            catch (Exception ex)
            {
                StatusMessage = "Error regenerating spheres: " + ex.Message;
            }
            finally
            {
                IsGenerating = false;
            }
        }

        // MainViewModel.cs : replace CreateStructures()
        // MainViewModel.cs : replace CreateStructures()
        private void CreateStructures()
        {
            try
            {
                StatusMessage = "Creating structures...";

                string status = _esapiService.GetPatientStatus();
                if (status != "Ready to create structures")
                {
                    StatusMessage = "Cannot create structures: " + status;
                    return;
                }

                _esapiService.CreateSphereStructures(GeneratedSpheres.ToList(), Parameters.SingleStructureOnly);
                StatusMessage = Parameters.SingleStructureOnly
                    ? "Created merged sphere structure and void structure."
                    : "Created individual spheres, merged sphere structure, and void structure.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error creating structures: " + ex.Message;
            }
        }

        private void MoveSliceUp()
        {
            if (CurrentSliceIndex < _esapiService.GetImageZSize() - 1)
                CurrentSliceIndex++;
        }

        private void MoveSliceDown()
        {
            if (CurrentSliceIndex > 0)
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
            if (SelectedSphere == null || _selectedStructure == null)
                return false;

            var mmPoint = CanvasToMm(canvasPoint);
            var candidateCenter = new VVector(mmPoint.X, mmPoint.Y, SelectedSphere.Center.z);

            bool valid = _sphereOptimizer.IsValidEditedSpherePosition(
                candidateCenter,
                SelectedSphere,
                GeneratedSpheres.ToList(),
                _selectedStructure,
                Parameters.BoundaryMarginMm,
                Parameters.CenterSpacing);

            if (!valid)
                return false;

            // MainViewModel.cs : in TryMoveSelectedSphereOnAxialCanvas(...)
            SelectedSphere.Center = candidateCenter;
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

            if (string.IsNullOrEmpty(Parameters.SelectedPTVId))
                return;

            var contoursMm = _esapiService.GetStructureContoursOnSlice(Parameters.SelectedPTVId, CurrentSliceIndex);
            foreach (var contour in contoursMm)
            {
                var points = new PointCollection();
                foreach (var p in contour)
                    points.Add(new Point(p.x, p.y));

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
            double currentZ = _esapiService.GetSliceZ(CurrentSliceIndex);

            foreach (var sphere in GeneratedSpheres)
            {
                double dz = Math.Abs(sphere.Center.z - currentZ);
                if (dz > sphere.Radius)
                    continue;

                double sliceRadius = Math.Sqrt(Math.Max(0, sphere.Radius * sphere.Radius - dz * dz));
                var center = MmToCanvas(sphere.Center.x, sphere.Center.y);

                AxialSpheres.Add(new AxialSphereVisual
                {
                    Sphere = sphere,
                    Center = center,
                    RadiusPixels = sliceRadius * _viewScale,
                    Stroke = sphere.IsEdited ? Brushes.Gold : (sphere.IsSelected ? Brushes.Lime : Brushes.Red),
                    Fill = sphere.IsEdited
                        ? new SolidColorBrush(Color.FromArgb(80, 255, 215, 0))
                        : new SolidColorBrush(Color.FromArgb(60, 255, 0, 0))
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

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class AxialSphereVisual
    {
        public SphereModel Sphere { get; set; }
        public Point Center { get; set; }
        public double RadiusPixels { get; set; }
        public Brush Stroke { get; set; }
        public Brush Fill { get; set; }
    }

    public class DelegateCommand : ICommand
    {
        private readonly Action _execute;

        public DelegateCommand(Action execute)
        {
            _execute = execute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public void Execute(object parameter)
        {
            _execute();
        }
    }
}