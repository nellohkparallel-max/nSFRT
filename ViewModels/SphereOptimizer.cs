using System;
using System.Collections.Generic;
using System.Linq;
using SFRThelper.Models;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Windows.Media.Media3D;

namespace SFRThelper.ViewModels
{
    public class SphereOptimizer
    {
        public class GridSearchResult
        {
            public List<VVector> ValidCenters { get; set; } = new List<VVector>();
            public VVector BestShift { get; set; }
            public int SphereCount { get; set; }
            public double PackingEfficiency { get; set; }
        }

        public List<SphereModel> OptimizeSpherePlacement(Structure ptv, double sphereRadius, double centerSpacing)
        {
            return OptimizeSpherePlacement(ptv, sphereRadius, centerSpacing, 0.0, 100);
        }

        public List<SphereModel> OptimizeSpherePlacement(Structure ptv, double sphereRadius, double centerSpacing, double boundaryMarginMm)
        {
            return OptimizeSpherePlacement(ptv, sphereRadius, centerSpacing, boundaryMarginMm, 100);
        }

        public List<SphereModel> OptimizeSpherePlacement(Structure ptv, double sphereRadius, double centerSpacing, double boundaryMarginMm, int maxIterations)
        {
            var spheres = new List<SphereModel>();

            try
            {
                var bounds = ptv.MeshGeometry.Bounds;
                System.Diagnostics.Debug.WriteLine("Starting iterative grid search optimization...");

                var searchResult = PerformIterativeGridSearch(bounds, ptv, sphereRadius, centerSpacing, boundaryMarginMm, maxIterations);

                if (searchResult.ValidCenters.Count > 0)
                {
                    for (int i = 0; i < searchResult.ValidCenters.Count; i++)
                        spheres.Add(new SphereModel(searchResult.ValidCenters[i], sphereRadius, i + 1));

                    System.Diagnostics.Debug.WriteLine($"Grid search completed: {spheres.Count} spheres at optimal shift ({searchResult.BestShift.x:F2}, {searchResult.BestShift.y:F2}, {searchResult.BestShift.z:F2})");
                    System.Diagnostics.Debug.WriteLine($"Packing efficiency: {searchResult.PackingEfficiency:P1}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No valid sphere placements found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OptimizeSpherePlacement: {ex.Message}");
            }

            return spheres;
        }

        public List<SphereModel> OptimizeSpherePlacement(Structure ptv, double sphereRadius, double centerSpacing, double boundaryMarginMm, int maxIterations, List<SphereModel> fixedSpheres)
        {
            var spheres = OptimizeSpherePlacement(ptv, sphereRadius, centerSpacing, boundaryMarginMm, maxIterations);
            if (fixedSpheres == null || fixedSpheres.Count == 0)
                return spheres;

            return spheres
                .Where(s => !OverlapsFixedSpheres(s, fixedSpheres, centerSpacing))
                .ToList();
        }

        public bool IsValidEditedSpherePosition(VVector candidateCenter, SphereModel movingSphere, List<SphereModel> allSpheres, Structure ptv, double boundaryMarginMm, double centerSpacing)
        {
            if (!IsSphereCompletelyInsideTarget(candidateCenter, movingSphere.Radius, ptv, boundaryMarginMm))
                return false;

            foreach (var sphere in allSpheres)
            {
                if (sphere == movingSphere)
                    continue;

                double minDistance = Math.Max(centerSpacing, movingSphere.Radius + sphere.Radius);
                if (CalculateDistance(candidateCenter, sphere.Center) < minDistance)
                    return false;
            }

            return true;
        }

        private bool OverlapsFixedSpheres(SphereModel sphere, List<SphereModel> fixedSpheres, double centerSpacing)
        {
            foreach (var fixedSphere in fixedSpheres)
            {
                double minDistance = Math.Max(centerSpacing, sphere.Radius + fixedSphere.Radius);
                if (CalculateDistance(sphere.Center, fixedSphere.Center) < minDistance)
                    return true;
            }

            return false;
        }

        private GridSearchResult PerformIterativeGridSearch(Rect3D bounds, Structure ptv, double sphereRadius, double centerSpacing, double boundaryMarginMm, int maxIterations)
        {
            var bestResult = new GridSearchResult();

            double maxShift = Math.Min(centerSpacing, sphereRadius * 2);
            int stepsPerAxis = (int)Math.Ceiling(Math.Pow(Math.Max(1, maxIterations), 1.0 / 3.0));
            if (stepsPerAxis % 2 == 0)
                stepsPerAxis++;

            stepsPerAxis = Math.Max(1, stepsPerAxis);
            double actualStepSize = stepsPerAxis == 1 ? 0 : (2 * maxShift) / (stepsPerAxis - 1);
            int totalIterations = stepsPerAxis * stepsPerAxis * stepsPerAxis;
            int iteration = 0;

            System.Diagnostics.Debug.WriteLine("Grid search parameters:");
            System.Diagnostics.Debug.WriteLine($" Requested iterations: {maxIterations}");
            System.Diagnostics.Debug.WriteLine($" Steps per axis: {stepsPerAxis}");
            System.Diagnostics.Debug.WriteLine($" Actual iterations: {totalIterations}");
            System.Diagnostics.Debug.WriteLine($" Max shift: ±{maxShift:F2} mm");
            System.Diagnostics.Debug.WriteLine($" Step size: {actualStepSize:F2} mm");

            for (int ix = 0; ix < stepsPerAxis; ix++)
            {
                double shiftX = stepsPerAxis == 1 ? 0 : -maxShift + ix * actualStepSize;

                for (int iy = 0; iy < stepsPerAxis; iy++)
                {
                    double shiftY = stepsPerAxis == 1 ? 0 : -maxShift + iy * actualStepSize;

                    for (int iz = 0; iz < stepsPerAxis; iz++)
                    {
                        double shiftZ = stepsPerAxis == 1 ? 0 : -maxShift + iz * actualStepSize;
                        iteration++;

                        var currentShift = new VVector(shiftX, shiftY, shiftZ);
                        var shiftedCenters = GenerateShiftedHCPCenters(bounds, sphereRadius, centerSpacing, currentShift);
                        var validCenters = FilterSpheresCompletelyInsideTarget(shiftedCenters, ptv, sphereRadius, boundaryMarginMm);
                        double efficiency = CalculatePackingEfficiency(validCenters, sphereRadius, bounds);

                        if (validCenters.Count > bestResult.SphereCount ||
                            (validCenters.Count == bestResult.SphereCount && efficiency > bestResult.PackingEfficiency))
                        {
                            bestResult.ValidCenters = new List<VVector>(validCenters);
                            bestResult.BestShift = currentShift;
                            bestResult.SphereCount = validCenters.Count;
                            bestResult.PackingEfficiency = efficiency;
                        }

                        if (iteration % Math.Max(totalIterations / 10, 1) == 0)
                        {
                            double progress = (double)iteration / totalIterations * 100;
                            System.Diagnostics.Debug.WriteLine($"Grid search progress: {progress:F0}% (best so far: {bestResult.SphereCount} spheres)");
                        }
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"Grid search completed after {iteration} iterations");
            return bestResult;
        }

        private List<VVector> GenerateShiftedHCPCenters(Rect3D bounds, double sphereRadius, double spacing, VVector shift)
        {
            var centers = new List<VVector>();

            double diameter = sphereRadius * 2;
            double actualSpacing = Math.Max(spacing, diameter);

            double layerHeight = actualSpacing * Math.Sqrt(2.0 / 3.0);
            double rowOffset = actualSpacing * Math.Sqrt(3) / 2;

            double startX = bounds.X + sphereRadius + 1.0 + shift.x;
            double endX = bounds.X + bounds.SizeX - sphereRadius - 1.0 + shift.x;
            double startY = bounds.Y + sphereRadius + 1.0 + shift.y;
            double endY = bounds.Y + bounds.SizeY - sphereRadius - 1.0 + shift.y;
            double startZ = bounds.Z + sphereRadius + 1.0 + shift.z;
            double endZ = bounds.Z + bounds.SizeZ - sphereRadius - 1.0 + shift.z;

            int layerIndex = 0;

            for (double z = startZ; z <= endZ; z += layerHeight)
            {
                bool isOddLayer = layerIndex % 2 == 1;
                int rowIndex = 0;

                for (double y = startY; y <= endY; y += rowOffset)
                {
                    bool isOddRow = rowIndex % 2 == 1;

                    double xOffset = 0;
                    if (isOddRow)
                        xOffset += actualSpacing / 2;
                    if (isOddLayer)
                        xOffset += actualSpacing / 4;

                    for (double x = startX + xOffset; x <= endX; x += actualSpacing)
                    {
                        centers.Add(new VVector(x, y, z));

                        if (centers.Count > 3000)
                            return centers;
                    }

                    rowIndex++;
                }

                layerIndex++;
            }

            return centers;
        }

        private List<VVector> FilterSpheresCompletelyInsideTarget(List<VVector> candidateCenters, Structure ptv, double sphereRadius, double boundaryMarginMm)
        {
            var validCenters = new List<VVector>();

            foreach (var center in candidateCenters)
            {
                try
                {
                    if (IsSphereCompletelyInsideTarget(center, sphereRadius, ptv, boundaryMarginMm))
                        validCenters.Add(center);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error validating center: {ex.Message}");
                }
            }

            return validCenters;
        }

        public bool IsSphereCompletelyInsideTarget(VVector center, double radius, Structure ptv, double boundaryMarginMm)
        {
            try
            {
                if (!ptv.IsPointInsideSegment(center))
                    return false;

                double checkRadius = radius + boundaryMarginMm;
                var samplingDirections = GetIcosahedronVertices();

                foreach (var direction in samplingDirections)
                {
                    double magnitude = Math.Sqrt(direction.x * direction.x + direction.y * direction.y + direction.z * direction.z);

                    var normalizedDir = new VVector(
                        direction.x / magnitude,
                        direction.y / magnitude,
                        direction.z / magnitude);

                    var surfacePoint = new VVector(
                        center.x + normalizedDir.x * checkRadius,
                        center.y + normalizedDir.y * checkRadius,
                        center.z + normalizedDir.z * checkRadius);

                    if (!ptv.IsPointInsideSegment(surfacePoint))
                        return false;
                }

                var cardinalPoints = new[]
                {
                    new VVector(center.x + checkRadius, center.y, center.z),
                    new VVector(center.x - checkRadius, center.y, center.z),
                    new VVector(center.x, center.y + checkRadius, center.z),
                    new VVector(center.x, center.y - checkRadius, center.z),
                    new VVector(center.x, center.y, center.z + checkRadius),
                    new VVector(center.x, center.y, center.z - checkRadius)
                };

                foreach (var point in cardinalPoints)
                {
                    if (!ptv.IsPointInsideSegment(point))
                        return false;
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public List<SphereModel> GenerateVoidSpheres(List<SphereModel> peakSpheres, Structure target, double boundaryMarginMm)
        {
            var voidSpheres = new List<SphereModel>();
            if (peakSpheres == null || peakSpheres.Count < 2)
                return voidSpheres;

            int nextId = 1;
            double baseRadius = peakSpheres.Min(s => s.Radius);
            double minRadius = Math.Max(1.0, baseRadius * 0.5);

            for (int i = 0; i < peakSpheres.Count; i++)
            {
                for (int j = i + 1; j < peakSpheres.Count; j++)
                {
                    var a = peakSpheres[i];
                    var b = peakSpheres[j];

                    double distance = CalculateDistance(a.Center, b.Center);
                    double availableRadius = (distance - a.Radius - b.Radius) / 2.0;
                    if (availableRadius < minRadius)
                        continue;

                    double radius = Math.Min(baseRadius, availableRadius);
                    var center = new VVector(
                        (a.Center.x + b.Center.x) / 2.0,
                        (a.Center.y + b.Center.y) / 2.0,
                        (a.Center.z + b.Center.z) / 2.0);

                    while (radius >= minRadius)
                    {
                        if (IsSphereCompletelyInsideTarget(center, radius, target, boundaryMarginMm) &&
                            !OverlapsAnySphere(center, radius, peakSpheres) &&
                            !OverlapsAnySphere(center, radius, voidSpheres))
                        {
                            voidSpheres.Add(new SphereModel(center, radius, nextId++));
                            break;
                        }

                        radius -= 0.5;
                    }
                }
            }

            return voidSpheres;
        }

        private bool OverlapsAnySphere(VVector center, double radius, List<SphereModel> spheres)
        {
            foreach (var sphere in spheres)
            {
                if (CalculateDistance(center, sphere.Center) < radius + sphere.Radius)
                    return true;
            }

            return false;
        }

        private List<VVector> GetIcosahedronVertices()
        {
            double phi = (1.0 + Math.Sqrt(5.0)) / 2.0;

            return new List<VVector>
            {
                new VVector(-1, phi, 0), new VVector( 1, phi, 0),
                new VVector(-1, -phi, 0), new VVector( 1, -phi, 0),
                new VVector(0, -1, phi), new VVector(0, 1, phi),
                new VVector(0, -1, -phi), new VVector(0, 1, -phi),
                new VVector( phi, 0, -1), new VVector( phi, 0, 1),
                new VVector(-phi, 0, -1), new VVector(-phi, 0, 1)
            };
        }

        private double CalculatePackingEfficiency(List<VVector> centers, double sphereRadius, Rect3D bounds)
        {
            if (centers.Count == 0)
                return 0;

            double sphereVolume = (4.0 / 3.0) * Math.PI * Math.Pow(sphereRadius, 3);
            double totalSphereVolume = centers.Count * sphereVolume;
            double boundsVolume = bounds.SizeX * bounds.SizeY * bounds.SizeZ;

            return totalSphereVolume / boundsVolume;
        }

        public double CalculateHCPPackingDensity(List<SphereModel> spheres, Rect3D bounds)
        {
            if (spheres.Count == 0)
                return 0;

            double totalSphereVolume = spheres.Count * (4.0 / 3.0) * Math.PI * Math.Pow(spheres[0].Radius, 3);
            double boundsVolume = bounds.SizeX * bounds.SizeY * bounds.SizeZ;

            return totalSphereVolume / boundsVolume;
        }

        private double CalculateDistance(VVector point1, VVector point2)
        {
            return Math.Sqrt(
                Math.Pow(point1.x - point2.x, 2) +
                Math.Pow(point1.y - point2.y, 2) +
                Math.Pow(point1.z - point2.z, 2));
        }
    }
}