using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using SFRThelper.Models;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace SFRThelper.Services
{
    public class ESAPIService : IESAPIService
    {
        private readonly ScriptContext _context;

        public ESAPIService(ScriptContext context)
        {
            _context = context ?? throw new ArgumentNullException("context");
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

                string reason;
                if (!CanModifyStructureSet(out reason))
                    return reason;

                return "Ready to create structures";
            }
            catch (Exception ex)
            {
                return "Error checking status: " + ex.Message;
            }
        }

        public bool TryEnsureWriteAccess(out string error)
        {
            error = null;
            try
            {
                if (_context.Patient == null)
                {
                    error = "No patient is loaded.";
                    return false;
                }
                _context.Patient.BeginModifications();
                return true;
            }
            catch (Exception ex)
            {
                error = "The patient or structure set cannot be modified. It may be locked or approved. " + ex.Message;
                return false;
            }
        }

        public bool CanModifyStructureSet(out string reason)
        {
            reason = null;
            if (_context.Patient == null)
            {
                reason = "No patient is loaded.";
                return false;
            }
            if (_context.StructureSet == null)
            {
                reason = "No structure set is loaded.";
                return false;
            }

            try
            {
                var ss = _context.StructureSet;
                if (_context.Patient.Courses != null)
                {
                    foreach (var course in _context.Patient.Courses)
                    {
                        if (course.PlanSetups == null)
                            continue;
                        foreach (var plan in course.PlanSetups)
                        {
                            if (plan == null || !SameStructureSet(plan.StructureSet))
                                continue;
                            if (plan.ApprovalStatus == PlanSetupApprovalStatus.TreatmentApproved)
                            {
                                reason = "Structure set is used by treatment-approved plan '" + plan.Id
                                    + "'. Unapprove the plan before generating structures.";
                                return false;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("CanModifyStructureSet: " + ex.Message);
            }

            return true;
        }

        public IReadOnlyList<StructureListItem> GetTargetStructures()
        {
            var items = new List<StructureListItem>();
            try
            {
                if (_context.StructureSet == null)
                    return items;

                foreach (var s in _context.StructureSet.Structures.Where(s => s != null && !s.IsEmpty))
                    items.Add(ToListItem(s));

                return items
                    .OrderByDescending(IsTargetLike)
                    .ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetTargetStructures: " + ex.Message);
                return items;
            }
        }

        public IReadOnlyList<StructureListItem> GetAvoidanceStructures()
        {
            var items = new List<StructureListItem> { StructureListItem.None() };
            try
            {
                if (_context.StructureSet == null)
                    return items;

                foreach (var s in _context.StructureSet.Structures
                    .Where(s => s != null && !s.IsEmpty)
                    .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
                {
                    items.Add(ToListItem(s));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetAvoidanceStructures: " + ex.Message);
            }
            return items;
        }

        public IReadOnlyList<PlanListItem> GetEvaluablePlans()
        {
            var items = new List<PlanListItem>();
            try
            {
                if (_context.Patient == null || _context.Patient.Courses == null)
                    return items;

                foreach (var course in _context.Patient.Courses)
                {
                    if (course.PlanSetups != null)
                    {
                        foreach (var plan in course.PlanSetups)
                        {
                            if (plan == null || !SameStructureSet(plan.StructureSet))
                                continue;
                            items.Add(new PlanListItem
                            {
                                Key = PlanKey("PlanSetup", course.Id, plan.Id),
                                CourseId = course.Id,
                                PlanId = plan.Id,
                                DisplayName = course.Id + " / " + plan.Id + (plan.IsDoseValid ? "  (calculated)" : "  (no dose)"),
                                IsPlanSum = false,
                                IsDoseValid = plan.IsDoseValid,
                                PrescriptionDoseGy = ToGyNullable(plan.TotalDose)
                            });
                        }
                    }

                    if (course.PlanSums != null)
                    {
                        foreach (var sum in course.PlanSums)
                        {
                            if (sum == null)
                                continue;
                            try
                            {
                                if (!SameStructureSet(sum.StructureSet))
                                    continue;
                            }
                            catch
                            {
                                continue;
                            }

                            // PlanSum has no IsDoseValid in ESAPI 16.1; PlanningItem.Dose is the equivalent signal.
                            bool doseValid = sum.Dose != null;

                            items.Add(new PlanListItem
                            {
                                Key = PlanKey("PlanSum", course.Id, sum.Id),
                                CourseId = course.Id,
                                PlanId = sum.Id,
                                DisplayName = course.Id + " / " + sum.Id + " (PlanSum)"
                                    + (doseValid ? "  (calculated)" : "  (no dose)"),
                                IsPlanSum = true,
                                IsDoseValid = doseValid,
                                PrescriptionDoseGy = null
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetEvaluablePlans: " + ex.Message);
            }

            return items;
        }

        public StructureListItem GetStructureInfo(string structureId)
        {
            var s = GetStructure(structureId);
            return s == null ? null : ToListItem(s);
        }

        public ImageGeometryDto GetImageGeometry()
        {
            var image = _context.Image;
            if (image == null)
                return new ImageGeometryDto { XRes = 1, YRes = 1, ZRes = 1 };

            return new ImageGeometryDto
            {
                OriginX = image.Origin.x,
                OriginY = image.Origin.y,
                OriginZ = image.Origin.z,
                XRes = image.XRes,
                YRes = image.YRes,
                ZRes = image.ZRes,
                XSize = image.XSize,
                YSize = image.YSize,
                ZSize = image.ZSize
            };
        }

        public IReadOnlyList<IReadOnlyList<Point3D>> GetStructureContoursOnSlice(string structureId, int sliceIndex)
        {
            var result = new List<IReadOnlyList<Point3D>>();
            var structure = GetStructure(structureId);
            if (structure == null || _context.Image == null)
                return result;

            try
            {
                var contours = structure.GetContoursOnImagePlane(sliceIndex);
                if (contours == null)
                    return result;
                foreach (var contour in contours)
                {
                    if (contour == null || contour.Length < 3)
                        continue;
                    var points = new List<Point3D>(contour.Length);
                    for (int i = 0; i < contour.Length; i++)
                        points.Add(new Point3D(contour[i].x, contour[i].y, contour[i].z));
                    result.Add(points);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetStructureContoursOnSlice: " + ex.Message);
            }

            return result;
        }

        public LatticeGeometryContext ExtractLatticeGeometry(
            SFRTParameters parameters,
            IProgress<string> progress,
            CancellationToken token)
        {
            var context = new LatticeGeometryContext
            {
                Image = GetImageGeometry(),
                TargetId = parameters != null ? parameters.SelectedTargetId : null,
                Oar1Id = parameters != null && parameters.HasOar1 ? parameters.Oar1StructureId : null,
                Oar2Id = parameters != null && parameters.HasOar2 ? parameters.Oar2StructureId : null,
                ValidVolume = VoxelMask.Empty()
            };

            if (parameters == null)
            {
                context.Message = "Parameters were not provided.";
                return context;
            }

            var target = GetStructure(parameters.SelectedTargetId);
            if (target == null)
            {
                context.Message = "Selected target was not found.";
                return context;
            }
            if (target.IsEmpty)
            {
                context.Message = "Selected target is empty.";
                return context;
            }

            Structure oar1 = parameters.HasOar1 ? GetStructure(parameters.Oar1StructureId) : null;
            Structure oar2 = parameters.HasOar2 ? GetStructure(parameters.Oar2StructureId) : null;
            if (parameters.HasOar1 && oar1 == null)
            {
                context.Message = "OAR Avoidance 1 was not found.";
                return context;
            }
            if (parameters.HasOar2 && oar2 == null)
            {
                context.Message = "OAR Avoidance 2 was not found.";
                return context;
            }

            context.TargetVolumeCc = target.Volume;
            context.TargetIsHighResolution = target.IsHighResolution;
            context.CenterOfMass = ToPoint(target.CenterPoint);
            context.TargetBounds = ToBounds(target);
            context.Transform = LatticeTransform.Identity(context.CenterOfMass);

            string writeError;
            if (!TryEnsureWriteAccess(out writeError))
            {
                context.Message = writeError;
                return context;
            }

            var temps = new StructureTransaction();
            try
            {
                Report(progress, "Building V_valid with geometric margins...");
                token.ThrowIfCancellationRequested();

                bool highRes = target.IsHighResolution
                    || (oar1 != null && oar1.IsHighResolution)
                    || (oar2 != null && oar2.IsHighResolution);

                Structure workTarget = EnsureWorkingCopy(target, "zzSFRTt", highRes, temps);
                SegmentVolume contracted = workTarget.Margin(-parameters.TargetContractionMm);

                SegmentVolume exclusion = null;
                if (oar1 != null)
                {
                    Structure workOar1 = EnsureWorkingCopy(oar1, "zzSFRTo1", highRes, temps);
                    SegmentVolume expanded = workOar1.Margin(parameters.Oar1ExpansionMm);
                    exclusion = expanded;
                }
                if (oar2 != null)
                {
                    Structure workOar2 = EnsureWorkingCopy(oar2, "zzSFRTo2", highRes, temps);
                    SegmentVolume expanded = workOar2.Margin(parameters.Oar2ExpansionMm);
                    exclusion = exclusion == null ? expanded : exclusion.Or(expanded);
                }

                SegmentVolume valid = exclusion != null ? contracted.Sub(exclusion) : contracted;

                string tempId = StructureNaming.NextAvailable(StructureNaming.TempValidId, ExistingIds());
                Structure host = _context.StructureSet.AddStructure("CONTROL", tempId);
                temps.Track(host.Id);
                if (highRes && !host.IsHighResolution)
                    host.ConvertToHighResolution();
                host.SegmentVolume = valid;

                if (host.IsEmpty)
                {
                    context.Message = "V_valid is empty after applying target contraction and OAR expansions.";
                    context.ValidVolume = VoxelMask.Empty();
                    return context;
                }

                context.ValidBounds = ToBounds(host);
                context.ValidVolumeCc = host.Volume;
                Report(progress, "Rasterizing V_valid for CPU packing...");
                context.ValidVolume = Rasterize(host, parameters, token, progress);
                context.Message = "Extracted V_valid (" + context.ValidVolume.OccupiedCount + " voxels, "
                    + context.ValidVolume.VolumeCc.ToString("F2") + " cc).";
                return context;
            }
            catch (OperationCanceledException)
            {
                context.Message = "Geometry extraction was cancelled.";
                throw;
            }
            catch (Exception ex)
            {
                context.Message = "Failed to extract lattice geometry: " + ex.Message;
                context.ValidVolume = VoxelMask.Empty();
                return context;
            }
            finally
            {
                RollbackTransaction(temps);
            }
        }

        public StructureCreationResult CreateLatticeStructures(
            IReadOnlyList<SphereModel> spheres,
            SFRTParameters parameters,
            IProgress<string> progress,
            CancellationToken token)
        {
            var result = new StructureCreationResult();
            if (spheres == null || spheres.Count == 0)
            {
                result.Message = "No spheres to create.";
                return result;
            }
            if (parameters == null)
            {
                result.Message = "Parameters were not provided.";
                return result;
            }

            string writeError;
            if (!TryEnsureWriteAccess(out writeError))
            {
                result.Message = writeError;
                return result;
            }

            string lockedReason;
            if (!CanModifyStructureSet(out lockedReason))
            {
                result.Message = lockedReason;
                return result;
            }

            var target = GetStructure(parameters.SelectedTargetId);
            if (target == null)
            {
                result.Message = "Selected target was not found.";
                return result;
            }

            var transaction = new StructureTransaction();
            try
            {
                bool highRes = target.IsHighResolution;
                var existing = ExistingIds();
                bool createIndividuals = parameters.GenerationMode == SphereGenerationMode.IndividualAndComposite;
                var peakStructures = new List<Structure>();

                if (createIndividuals)
                {
                    for (int i = 0; i < spheres.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        Report(progress, "Creating " + spheres[i].Id + " (" + (i + 1) + "/" + spheres.Count + ")...");
                        string preferred = string.IsNullOrEmpty(spheres[i].Id)
                            ? StructureNaming.FormatPeakId(i + 1)
                            : spheres[i].Id;
                        string id = StructureNaming.NextAvailable(preferred, existing);
                        Structure peak = _context.StructureSet.AddStructure("CONTROL", id);
                        transaction.Track(peak.Id);
                        existing.Add(peak.Id);
                        peak.Color = Colors.Gold;
                        if (highRes && !peak.IsHighResolution)
                            peak.ConvertToHighResolution();
                        AddSphereContours(peak, spheres[i]);
                        peakStructures.Add(peak);
                    }
                }

                token.ThrowIfCancellationRequested();
                Report(progress, "Creating composite Lattice_Peaks...");
                string peaksId = StructureNaming.NextAvailable(StructureNaming.CompositePeaksId, existing);
                Structure composite = _context.StructureSet.AddStructure("CONTROL", peaksId);
                transaction.Track(composite.Id);
                existing.Add(composite.Id);
                composite.Color = Colors.OrangeRed;
                if (highRes && !composite.IsHighResolution)
                    composite.ConvertToHighResolution();

                if (createIndividuals && peakStructures.Count > 0)
                {
                    SegmentVolume union = peakStructures[0].SegmentVolume;
                    for (int i = 1; i < peakStructures.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        union = union.Or(peakStructures[i].SegmentVolume);
                    }
                    composite.SegmentVolume = union;
                }
                else
                {
                    AddSphereContours(composite, spheres);
                }

                token.ThrowIfCancellationRequested();
                Report(progress, "Creating Lattice_Valley = Target \\ Lattice_Peaks...");
                string valleyId = StructureNaming.NextAvailable(StructureNaming.ValleyId, existing);
                Structure valley = _context.StructureSet.AddStructure("CONTROL", valleyId);
                transaction.Track(valley.Id);
                valley.Color = Colors.DeepSkyBlue;
                if (highRes && !valley.IsHighResolution)
                    valley.ConvertToHighResolution();

                valley.SegmentVolume = target.SegmentVolume;
                valley.SegmentVolume = valley.Sub(composite.SegmentVolume);

                transaction.Commit();
                result.Success = true;
                result.CreatedStructureIds = new List<string>(transaction.CreatedIds);
                result.CompositePeaksId = composite.Id;
                result.ValleyId = valley.Id;
                result.Message = createIndividuals
                    ? "Created " + peakStructures.Count + " Peak structures, " + composite.Id + ", and " + valley.Id + "."
                    : "Created composite " + composite.Id + " and " + valley.Id + ".";
                return result;
            }
            catch (OperationCanceledException)
            {
                RollbackTransaction(transaction);
                result.RolledBack = true;
                result.Message = "Generation aborted. Newly created structures were rolled back.";
                return result;
            }
            catch (Exception ex)
            {
                RollbackTransaction(transaction);
                result.RolledBack = true;
                result.Message = "Structure generation failed and was rolled back: " + ex.Message;
                return result;
            }
        }

        public void RollbackCreatedStructures(IEnumerable<string> structureIds)
        {
            if (structureIds == null || _context.StructureSet == null)
                return;
            var transaction = new StructureTransaction();
            foreach (var id in structureIds)
                transaction.Track(id);
            RollbackTransaction(transaction);
        }

        public EvaluationDoseContext ExtractEvaluationDoseContext(string planKey, SFRTParameters parameters)
        {
            var ctx = new EvaluationDoseContext();
            PlanningItem item;
            PlanListItem planInfo;
            if (!TryFindPlanningItem(planKey, out item, out planInfo))
            {
                ctx.ErrorMessage = "The selected plan was not found.";
                return ctx;
            }

            ctx.Plan = planInfo;
            if (item == null || !planInfo.IsDoseValid)
            {
                ctx.ErrorMessage = "The selected plan does not have a valid calculated dose.";
                return ctx;
            }

            var ss = _context.StructureSet;
            Structure target = parameters != null ? GetStructure(parameters.SelectedTargetId) : null;
            Structure peaks = FindByIdOrPrefix(StructureNaming.CompositePeaksId, "Lattice_Pe");
            Structure valley = FindByIdOrPrefix(StructureNaming.ValleyId, "Lattice_Va");

            if (target != null)
                ctx.Target = Snapshot(item, target, planInfo.PrescriptionDoseGy);
            if (peaks != null)
                ctx.Peaks = Snapshot(item, peaks, planInfo.PrescriptionDoseGy);
            if (valley != null)
                ctx.Valley = Snapshot(item, valley, planInfo.PrescriptionDoseGy);

            foreach (var s in ss.Structures.Where(s => s != null && StructureNaming.IsIndividualPeakId(s.Id)))
                ctx.IndividualPeaks.Add(Snapshot(item, s, planInfo.PrescriptionDoseGy));

            if (parameters != null && parameters.HasOar1)
            {
                var oar = GetStructure(parameters.Oar1StructureId);
                if (oar != null)
                    ctx.Oars.Add(Snapshot(item, oar, planInfo.PrescriptionDoseGy));
            }
            if (parameters != null && parameters.HasOar2)
            {
                var oar = GetStructure(parameters.Oar2StructureId);
                if (oar != null)
                    ctx.Oars.Add(Snapshot(item, oar, planInfo.PrescriptionDoseGy));
            }

            if (ctx.Peaks == null)
                ctx.ErrorMessage = "Lattice_Peaks was not found on this structure set. Generate lattice structures first.";
            else if (ctx.Valley == null)
                ctx.ErrorMessage = "Lattice_Valley was not found on this structure set. Generate lattice structures first.";

            return ctx;
        }

        private StructureDoseSnapshot Snapshot(PlanningItem item, Structure structure, double? rxGy)
        {
            var snap = new StructureDoseSnapshot
            {
                Id = structure.Id,
                VolumeCc = structure.Volume,
                DmaxGy = double.NaN,
                DmeanGy = double.NaN,
                DminGy = double.NaN,
                D2Gy = double.NaN,
                D5Gy = double.NaN,
                D10Gy = double.NaN,
                D90Gy = double.NaN,
                D95Gy = double.NaN,
                D98Gy = double.NaN,
                DmaxPercent = double.NaN,
                DmeanPercent = double.NaN,
                DminPercent = double.NaN,
                D2Percent = double.NaN,
                D5Percent = double.NaN,
                D10Percent = double.NaN,
                D90Percent = double.NaN,
                D95Percent = double.NaN,
                D98Percent = double.NaN,
                V100Percent = double.NaN,
                D003CcGy = double.NaN
            };

            try
            {
                var dvhAbs = item.GetDVHCumulativeData(structure, DoseValuePresentation.Absolute, VolumePresentation.Relative, 0.1);
                if (dvhAbs == null)
                    return snap;

                snap.HasDose = true;
                snap.DmaxGy = ToGy(dvhAbs.MaxDose);
                snap.DmeanGy = ToGy(dvhAbs.MeanDose);
                snap.DminGy = ToGy(dvhAbs.MinDose);
                snap.D2Gy = DoseAtVolumeGy(item, structure, 2.0);
                snap.D5Gy = DoseAtVolumeGy(item, structure, 5.0);
                snap.D10Gy = DoseAtVolumeGy(item, structure, 10.0);
                snap.D90Gy = DoseAtVolumeGy(item, structure, 90.0);
                snap.D95Gy = DoseAtVolumeGy(item, structure, 95.0);
                snap.D98Gy = DoseAtVolumeGy(item, structure, 98.0);

                // ESAPI 16.1: only absolute dose is supported for PlanSums.
                if (!(item is PlanSum))
                {
                    var dvhRel = item.GetDVHCumulativeData(structure, DoseValuePresentation.Relative, VolumePresentation.Relative, 0.1);
                    if (dvhRel != null)
                    {
                        snap.DmaxPercent = dvhRel.MaxDose.Dose;
                        snap.DmeanPercent = dvhRel.MeanDose.Dose;
                        snap.DminPercent = dvhRel.MinDose.Dose;
                    }
                    snap.D2Percent = DoseAtVolumePercent(item, structure, 2.0);
                    snap.D5Percent = DoseAtVolumePercent(item, structure, 5.0);
                    snap.D10Percent = DoseAtVolumePercent(item, structure, 10.0);
                    snap.D90Percent = DoseAtVolumePercent(item, structure, 90.0);
                    snap.D95Percent = DoseAtVolumePercent(item, structure, 95.0);
                    snap.D98Percent = DoseAtVolumePercent(item, structure, 98.0);
                }

                if (rxGy.HasValue && rxGy.Value > 0)
                {
                    try
                    {
                        snap.V100Percent = item.GetVolumeAtDose(
                            structure,
                            new DoseValue(rxGy.Value, DoseValue.DoseUnit.Gy),
                            VolumePresentation.Relative);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("V100%: " + ex.Message);
                    }
                }

                try
                {
                    if (structure.Volume >= 0.03)
                    {
                        snap.D003CcGy = ToGy(item.GetDoseAtVolume(
                            structure, 0.03, VolumePresentation.AbsoluteCm3, DoseValuePresentation.Absolute));
                    }
                    else
                    {
                        snap.D003CcGy = snap.DmaxGy;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("D0.03cc: " + ex.Message);
                    snap.D003CcGy = snap.DmaxGy;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Snapshot " + structure.Id + ": " + ex.Message);
            }

            return snap;
        }

        private double DoseAtVolumeGy(PlanningItem item, Structure structure, double volumePercent)
        {
            try
            {
                return ToGy(item.GetDoseAtVolume(structure, volumePercent, VolumePresentation.Relative, DoseValuePresentation.Absolute));
            }
            catch
            {
                return double.NaN;
            }
        }

        private double DoseAtVolumePercent(PlanningItem item, Structure structure, double volumePercent)
        {
            try
            {
                DoseValue dv = item.GetDoseAtVolume(structure, volumePercent, VolumePresentation.Relative, DoseValuePresentation.Relative);
                return dv.Dose;
            }
            catch
            {
                return double.NaN;
            }
        }

        private VoxelMask Rasterize(Structure host, SFRTParameters parameters, CancellationToken token, IProgress<string> progress)
        {
            var bounds = ToBounds(host);
            if (bounds.IsEmpty)
                return VoxelMask.Empty();

            double imageMin = 1.0;
            if (_context.Image != null)
                imageMin = Math.Min(_context.Image.XRes, Math.Min(_context.Image.YRes, _context.Image.ZRes));

            double step = Math.Max(1.0, Math.Min(2.0, Math.Min(parameters.SphereRadiusMm * 0.5, imageMin)));
            const int maxDim = 192;
            int nx = Math.Max(1, (int)Math.Ceiling(bounds.SizeX / step));
            int ny = Math.Max(1, (int)Math.Ceiling(bounds.SizeY / step));
            int nz = Math.Max(1, (int)Math.Ceiling(bounds.SizeZ / step));
            int maxN = Math.Max(nx, Math.Max(ny, nz));
            if (maxN > maxDim)
            {
                step *= (double)maxN / maxDim;
                nx = Math.Max(1, (int)Math.Ceiling(bounds.SizeX / step));
                ny = Math.Max(1, (int)Math.Ceiling(bounds.SizeY / step));
                nz = Math.Max(1, (int)Math.Ceiling(bounds.SizeZ / step));
            }

            var mask = new VoxelMask(bounds.MinX, bounds.MinY, bounds.MinZ, step, step, step, nx, ny, nz);
            for (int iz = 0; iz < nz; iz++)
            {
                token.ThrowIfCancellationRequested();
                if (iz % 8 == 0)
                    Report(progress, "Rasterizing V_valid slice " + (iz + 1) + "/" + nz + "...");
                double z = bounds.MinZ + (iz + 0.5) * step;
                for (int iy = 0; iy < ny; iy++)
                {
                    double y = bounds.MinY + (iy + 0.5) * step;
                    for (int ix = 0; ix < nx; ix++)
                    {
                        double x = bounds.MinX + (ix + 0.5) * step;
                        try
                        {
                            if (host.IsPointInsideSegment(new VVector(x, y, z)))
                                mask.Set(ix, iy, iz, true);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            return mask;
        }

        private Structure EnsureWorkingCopy(Structure source, string preferredId, bool highRes, StructureTransaction temps)
        {
            if (source == null)
                return null;
            if (!highRes || source.IsHighResolution == highRes)
                return source;

            string id = StructureNaming.NextAvailable(preferredId, ExistingIds());
            Structure copy = _context.StructureSet.AddStructure("CONTROL", id);
            temps.Track(copy.Id);
            copy.SegmentVolume = source.SegmentVolume;
            if (highRes && !copy.IsHighResolution)
                copy.ConvertToHighResolution();
            return copy;
        }

        private void AddSphereContours(Structure structure, SphereModel sphere)
        {
            AddSphereContours(structure, new[] { sphere });
        }

        private void AddSphereContours(Structure structure, IReadOnlyList<SphereModel> spheres)
        {
            var image = _context.Image;
            if (image == null || structure == null || spheres == null || spheres.Count == 0)
                return;

            double globalMinZ = spheres.Min(s => s.Center.Z - s.Radius - 1.0);
            double globalMaxZ = spheres.Max(s => s.Center.Z + s.Radius + 1.0);

            for (int sliceIndex = 0; sliceIndex < image.ZSize; sliceIndex++)
            {
                double z = image.Origin.z + sliceIndex * image.ZRes;
                if (z < globalMinZ || z > globalMaxZ)
                    continue;

                foreach (var sphere in spheres)
                {
                    foreach (var contour in GenerateCircleContour(sphere.Center, sphere.Radius, z, image.XRes, image.YRes))
                    {
                        if (contour.Length >= 3)
                            structure.AddContourOnImagePlane(contour, sliceIndex);
                    }
                }
            }
        }

        private static List<VVector[]> GenerateCircleContour(Point3D center, double radius, double z, double xRes, double yRes)
        {
            var contours = new List<VVector[]>();
            double deltaZ = Math.Abs(z - center.Z);
            if (deltaZ > radius + 0.1)
                return contours;

            double circleRadius = Math.Sqrt(Math.Max(0, radius * radius - deltaZ * deltaZ));
            if (circleRadius < 0.1)
                return contours;

            double circumference = 2 * Math.PI * circleRadius;
            double minRes = Math.Min(xRes, yRes);
            int basePoints = Math.Max(16, (int)(circumference / Math.Max(minRes, 0.5)));
            int numPoints = Math.Min(basePoints, 120);
            numPoints = ((numPoints + 3) / 4) * 4;

            var points = new VVector[numPoints];
            for (int i = 0; i < numPoints; i++)
            {
                double angle = 2.0 * Math.PI * i / numPoints;
                points[i] = new VVector(
                    center.X + circleRadius * Math.Cos(angle),
                    center.Y + circleRadius * Math.Sin(angle),
                    z);
            }
            contours.Add(points);
            return contours;
        }

        private void RollbackTransaction(StructureTransaction transaction)
        {
            if (transaction == null || _context.StructureSet == null)
                return;
            foreach (var id in transaction.RollbackOrder())
            {
                try
                {
                    Structure s = _context.StructureSet.Structures.FirstOrDefault(x => x.Id == id);
                    if (s != null)
                        _context.StructureSet.RemoveStructure(s);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Rollback failed for " + id + ": " + ex.Message);
                }
            }
            transaction.MarkRolledBack();
        }

        private HashSet<string> ExistingIds()
        {
            return new HashSet<string>(_context.StructureSet.Structures.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
        }

        private Structure GetStructure(string id)
        {
            if (string.IsNullOrEmpty(id) || _context.StructureSet == null)
                return null;
            return _context.StructureSet.Structures.FirstOrDefault(s => s.Id == id);
        }

        private Structure FindByIdOrPrefix(string preferred, string prefix)
        {
            var ss = _context.StructureSet;
            if (ss == null)
                return null;
            return ss.Structures.FirstOrDefault(s => s.Id == preferred)
                ?? ss.Structures.FirstOrDefault(s => s.Id != null && s.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private bool TryFindPlanningItem(string key, out PlanningItem item, out PlanListItem info)
        {
            item = null;
            info = GetEvaluablePlans().FirstOrDefault(p => p.Key == key);
            if (info == null || _context.Patient == null)
                return false;

            // C# forbids capturing out/ref parameters in lambdas (CS1628).
            string courseId = info.CourseId;
            string planId = info.PlanId;
            bool isPlanSum = info.IsPlanSum;

            foreach (var course in _context.Patient.Courses)
            {
                if (!string.Equals(course.Id, courseId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (isPlanSum)
                    item = course.PlanSums.FirstOrDefault(s => s.Id == planId);
                else
                    item = course.PlanSetups.FirstOrDefault(s => s.Id == planId);
                if (item != null)
                    return true;
            }
            return false;
        }

        private bool SameStructureSet(StructureSet other)
        {
            if (other == null || _context.StructureSet == null)
                return false;
            if (ReferenceEquals(other, _context.StructureSet))
                return true;
            try
            {
                return other.UID == _context.StructureSet.UID;
            }
            catch
            {
                return string.Equals(other.Id, _context.StructureSet.Id, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static StructureListItem ToListItem(Structure s)
        {
            return new StructureListItem
            {
                Id = s.Id,
                DisplayName = s.Id + (string.IsNullOrEmpty(s.DicomType) ? string.Empty : "  [" + s.DicomType + "]"),
                DicomType = s.DicomType,
                VolumeCc = s.Volume,
                IsHighResolution = s.IsHighResolution,
                IsEmpty = s.IsEmpty
            };
        }

        private static bool IsTargetLike(StructureListItem item)
        {
            string id = (item.Id ?? string.Empty).ToUpperInvariant();
            string t = (item.DicomType ?? string.Empty).ToUpperInvariant();
            return t == "GTV" || t == "CTV" || t == "PTV"
                || id.Contains("GTV") || id.Contains("CTV") || id.Contains("PTV");
        }

        private static BoundingBox3D ToBounds(Structure s)
        {
            try
            {
                var b = s.MeshGeometry.Bounds;
                return BoundingBox3D.FromMinMax(b.X, b.Y, b.Z, b.X + b.SizeX, b.Y + b.SizeY, b.Z + b.SizeZ);
            }
            catch
            {
                Point3D c = ToPoint(s.CenterPoint);
                return BoundingBox3D.FromMinMax(c.X - 10, c.Y - 10, c.Z - 10, c.X + 10, c.Y + 10, c.Z + 10);
            }
        }

        private static Point3D ToPoint(VVector v)
        {
            return new Point3D(v.x, v.y, v.z);
        }

        private static string PlanKey(string kind, string courseId, string planId)
        {
            return kind + "|" + courseId + "|" + planId;
        }

        private static double ToGy(DoseValue dv)
        {
            if (dv.Unit == DoseValue.DoseUnit.cGy)
                return dv.Dose / 100.0;
            if (dv.Unit == DoseValue.DoseUnit.Gy)
                return dv.Dose;
            return dv.Dose;
        }

        private static double? ToGyNullable(DoseValue dv)
        {
            try
            {
                double gy = ToGy(dv);
                if (gy <= 0 || double.IsNaN(gy) || double.IsInfinity(gy))
                    return null;
                return gy;
            }
            catch
            {
                return null;
            }
        }

        private static void Report(IProgress<string> progress, string message)
        {
            if (progress != null)
                progress.Report(message);
            PumpUi();
        }

        /// <summary>
        /// Lets the WPF message pump run so Cancel can be processed while ESAPI STA work is in progress.
        /// </summary>
        private static void PumpUi()
        {
            try
            {
                var dispatcher = System.Windows.Threading.Dispatcher.FromThread(System.Threading.Thread.CurrentThread);
                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                    dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            }
            catch
            {
            }
        }
    }
}
