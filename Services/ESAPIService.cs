using System;
using System.Collections.Generic;
using System.Linq;
using SFRThelper.Models;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace SFRThelper.Services
{
    public class ESAPIService
    {
        private readonly ScriptContext _context;

        public ESAPIService(ScriptContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public List<Structure> GetPTVStructures()
        {
            try
            {
                return _context.StructureSet?.Structures
                    .Where(s => s.Id.ToUpper().Contains("PTV") || s.Id.ToUpper().Contains("GTV") || s.DicomType.ToUpper() == "PTV" || s.DicomType.ToUpper() == "GTV")
                    .Where(s => !s.IsEmpty)
                    .ToList() ?? new List<Structure>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting PTV structures: {ex.Message}");
                return new List<Structure>();
            }
        }

        public Structure GetStructureById(string structureId)
        {
            try
            {
                return _context.StructureSet?.Structures.FirstOrDefault(s => s.Id == structureId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting structure by ID: {ex.Message}");
                return null;
            }
        }

        public string GetPatientStatus()
        {
            try
            {
                if (_context.Patient == null)
                    return "No patient loaded";

                if (_context.StructureSet == null)
                    return "No structure set loaded";

                if (_context.Image == null)
                    return "No image loaded";

                return "Ready to create structures";
            }
            catch (Exception ex)
            {
                return $"Error checking status: {ex.Message}";
            }
        }

        public double GetImageOriginX() => _context.Image?.Origin.x ?? 0;
        public double GetImageOriginY() => _context.Image?.Origin.y ?? 0;
        public double GetImageOriginZ() => _context.Image?.Origin.z ?? 0;
        public double GetImageXRes() => _context.Image?.XRes ?? 1;
        public double GetImageYRes() => _context.Image?.YRes ?? 1;
        public double GetImageZRes() => _context.Image?.ZRes ?? 1;
        public int GetImageXSize() => _context.Image?.XSize ?? 0;
        public int GetImageYSize() => _context.Image?.YSize ?? 0;
        public int GetImageZSize() => _context.Image?.ZSize ?? 0;

        public double GetSliceZ(int sliceIndex)
        {
            return _context.Image.Origin.z + sliceIndex * _context.Image.ZRes;
        }

        public int GetNearestSliceIndex(double z)
        {
            if (_context.Image == null)
                return 0;

            int index = (int)Math.Round((z - _context.Image.Origin.z) / _context.Image.ZRes);
            if (index < 0) index = 0;
            if (index >= _context.Image.ZSize) index = _context.Image.ZSize - 1;
            return index;
        }

        public bool IsPointInsideStructure(string structureId, VVector point)
        {
            var structure = GetStructureById(structureId);
            if (structure == null)
                return false;

            try
            {
                return structure.IsPointInsideSegment(point);
            }
            catch
            {
                return false;
            }
        }

        public List<List<VVector>> GetStructureContoursOnSlice(string structureId, int sliceIndex)
        {
            var result = new List<List<VVector>>();
            var structure = GetStructureById(structureId);
            if (structure == null || _context.Image == null)
                return result;

            try
            {
                var contours = structure.GetContoursOnImagePlane(sliceIndex);
                if (contours == null)
                    return result;

                foreach (var contour in contours)
                {
                    if (contour != null && contour.Length >= 3)
                        result.Add(contour.ToList());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetStructureContoursOnSlice error: {ex.Message}");
            }

            return result;
        }

        public void CreateSingleSphereStructure(List<SphereModel> spheres)
        {
            if (spheres == null || spheres.Count == 0)
                throw new ArgumentException("No spheres to create.");

            if (_context.Patient == null)
                throw new InvalidOperationException("No patient is loaded.");

            if (_context.StructureSet == null)
                throw new InvalidOperationException("No structure set is loaded.");

            if (_context.Image == null)
                throw new InvalidOperationException("No image is loaded.");

            try
            {
                _context.Patient.BeginModifications();

                var structureId = GenerateUniqueStructureId("SFRT_Spheres");
                var combinedStructure = _context.StructureSet.AddStructure("CONTROL", structureId);

                if (combinedStructure == null)
                    throw new InvalidOperationException("Failed to create structure.");

                combinedStructure.Color = System.Windows.Media.Colors.Red;
                CreateHighPrecisionSphereContours(combinedStructure, spheres);

                System.Diagnostics.Debug.WriteLine($"Successfully created high-precision structure '{structureId}' with {spheres.Count} spheres");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create combined sphere structure: {ex.Message}", ex);
            }
        }

        private void CreateHighPrecisionSphereContours(Structure structure, List<SphereModel> spheres)
        {
            var image = _context.Image;
            double globalMinZ = spheres.Min(s => s.Center.z - s.Radius - 1.0);
            double globalMaxZ = spheres.Max(s => s.Center.z + s.Radius + 1.0);

            int processedSlices = 0;
            int totalSlices = 0;

            for (int sliceIndex = 0; sliceIndex < image.ZSize; sliceIndex++)
            {
                double z = image.Origin.z + sliceIndex * image.ZRes;

                if (z >= globalMinZ && z <= globalMaxZ)
                {
                    totalSlices++;
                    var allContoursOnSlice = new List<VVector[]>();

                    foreach (var sphere in spheres)
                    {
                        var contours = GenerateHighPrecisionCircleContour(sphere.Center, sphere.Radius, z, image.XRes, image.YRes);
                        allContoursOnSlice.AddRange(contours);
                    }

                    if (allContoursOnSlice.Count > 0)
                    {
                        try
                        {
                            foreach (var contour in allContoursOnSlice)
                            {
                                if (contour.Length >= 3)
                                    structure.AddContourOnImagePlane(contour, sliceIndex);
                            }

                            processedSlices++;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to add contours on slice {sliceIndex} at Z={z:F2}: {ex.Message}");
                        }
                    }
                }

                if (processedSlices % 5 == 0 && processedSlices > 0)
                {
                    double progress = (double)processedSlices / Math.Max(totalSlices, 1) * 100;
                    System.Diagnostics.Debug.WriteLine($"Contour creation progress: {progress:F1}% ({processedSlices}/{totalSlices} slices)");
                }
            }

            System.Diagnostics.Debug.WriteLine($"Contour creation completed: {processedSlices}/{totalSlices} slices processed successfully");
        }

        private List<VVector[]> GenerateHighPrecisionCircleContour(VVector sphereCenter, double sphereRadius, double z, double xRes, double yRes)
        {
            var contours = new List<VVector[]>();
            double deltaZ = Math.Abs(z - sphereCenter.z);

            if (deltaZ > sphereRadius + 0.1)
                return contours;

            double circleRadius = Math.Sqrt(Math.Max(0, sphereRadius * sphereRadius - deltaZ * deltaZ));
            if (circleRadius < 0.1)
                return contours;

            double circumference = 2 * Math.PI * circleRadius;
            double minRes = Math.Min(xRes, yRes);
            int basePoints = Math.Max(8, (int)(circumference / minRes));
            int numPoints = Math.Min(basePoints, 120);
            numPoints = ((numPoints + 3) / 4) * 4;

            var points = new VVector[numPoints];
            for (int i = 0; i < numPoints; i++)
            {
                double angle = 2.0 * Math.PI * i / numPoints;
                double x = sphereCenter.x + circleRadius * Math.Cos(angle);
                double y = sphereCenter.y + circleRadius * Math.Sin(angle);
                points[i] = new VVector(x, y, z);
            }

            contours.Add(points);

            if (circleRadius > 10.0)
            {
                int innerRings = (int)(circleRadius / 8.0);
                innerRings = Math.Min(innerRings, 3);

                for (int ring = 1; ring <= innerRings; ring++)
                {
                    double innerRadius = circleRadius * (1.0 - 0.3 * ring);
                    if (innerRadius > 1.0)
                    {
                        var innerPoints = new VVector[numPoints / 2];
                        for (int i = 0; i < innerPoints.Length; i++)
                        {
                            double angle = 2.0 * Math.PI * i / innerPoints.Length;
                            double x = sphereCenter.x + innerRadius * Math.Cos(angle);
                            double y = sphereCenter.y + innerRadius * Math.Sin(angle);
                            innerPoints[i] = new VVector(x, y, z);
                        }
                        contours.Add(innerPoints);
                    }
                }
            }

            return contours;
        }

        private string GenerateUniqueStructureId(string baseId)
        {
            try
            {
                var existingIds = new HashSet<string>(_context.StructureSet.Structures.Select(s => s.Id));
                string candidateId = baseId;
                int counter = 1;

                if (candidateId.Length > 12)
                    candidateId = candidateId.Substring(0, 12);

                while (existingIds.Contains(candidateId))
                {
                    candidateId = $"{baseId.Substring(0, Math.Min(baseId.Length, 10))}_{counter:D2}";
                    counter++;

                    if (counter > 99)
                    {
                        candidateId = $"SFRT_{DateTime.Now.Millisecond:D3}";
                        break;
                    }
                }

                return candidateId;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating unique ID: {ex.Message}");
                return $"SFRT_{DateTime.Now.Millisecond:D3}";
            }
        }
        // ESAPIService.cs : add this method
        // ESAPIService.cs
        public void CreateSphereStructures(List<SphereModel> spheres, bool singleStructureOnly)
        {
            if (spheres == null || spheres.Count == 0)
                throw new ArgumentException("No spheres to create.");

            if (_context.Patient == null)
                throw new InvalidOperationException("No patient is loaded.");

            if (_context.StructureSet == null)
                throw new InvalidOperationException("No structure set is loaded.");

            if (_context.Image == null)
                throw new InvalidOperationException("No image is loaded.");

            try
            {
                _context.Patient.BeginModifications();

                if (!singleStructureOnly)
                {
                    foreach (var sphere in spheres)
                    {
                        var sphereId = GenerateUniqueStructureId(sphere.Id);
                        var sphereStructure = _context.StructureSet.AddStructure("CONTROL", sphereId);
                        if (sphereStructure != null)
                        {
                            sphereStructure.Color = System.Windows.Media.Colors.Yellow;
                            CreateHighPrecisionSphereContours(sphereStructure, new List<SphereModel> { sphere });
                        }
                    }
                }

                var mergedId = GenerateUniqueStructureId("SFRT_Spheres");
                var mergedStructure = _context.StructureSet.AddStructure("CONTROL", mergedId);
                if (mergedStructure == null)
                    throw new InvalidOperationException("Failed to create merged sphere structure.");

                mergedStructure.Color = System.Windows.Media.Colors.Red;
                CreateHighPrecisionSphereContours(mergedStructure, spheres);

                var voidSpheres = BuildVoidSpheres(spheres);
                if (voidSpheres.Count > 0)
                {
                    var voidId = GenerateUniqueStructureId("SFRT_Voids");
                    var voidStructure = _context.StructureSet.AddStructure("CONTROL", voidId);
                    if (voidStructure != null)
                    {
                        voidStructure.Color = System.Windows.Media.Colors.Blue;
                        CreateHighPrecisionSphereContours(voidStructure, voidSpheres);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to create structures: " + ex.Message, ex);
            }
        }

        private List<SphereModel> BuildVoidSpheres(List<SphereModel> peakSpheres)
        {
            var result = new List<SphereModel>();
            int index = 1;

            for (int i = 0; i < peakSpheres.Count; i++)
            {
                for (int j = i + 1; j < peakSpheres.Count; j++)
                {
                    var a = peakSpheres[i];
                    var b = peakSpheres[j];

                    double dx = a.Center.x - b.Center.x;
                    double dy = a.Center.y - b.Center.y;
                    double dz = a.Center.z - b.Center.z;
                    double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                    double gap = distance - a.Radius - b.Radius;
                    if (gap <= 1.0)
                        continue;

                    double radius = Math.Min(a.Radius, Math.Min(b.Radius, gap / 2.0));
                    if (radius < 1.0)
                        continue;

                    var center = new VVector(
                        (a.Center.x + b.Center.x) / 2.0,
                        (a.Center.y + b.Center.y) / 2.0,
                        (a.Center.z + b.Center.z) / 2.0);

                    result.Add(new SphereModel(center, radius, index));
                    index++;
                }
            }

            return result;
        }
        public bool ValidateStructureIntegrity(Structure structure, List<SphereModel> originalSpheres)
        {
            try
            {
                if (structure == null || structure.IsEmpty)
                {
                    System.Diagnostics.Debug.WriteLine("Structure validation failed: Structure is null or empty");
                    return false;
                }

                var bounds = structure.MeshGeometry.Bounds;
                double structureVolume = structure.Volume;

                double expectedVolume = 0;
                foreach (var sphere in originalSpheres)
                    expectedVolume += (4.0 / 3.0) * Math.PI * Math.Pow(sphere.Radius, 3);

                expectedVolume /= 1000.0;
                double volumeRatio = expectedVolume > 0 ? structureVolume / expectedVolume : 0;
                bool volumeValid = volumeRatio >= 0.5 && volumeRatio <= 1.5;

                System.Diagnostics.Debug.WriteLine($"Structure validation: Volume ratio = {volumeRatio:F2} (Expected: {expectedVolume:F1} cm³, Actual: {structureVolume:F1} cm³)");
                System.Diagnostics.Debug.WriteLine($"Bounds: {bounds.SizeX:F1} x {bounds.SizeY:F1} x {bounds.SizeZ:F1} mm³");

                return volumeValid;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Structure validation error: {ex.Message}");
                return false;
            }
        }
    }
}