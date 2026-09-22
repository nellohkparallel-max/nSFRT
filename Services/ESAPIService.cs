using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using SFRThelper.Helpers;
using SFRThelper.Models;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace SFRThelper.Services
{
    public class ESAPIService : IESAPIService
    {
        private readonly ScriptContext _context;
        private readonly Application _application;
        private Patient _standalonePatient;
        private StructureSet _standaloneStructureSet;
        private readonly IEsapiWorker _worker;

        public ESAPIService(ScriptContext context)
            : this(context, new EsapiWorker(System.Windows.Threading.Dispatcher.CurrentDispatcher))
        {
        }

        public ESAPIService(ScriptContext context, IEsapiWorker worker)
        {
            _context = context ?? throw new ArgumentNullException("context");
            _worker = worker;
        }

        public ESAPIService(Application application, IEsapiWorker worker)
        {
            _application = application ?? throw new ArgumentNullException("application");
            _worker = worker;
        }

        public bool IsStandalone
        {
            get { return _application != null; }
        }

        public IEsapiWorker Worker
        {
            get { return _worker; }
        }

        public void AttachStandalone(Patient patient, StructureSet structureSet)
        {
            _standalonePatient = patient;
            _standaloneStructureSet = structureSet;
        }

        private Patient CurrentPatient
        {
            get { return _context != null ? _context.Patient : _standalonePatient; }
        }

        private StructureSet CurrentStructureSet
        {
            get { return _context != null ? _context.StructureSet : _standaloneStructureSet; }
        }

        private VMS.TPS.Common.Model.API.Image CurrentImage
        {
            get
            {
                if (_context != null)
                    return _context.Image;
                return _standaloneStructureSet != null ? _standaloneStructureSet.Image : null;
            }
        }

        public string GetPatientStatus()
        {
            try
            {
                if (CurrentPatient == null)
                    return "No patient loaded";
                if (CurrentStructureSet == null)
                    return "No structure set loaded";
                if (CurrentImage == null)
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
                if (CurrentPatient == null)
                {
                    error = "No patient is loaded.";
                    return false;
                }
                CurrentPatient.BeginModifications();
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
            if (CurrentPatient == null)
            {
                reason = "No patient is loaded.";
                return false;
            }
            if (CurrentStructureSet == null)
            {
                reason = "No structure set is loaded.";
                return false;
            }

            try
            {
                var ss = CurrentStructureSet;
                if (CurrentPatient.Courses != null)
                {
                    foreach (var course in CurrentPatient.Courses)
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
                if (CurrentStructureSet == null)
                    return items;

                foreach (var s in CurrentStructureSet.Structures.Where(s => s != null && !s.IsEmpty))
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
                if (CurrentStructureSet == null)
                    return items;

                foreach (var s in CurrentStructureSet.Structures
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
                if (CurrentPatient == null || CurrentPatient.Courses == null)
                    return items;

                foreach (var course in CurrentPatient.Courses)
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
            var image = CurrentImage;
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
            if (structure == null || CurrentImage == null)
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
            context.Transform = LatticeTransform.FromYawDegrees(context.CenterOfMass, parameters.GridRotationDeg);

            Structure body = FindBodyStructure();
            context.BodyId = body != null ? body.Id : null;

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
                    || (oar2 != null && oar2.IsHighResolution)
                    || (body != null && body.IsHighResolution);

                Structure workTarget = EnsureWorkingCopy(target, "zzSFRTt", highRes, temps);
                SegmentVolume contracted = workTarget.Margin(-parameters.TargetContractionMm);

                if (body != null)
                {
                    Structure workBody = EnsureWorkingCopy(body, "zzSFRTb", highRes, temps);
                    SegmentVolume skinSafe = workBody.Margin(-parameters.SkinContractionMm);
                    contracted = contracted.And(skinSafe);
                }

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
                Structure host = CurrentStructureSet.AddStructure("CONTROL", tempId);
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
            var included = FilterIncludedSpheres(spheres);
            if (included.Count == 0)
            {
                result.Message = "No included spheres to create. Check at least one Peak_xx row.";
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

            string targetId = parameters.SelectedTargetId;
            Structure target = Refetch(targetId);
            if (target == null)
            {
                result.Message = "Selected target was not found.";
                return result;
            }

            var transaction = new StructureTransaction();
            try
            {
                target = EnsureHighResolutionById(targetId, true);
                bool highRes = target != null && target.IsHighResolution;
                var existing = ExistingIds();
                bool createIndividuals = parameters.GenerationMode == SphereGenerationMode.IndividualAndComposite;
                var peakIds = new List<string>();

                if (createIndividuals)
                {
                    for (int i = 0; i < included.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        Report(progress, "Creating " + included[i].Id + " (" + (i + 1) + "/" + included.Count + ")...");
                        string preferred = string.IsNullOrEmpty(included[i].Id)
                            ? StructureNaming.FormatPeakId(i + 1)
                            : included[i].Id;
                        string id = StructureNaming.NextAvailable(preferred, existing);
                        AddTypedStructure(StructureNaming.DicomControl, id, transaction, existing, highRes);
                        Structure peak = Refetch(id);
                        if (peak != null)
                            peak.Color = Colors.Gold;
                        peak = EnsureHighResolutionById(id, highRes);
                        AddSphereContours(peak, included[i]);
                        peakIds.Add(id);
                    }
                }

                token.ThrowIfCancellationRequested();
                Report(progress, "Creating composite Lattice_Peaks...");
                string peaksId = StructureNaming.NextAvailable(StructureNaming.CompositePeaksId, existing);
                AddTypedStructure(StructureNaming.DicomControl, peaksId, transaction, existing, highRes);
                Structure composite = Refetch(peaksId);
                if (composite != null)
                    composite.Color = Colors.Red;
                composite = EnsureHighResolutionById(peaksId, highRes);

                if (createIndividuals && peakIds.Count > 0)
                {
                    SegmentVolume union = null;
                    for (int i = 0; i < peakIds.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        Structure peak = Refetch(peakIds[i]);
                        if (peak == null)
                            continue;
                        union = union == null ? peak.SegmentVolume : union.Or(peak.SegmentVolume);
                    }
                    composite = AssignSegment(peaksId, union);
                }
                else
                {
                    composite = Refetch(peaksId);
                    AddSphereContours(composite, included);
                    composite = Refetch(peaksId);
                }

                token.ThrowIfCancellationRequested();
                Report(progress, "Creating Lattice_Valley = Target \\ Lattice_Peaks...");
                string valleyId = StructureNaming.NextAvailable(StructureNaming.ValleyId, existing);
                AddTypedStructure(StructureNaming.DicomAvoidance, valleyId, transaction, existing, highRes);
                Structure valley = Refetch(valleyId);
                if (valley != null)
                    valley.Color = Colors.Cyan;
                valley = EnsureHighResolutionById(valleyId, highRes);
                target = Refetch(targetId);
                composite = Refetch(peaksId);
                if (target != null && composite != null)
                {
                    valley = AssignSegment(valleyId, target.SegmentVolume);
                    valley = AssignSegment(valleyId, valley.Sub(composite.SegmentVolume));
                }

                string penumbraId = null;
                string coreId = null;
                string ring01Id = null;
                string ring13Id = null;
                string bodyTempId = null;

                if (parameters.GenerateTuningStructures)
                {
                    double dShell = parameters.PenumbraShellThicknessMm;
                    if (dShell < 1.0) dShell = 1.0;
                    if (dShell > 5.0) dShell = 5.0;
                    double ring1Mm = parameters.ConcentricRing1Mm > 0 ? parameters.ConcentricRing1Mm : 10.0;
                    double ring2Mm = parameters.ConcentricRing2Mm > ring1Mm ? parameters.ConcentricRing2Mm : ring1Mm + 20.0;

                    string bodyClipId = null;
                    Structure bodyClip = FindBodyStructure();
                    if (bodyClip != null)
                        bodyClipId = bodyClip.Id;
                    if (bodyClip != null && highRes && !bodyClip.IsHighResolution)
                    {
                        try
                        {
                            bodyTempId = StructureNaming.NextAvailable("zzExtHR", existing);
                            AddTypedStructure(StructureNaming.DicomControl, bodyTempId, transaction, existing, true);
                            Structure bodySrc = Refetch(bodyClipId);
                            Structure bodyTmp = Refetch(bodyTempId);
                            if (bodySrc != null && bodyTmp != null)
                                AssignSegment(bodyTempId, bodySrc.SegmentVolume);
                            EnsureHighResolutionById(bodyTempId, true);
                            bodyClipId = bodyTempId;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine("EXTERNAL HR copy: " + ex.Message);
                            if (!string.IsNullOrEmpty(bodyTempId))
                            {
                                TryRemoveById(bodyTempId);
                                transaction.Untrack(bodyTempId);
                                existing.Remove(bodyTempId);
                                bodyTempId = null;
                            }
                            bodyClip = FindBodyStructure();
                            bodyClipId = bodyClip != null ? bodyClip.Id : null;
                        }
                    }

                    token.ThrowIfCancellationRequested();
                    Report(progress, "Creating Peak_Penumbra = (Peaks.Margin(+d) \\ Peaks) ∩ Target...");
                    penumbraId = StructureNaming.NextAvailable(StructureNaming.PenumbraShellId, existing);
                    AddTypedStructure(StructureNaming.DicomControl, penumbraId, transaction, existing, highRes);
                    Structure penumbra = Refetch(penumbraId);
                    if (penumbra != null)
                        penumbra.Color = Color.FromRgb(255, 140, 0);
                    EnsureHighResolutionById(penumbraId, highRes);
                    composite = Refetch(peaksId);
                    target = Refetch(targetId);
                    if (composite != null && target != null)
                    {
                        SegmentVolume expanded = composite.Margin(dShell);
                        AssignSegment(penumbraId, expanded);
                        penumbra = Refetch(penumbraId);
                        composite = Refetch(peaksId);
                        AssignSegment(penumbraId, penumbra.Sub(composite.SegmentVolume));
                        penumbra = Refetch(penumbraId);
                        target = Refetch(targetId);
                        AssignSegment(penumbraId, penumbra.And(target.SegmentVolume));
                    }

                    token.ThrowIfCancellationRequested();
                    Report(progress, "Creating Valley_Core = Target \\ Peaks.Margin(+d)...");
                    coreId = StructureNaming.NextAvailable(StructureNaming.ValleyCoreId, existing);
                    AddTypedStructure(StructureNaming.DicomAvoidance, coreId, transaction, existing, highRes);
                    Structure valleyCore = Refetch(coreId);
                    if (valleyCore != null)
                        valleyCore.Color = Color.FromRgb(30, 144, 255);
                    EnsureHighResolutionById(coreId, highRes);
                    target = Refetch(targetId);
                    composite = Refetch(peaksId);
                    if (target != null && composite != null)
                    {
                        AssignSegment(coreId, target.SegmentVolume);
                        valleyCore = Refetch(coreId);
                        composite = Refetch(peaksId);
                        AssignSegment(coreId, valleyCore.Sub(composite.Margin(dShell)));
                    }

                    token.ThrowIfCancellationRequested();
                    Report(progress, "Creating Ring_SFRT rings clipped to EXTERNAL...");
                    ring01Id = StructureNaming.NextAvailable(StructureNaming.Ring01Id, existing);
                    AddTypedStructure(StructureNaming.DicomAvoidance, ring01Id, transaction, existing, highRes);
                    Structure ring01 = Refetch(ring01Id);
                    if (ring01 != null)
                        ring01.Color = Color.FromRgb(144, 238, 144);
                    EnsureHighResolutionById(ring01Id, highRes);
                    target = Refetch(targetId);
                    if (target != null)
                    {
                        AssignSegment(ring01Id, target.Margin(ring1Mm));
                        ring01 = Refetch(ring01Id);
                        target = Refetch(targetId);
                        AssignSegment(ring01Id, ring01.Sub(target.SegmentVolume));
                    }
                    ClipToExternalById(ring01Id, bodyClipId);

                    ring13Id = StructureNaming.NextAvailable(StructureNaming.Ring13Id, existing);
                    AddTypedStructure(StructureNaming.DicomAvoidance, ring13Id, transaction, existing, highRes);
                    Structure ring13 = Refetch(ring13Id);
                    if (ring13 != null)
                        ring13.Color = Color.FromRgb(192, 192, 192);
                    EnsureHighResolutionById(ring13Id, highRes);
                    target = Refetch(targetId);
                    if (target != null)
                    {
                        AssignSegment(ring13Id, target.Margin(ring2Mm));
                        ring13 = Refetch(ring13Id);
                        target = Refetch(targetId);
                        AssignSegment(ring13Id, ring13.Sub(target.Margin(ring1Mm)));
                    }
                    ClipToExternalById(ring13Id, bodyClipId);

                    if (!string.IsNullOrEmpty(bodyTempId))
                    {
                        TryRemoveById(bodyTempId);
                        transaction.Untrack(bodyTempId);
                        existing.Remove(bodyTempId);
                        bodyTempId = null;
                    }
                }

                token.ThrowIfCancellationRequested();
                Report(progress, "Creating POI_Peak_Center and POI_Valley_Center markers...");
                string poiPeakId = StructureNaming.NextAvailable(StructureNaming.PoiPeakCenterId, existing);
                AddTypedStructure(StructureNaming.DicomMarker, poiPeakId, transaction, existing, highRes);
                Point3D peakPoi = included[0].Center;
                AddSphereContours(Refetch(poiPeakId), new SphereModel(peakPoi, 1.0, 1) { Id = poiPeakId });

                string poiValleyId = StructureNaming.NextAvailable(StructureNaming.PoiValleyCenterId, existing);
                AddTypedStructure(StructureNaming.DicomMarker, poiValleyId, transaction, existing, highRes);
                Point3D valleyPoi = FindValleyPoi(targetId, included);
                AddSphereContours(Refetch(poiValleyId), new SphereModel(valleyPoi, 1.0, 1) { Id = poiValleyId });

                transaction.Commit();
                result.Success = true;
                result.CreatedStructureIds = new List<string>(transaction.CreatedIds);
                result.CompositePeaksId = peaksId;
                result.ValleyId = valleyId;
                result.PenumbraShellId = penumbraId;
                result.ValleyCoreId = coreId;
                result.Ring01Id = ring01Id;
                result.Ring13Id = ring13Id;
                result.PoiPeakCenterId = poiPeakId;
                result.PoiValleyCenterId = poiValleyId;
                result.Message = BuildCreationMessage(createIndividuals, peakIds.Count, peaksId, valleyId,
                    penumbraId, coreId, ring01Id, ring13Id, poiPeakId, poiValleyId);
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

        public string SetupVmatArcs()
        {
            return SetupVmatArcs(null);
        }

        public string SetupVmatArcs(SFRTParameters parameters)
        {
            try
            {
                string writeError;
                if (!TryEnsureWriteAccess(out writeError))
                    return writeError;

                ExternalPlanSetup plan = ResolveExternalPlan();
                if (plan == null)
                    return "No ExternalPlanSetup is in scope. Open a photon plan before seeding VMAT arcs.";

                LinacEnergyOption machineOpt = ResolveMachineOption(plan, parameters);
                if (machineOpt == null || string.IsNullOrWhiteSpace(machineOpt.MachineId))
                    return "External beam configuration not found. Select a commissioned linac/energy in Plan Automation, or add a treatment beam to the plan so machine parameters can be inherited.";

                VVector iso = ResolveIsocenter(plan);

                string lastError = null;
                if (TryAddVmatPair(plan, machineOpt, iso, out lastError))
                {
                    string jawNote = TryEnableJawTracking(plan);
                    return "Added 2 coplanar VMAT arcs (collimators 15° / 75°, technique ARC, default MLC)"
                        + (string.IsNullOrEmpty(jawNote) ? " and enabled jaw tracking." : ". " + jawNote);
                }

                return "VMAT arc setup failed: External beam configuration not found ("
                    + machineOpt.Display + "). " + (lastError ?? string.Empty);
            }
            catch (Exception ex)
            {
                return "VMAT arc setup failed: " + ex.Message;
            }
        }

        public string SeedPhotonObjectives(SFRTParameters parameters)
        {
            try
            {
                string writeError;
                if (!TryEnsureWriteAccess(out writeError))
                    return writeError;

                ExternalPlanSetup plan = ResolveExternalPlan();
                if (plan == null)
                    return "No ExternalPlanSetup is in scope. Open a photon plan in write mode before seeding PO objectives.";

                if (plan.ApprovalStatus == PlanSetupApprovalStatus.TreatmentApproved)
                {
                    return "Plan '" + plan.Id + "' is treatment-approved and locked. Unapprove the plan before seeding Photon Optimizer objectives.";
                }

                if (plan.OptimizationSetup == null)
                    return "Plan '" + plan.Id + "' does not have an active OptimizationSetup. Initialize optimization on the plan first.";

                if (parameters == null)
                    return "Parameters were not provided.";

                double rxGy = parameters.PrescriptionDoseGy;
                if (rxGy <= 0)
                {
                    try { rxGy = ToGy(plan.TotalDose); }
                    catch { rxGy = 0; }
                }
                if (rxGy <= 0)
                    return "Prescription dose (D_rx) must be greater than 0 Gy.";

                int nfx = parameters.FractionCount;
                if (nfx < 1)
                    nfx = 1;

                Structure peaks = FindByIdOrPrefix(StructureNaming.CompositePeaksId, "Lattice_Pe");
                Structure valleyCore = FindByIdOrPrefix(StructureNaming.ValleyCoreId, "Valley_Co");
                Structure valley = FindByIdOrPrefix(StructureNaming.ValleyId, "Lattice_Va");
                if (peaks == null || (valleyCore == null && valley == null))
                    return "Generate Lattice_Peaks and Valley_Core (or Lattice_Valley) before seeding PO objectives.";

                var opt = plan.OptimizationSetup;

                if (parameters.ClearExistingObjectives)
                {
                    try
                    {
                        var existing = opt.Objectives.ToList();
                        foreach (var obj in existing)
                        {
                            try
                            {
                                opt.RemoveObjective(obj);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine("RemoveObjective: " + ex.Message);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        return "Could not clear existing PO objectives. The plan may be locked or unapproved for editing. " + ex.Message;
                    }
                }

                IList<PoPointObjective> specs = OptimizationObjectivePresets.Build(parameters, rxGy);
                int added = 0;
                bool valleyCoreAdded = false;
                foreach (var spec in specs)
                {
                    if (spec == null)
                        continue;
                    if (spec.Role == "Lattice_Valley" && valleyCoreAdded)
                        continue;

                    Structure structure = FindByIdOrPrefix(spec.StructureId, spec.StructurePrefix);
                    if (structure == null || structure.IsEmpty)
                    {
                        if (spec.Required)
                            return "Required structure '" + spec.StructureId + "' was not found or is empty.";
                        continue;
                    }

                    OptimizationObjectiveOperator op = spec.IsLower
                        ? OptimizationObjectiveOperator.Lower
                        : OptimizationObjectiveOperator.Upper;
                    opt.AddPointObjective(
                        structure,
                        op,
                        new DoseValue(spec.DoseGy, DoseValue.DoseUnit.Gy),
                        spec.VolumePercent,
                        spec.Priority);
                    added++;
                    if (spec.Role == "Valley_Core")
                        valleyCoreAdded = true;
                }

                try
                {
                    opt.AddAutomaticNormalTissueObjective(40);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("NTO: " + ex.Message);
                }

                bool jawEnabled = false;
                string jawNote = null;
                if (parameters.AutoEnableJawTracking)
                {
                    jawNote = TryEnableJawTracking(plan);
                    jawEnabled = string.IsNullOrEmpty(jawNote);
                }

                if (added == 0)
                    return "No PO objectives were inserted. Confirm Lattice_Peaks / Valley_Core (or Lattice_Valley) exist on the structure set.";

                string message = string.Format(CultureInfo.InvariantCulture,
                    "Successfully seeded {0} PO objectives (Rx: {1:0.##} Gy x {2} fx)",
                    added, rxGy, nfx);
                if (jawEnabled)
                    message += " and enabled Jaw Tracking";
                message += " on Plan: " + plan.Id + ".";
                if (!string.IsNullOrEmpty(jawNote))
                    message += " " + jawNote;
                return message;
            }
            catch (Exception ex)
            {
                return "PO objective seeding failed: " + ex.Message;
            }
        }

        public bool HasPhotonSeedingStructures()
        {
            try
            {
                Structure peaks = FindByIdOrPrefix(StructureNaming.CompositePeaksId, "Lattice_Pe");
                Structure valleyCore = FindByIdOrPrefix(StructureNaming.ValleyCoreId, "Valley_Co");
                Structure valley = FindByIdOrPrefix(StructureNaming.ValleyId, "Lattice_Va");
                return peaks != null && (valleyCore != null || valley != null);
            }
            catch
            {
                return false;
            }
        }

        public bool HasActivePlanSetup()
        {
            try
            {
                return ResolveExternalPlan() != null;
            }
            catch
            {
                return false;
            }
        }

        public IReadOnlyList<LinacEnergyOption> GetLinacEnergyOptions()
        {
            var list = new List<LinacEnergyOption>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (CurrentPatient == null || CurrentPatient.Courses == null)
                    return list;
                foreach (var course in CurrentPatient.Courses)
                {
                    if (course.PlanSetups == null)
                        continue;
                    foreach (var p in course.PlanSetups)
                    {
                        var ext = p as ExternalPlanSetup;
                        if (ext == null || ext.Beams == null)
                            continue;
                        foreach (var beam in ext.Beams)
                        {
                            if (beam == null || beam.IsSetupField)
                                continue;
                            LinacEnergyOption opt = FromBeam(beam, ext.Id);
                            if (opt == null || seen.Contains(opt.Key))
                                continue;
                            seen.Add(opt.Key);
                            list.Add(opt);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetLinacEnergyOptions: " + ex.Message);
            }
            return list;
        }

        private ExternalPlanSetup ResolveExternalPlan()
        {
            if (_context != null && _context.ExternalPlanSetup != null)
                return _context.ExternalPlanSetup;
            if (CurrentPatient == null || CurrentStructureSet == null)
                return null;
            foreach (var course in CurrentPatient.Courses)
            {
                if (course.PlanSetups == null)
                    continue;
                foreach (var p in course.PlanSetups)
                {
                    var ext = p as ExternalPlanSetup;
                    if (ext != null && SameStructureSet(ext.StructureSet))
                        return ext;
                }
            }
            return null;
        }

        private void FillDeliveryGuards(PlanningItem item, EvaluationDoseContext ctx)
        {
            try
            {
                if (item.Dose != null)
                {
                    ctx.DoseGridXMm = item.Dose.XRes;
                    ctx.DoseGridYMm = item.Dose.YRes;
                    ctx.DoseGridZMm = item.Dose.ZRes;
                }
            }
            catch
            {
            }

            var setup = item as PlanSetup;
            if (setup == null || setup.Beams == null)
                return;
            double mu = 0;
            foreach (var beam in setup.Beams)
            {
                if (beam == null || beam.IsSetupField)
                    continue;
                try { mu += beam.Meterset.Value; }
                catch { }
            }
            ctx.TotalMu = mu;
        }

        private List<DoseGradientRow> SamplePeakPairGradients(PlanningItem item, List<StructureDoseSnapshot> peaks)
        {
            var rows = new List<DoseGradientRow>();
            if (item == null || item.Dose == null || peaks == null || peaks.Count < 2)
                return rows;

            int limit = Math.Min(peaks.Count, 24);
            for (int i = 0; i < limit; i++)
            {
                int best = -1;
                double bestD = double.MaxValue;
                for (int j = 0; j < limit; j++)
                {
                    if (i == j)
                        continue;
                    double d = peaks[i].Center.DistanceTo(peaks[j].Center);
                    if (d < bestD && d > 0.5)
                    {
                        bestD = d;
                        best = j;
                    }
                }
                if (best < 0)
                    continue;
                if (string.Compare(peaks[i].Id, peaks[best].Id, StringComparison.Ordinal) > 0)
                    continue;

                DoseGradientRow row = SampleGradient(item, peaks[i], peaks[best]);
                if (row != null)
                    rows.Add(row);
            }
            return rows;
        }

        private DoseGradientRow SampleGradient(PlanningItem item, StructureDoseSnapshot a, StructureDoseSnapshot b)
        {
            try
            {
                int n = 25;
                double minDose = double.MaxValue;
                double maxDose = 0;
                for (int i = 0; i < n; i++)
                {
                    double t = i / (double)(n - 1);
                    var p = new VVector(
                        a.Center.X + (b.Center.X - a.Center.X) * t,
                        a.Center.Y + (b.Center.Y - a.Center.Y) * t,
                        a.Center.Z + (b.Center.Z - a.Center.Z) * t);
                    double d = ToGy(item.Dose.GetDoseToPoint(p));
                    if (double.IsNaN(d))
                        continue;
                    if (d < minDose) minDose = d;
                    if (d > maxDose) maxDose = d;
                }
                double sep = a.Center.DistanceTo(b.Center);
                if (sep < 1e-3 || minDose == double.MaxValue)
                    return null;
                return new DoseGradientRow
                {
                    PeakA = a.Id,
                    PeakB = b.Id,
                    SeparationMm = sep,
                    TroughGy = minDose,
                    PeakGy = maxDose,
                    GradientGyPerMm = (maxDose - minDose) / (sep * 0.5)
                };
            }
            catch
            {
                return null;
            }
        }

        private static List<DvhBin> BuildDvhBins(DVHData dvh)
        {
            var bins = new List<DvhBin>();
            if (dvh == null || dvh.CurveData == null)
                return bins;
            foreach (var pt in dvh.CurveData)
            {
                bins.Add(new DvhBin
                {
                    DoseGy = ToGy(pt.DoseValue),
                    CumulativeVolume = pt.Volume
                });
            }
            return bins;
        }

        private Structure FindBodyStructure()
        {
            var ss = CurrentStructureSet;
            if (ss == null)
                return null;
            Structure body = ss.Structures.FirstOrDefault(s => s != null && !s.IsEmpty && s.DicomType == "EXTERNAL");
            if (body != null)
                return body;
            return ss.Structures.FirstOrDefault(s =>
            {
                if (s == null || s.IsEmpty)
                    return false;
                string id = (s.Id ?? string.Empty).ToUpperInvariant();
                return id == "BODY" || id == "EXTERNAL" || id.Contains("BODY") || id.Contains("SKIN");
            });
        }

        public void RollbackCreatedStructures(IEnumerable<string> structureIds)
        {
            if (structureIds == null || CurrentStructureSet == null)
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

            Structure target = parameters != null ? GetStructure(parameters.SelectedTargetId) : null;
            Structure peaks = FindByIdOrPrefix(StructureNaming.CompositePeaksId, "Lattice_Pe");
            Structure valley = FindByIdOrPrefix(StructureNaming.ValleyId, "Lattice_Va");
            Structure valleyCore = FindByIdOrPrefix(StructureNaming.ValleyCoreId, "Valley_Co");

            if (target != null)
                ctx.Target = Snapshot(item, target, planInfo.PrescriptionDoseGy);
            if (peaks != null)
                ctx.Peaks = Snapshot(item, peaks, planInfo.PrescriptionDoseGy);
            if (valley != null)
                ctx.Valley = Snapshot(item, valley, planInfo.PrescriptionDoseGy);
            if (valleyCore != null)
                ctx.ValleyCore = Snapshot(item, valleyCore, planInfo.PrescriptionDoseGy);

            var peakIds = SnapshotStructureIds();
            foreach (var id in peakIds)
            {
                if (!StructureNaming.IsIndividualPeakId(id))
                    continue;
                Structure s = Refetch(id);
                if (s != null)
                    ctx.IndividualPeaks.Add(Snapshot(item, s, planInfo.PrescriptionDoseGy));
            }

            FillDeliveryGuards(item, ctx);
            ctx.Gradients = SamplePeakPairGradients(item, ctx.IndividualPeaks);

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
                snap.Center = ToPoint(structure.CenterPoint);
                snap.DvhBins = BuildDvhBins(dvhAbs);
                snap.GeudAMinus10 = SfrtMetricsCalculator.Geud(snap.DvhBins, -10);
                snap.GeudA1 = SfrtMetricsCalculator.Geud(snap.DvhBins, 1);
                snap.GeudA2 = SfrtMetricsCalculator.Geud(snap.DvhBins, 2);
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

            double step = SFRTParameters.VoxelResolutionMm;
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
            Report(progress, "Computing 2.0 mm V_valid SDF...");
            mask.ComputeDistanceField();
            return mask;
        }

        private Structure EnsureWorkingCopy(Structure source, string preferredId, bool highRes, StructureTransaction temps)
        {
            if (source == null)
                return null;
            if (!highRes || source.IsHighResolution == highRes)
                return source;

            string id = StructureNaming.NextAvailable(preferredId, ExistingIds());
            Structure copy = CurrentStructureSet.AddStructure("CONTROL", id);
            temps.Track(copy.Id);
            copy.SegmentVolume = source.SegmentVolume;
            return EnsureHighResolutionById(id, highRes);
        }

        private void AddSphereContours(Structure structure, SphereModel sphere)
        {
            AddSphereContours(structure, new[] { sphere });
        }

        private void AddSphereContours(Structure structure, IReadOnlyList<SphereModel> spheres)
        {
            var image = CurrentImage;
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
            if (circleRadius < 0.5)
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
            if (transaction == null || CurrentStructureSet == null)
                return;
            foreach (var id in transaction.RollbackOrder())
                TryRemoveById(id);
            transaction.MarkRolledBack();
        }

        private void TryRemoveById(string id)
        {
            if (string.IsNullOrEmpty(id) || CurrentStructureSet == null)
                return;
            try
            {
                Structure s = Refetch(id);
                if (s == null)
                    return;
                bool canRemove = true;
                try
                {
                    canRemove = CurrentStructureSet.CanRemoveStructure(s);
                }
                catch
                {
                    canRemove = true;
                }
                if (canRemove)
                    CurrentStructureSet.RemoveStructure(s);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Rollback cleanup warning for " + id + ": " + ex.Message);
            }
        }

        private List<string> SnapshotStructureIds()
        {
            if (CurrentStructureSet == null)
                return new List<string>();
            return CurrentStructureSet.Structures.Select(s => s.Id).ToList();
        }

        private HashSet<string> ExistingIds()
        {
            return new HashSet<string>(SnapshotStructureIds(), StringComparer.OrdinalIgnoreCase);
        }

        private Structure GetStructure(string id)
        {
            return Refetch(id);
        }

        private Structure Refetch(string id)
        {
            if (string.IsNullOrEmpty(id) || CurrentStructureSet == null)
                return null;
            return CurrentStructureSet.Structures.FirstOrDefault(s => s.Id == id);
        }

        private Structure FindByIdOrPrefix(string preferred, string prefix)
        {
            var ss = CurrentStructureSet;
            if (ss == null)
                return null;
            var ids = SnapshotStructureIds();
            string match = ids.FirstOrDefault(id => string.Equals(id, preferred, StringComparison.OrdinalIgnoreCase));
            if (match == null && !string.IsNullOrEmpty(prefix))
                match = ids.FirstOrDefault(id => id != null && id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            return match == null ? null : Refetch(match);
        }

        private Structure AddTypedStructure(string dicomType, string id, StructureTransaction transaction, HashSet<string> existing, bool highRes)
        {
            Structure structure = null;
            try
            {
                structure = CurrentStructureSet.AddStructure(dicomType, id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("AddStructure(" + dicomType + ", " + id + "): " + ex.Message);
                if (!string.Equals(dicomType, StructureNaming.DicomControl, StringComparison.OrdinalIgnoreCase))
                    structure = CurrentStructureSet.AddStructure(StructureNaming.DicomControl, id);
                else
                    throw;
            }

            string createdId = structure.Id;
            if (transaction != null)
                transaction.Track(createdId);
            if (existing != null)
                existing.Add(createdId);
            return EnsureHighResolutionById(createdId, highRes);
        }

        private Structure EnsureHighResolutionById(string id, bool highRes)
        {
            Structure structure = Refetch(id);
            if (structure == null || !highRes || structure.IsHighResolution)
                return structure;
            try
            {
                structure.ConvertToHighResolution();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ConvertToHighResolution " + id + ": " + ex.Message);
            }
            return Refetch(id);
        }

        private Structure AssignSegment(string id, SegmentVolume volume)
        {
            Structure structure = Refetch(id);
            if (structure == null || volume == null)
                return structure;
            structure.SegmentVolume = volume;
            return Refetch(id);
        }

        private void ClipToExternalById(string ringId, string bodyId)
        {
            Structure ring = Refetch(ringId);
            Structure body = Refetch(bodyId);
            if (ring == null || body == null || body.IsEmpty)
                return;
            try
            {
                AssignSegment(ringId, ring.And(body.SegmentVolume));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("EXTERNAL clip " + ringId + ": " + ex.Message);
            }
        }

        private static List<SphereModel> FilterIncludedSpheres(IReadOnlyList<SphereModel> spheres)
        {
            var list = new List<SphereModel>();
            if (spheres == null)
                return list;
            for (int i = 0; i < spheres.Count; i++)
            {
                if (spheres[i] != null && spheres[i].IsIncluded)
                    list.Add(spheres[i]);
            }
            return list;
        }

        private Point3D FindValleyPoi(string targetId, IReadOnlyList<SphereModel> peaks)
        {
            Structure target = Refetch(targetId);
            if (target == null)
                return peaks != null && peaks.Count > 0 ? peaks[0].Center : new Point3D(0, 0, 0);
            Point3D best = ToPoint(target.CenterPoint);
            double bestMin = -1;
            BoundingBox3D bounds = ToBounds(target);
            double step = 4.0;
            for (double z = bounds.MinZ; z <= bounds.MaxZ + 1e-6; z += step)
            {
                for (double y = bounds.MinY; y <= bounds.MaxY + 1e-6; y += step)
                {
                    for (double x = bounds.MinX; x <= bounds.MaxX + 1e-6; x += step)
                    {
                        try
                        {
                            if (!target.IsPointInsideSegment(new VVector(x, y, z)))
                                continue;
                        }
                        catch
                        {
                            continue;
                        }
                        double minD = double.MaxValue;
                        if (peaks != null)
                        {
                            for (int i = 0; i < peaks.Count; i++)
                            {
                                double d = peaks[i].Center.DistanceTo(new Point3D(x, y, z));
                                if (d < minD)
                                    minD = d;
                            }
                        }
                        if (minD > bestMin)
                        {
                            bestMin = minD;
                            best = new Point3D(x, y, z);
                        }
                    }
                }
            }
            return best;
        }

        private static string BuildCreationMessage(
            bool createIndividuals,
            int peakCount,
            string peaksId,
            string valleyId,
            string penumbraId,
            string valleyCoreId,
            string ring01Id,
            string ring13Id,
            string poiPeakId,
            string poiValleyId)
        {
            var parts = new List<string>();
            if (createIndividuals)
                parts.Add(peakCount.ToString(CultureInfo.InvariantCulture) + " Peak structures");
            AppendId(parts, peaksId);
            AppendId(parts, valleyId);
            AppendId(parts, penumbraId);
            AppendId(parts, valleyCoreId);
            AppendId(parts, ring01Id);
            AppendId(parts, ring13Id);
            AppendId(parts, poiPeakId);
            AppendId(parts, poiValleyId);

            if (parts.Count == 0)
                return "Created lattice structures.";
            if (parts.Count == 1)
                return "Created " + parts[0] + ".";
            if (parts.Count == 2)
                return "Created " + parts[0] + " and " + parts[1] + ".";

            var sb = new System.Text.StringBuilder("Created ");
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                    sb.Append(i == parts.Count - 1 ? ", and " : ", ");
                sb.Append(parts[i]);
            }
            sb.Append(".");
            return sb.ToString();
        }

        private static void AppendId(List<string> parts, string id)
        {
            if (!string.IsNullOrEmpty(id))
                parts.Add(id);
        }

        private LinacEnergyOption ResolveMachineOption(ExternalPlanSetup plan, SFRTParameters parameters)
        {
            Beam template = FirstTreatmentBeam(plan);
            if (template != null)
                return FromBeam(template, plan.Id);

            if (parameters != null && !string.IsNullOrWhiteSpace(parameters.SelectedMachineId)
                && !string.IsNullOrWhiteSpace(parameters.SelectedEnergyMode)
                && parameters.SelectedDoseRate > 0)
            {
                return new LinacEnergyOption
                {
                    MachineId = parameters.SelectedMachineId,
                    EnergyModeId = parameters.SelectedEnergyMode,
                    EnergyModeDisplayName = parameters.SelectedEnergyMode,
                    PrimaryFluenceMode = parameters.SelectedPrimaryFluenceMode,
                    DoseRate = parameters.SelectedDoseRate,
                    Key = BeamMachineParser.OptionKey(
                        parameters.SelectedMachineId, parameters.SelectedEnergyMode,
                        parameters.SelectedDoseRate, parameters.SelectedPrimaryFluenceMode)
                };
            }

            IReadOnlyList<LinacEnergyOption> options = GetLinacEnergyOptions();
            if (options.Count > 0)
                return options[0];
            return null;
        }

        private static Beam FirstTreatmentBeam(ExternalPlanSetup plan)
        {
            if (plan == null || plan.Beams == null)
                return null;
            foreach (var b in plan.Beams)
            {
                if (b != null && !b.IsSetupField)
                    return b;
            }
            return null;
        }

        private static LinacEnergyOption FromBeam(Beam beam, string planId)
        {
            if (beam == null || beam.TreatmentUnit == null)
                return null;
            string display = beam.EnergyModeDisplayName;
            string energy;
            string fluence;
            BeamMachineParser.SplitEnergyMode(display, out energy, out fluence);
            int doseRate = 0;
            try { doseRate = (int)beam.DoseRate; }
            catch { }
            return new LinacEnergyOption
            {
                MachineId = beam.TreatmentUnit.Id,
                EnergyModeDisplayName = display,
                EnergyModeId = energy,
                PrimaryFluenceMode = fluence,
                DoseRate = doseRate,
                SourcePlanId = planId,
                Key = BeamMachineParser.OptionKey(beam.TreatmentUnit.Id, energy, doseRate, fluence)
            };
        }

        private VVector ResolveIsocenter(ExternalPlanSetup plan)
        {
            Beam template = FirstTreatmentBeam(plan);
            if (template != null)
                return template.IsocenterPosition;

            Structure target = CurrentStructureSet != null
                ? CurrentStructureSet.Structures.FirstOrDefault(s => s != null && !s.IsEmpty &&
                    (s.DicomType == "GTV" || s.DicomType == "PTV" || (s.Id ?? string.Empty).ToUpperInvariant().Contains("GTV")))
                : null;
            if (target != null)
                return target.CenterPoint;
            return new VVector(0, 0, 0);
        }

        private bool TryAddVmatPair(ExternalPlanSetup plan, LinacEnergyOption opt, VVector iso, out string error)
        {
            error = null;
            var attempts = new List<ExternalBeamMachineParameters>();
            attempts.Add(new ExternalBeamMachineParameters(opt.MachineId, opt.EnergyModeDisplayName ?? opt.EnergyModeId, opt.DoseRate, "ARC", null));
            if (!string.IsNullOrEmpty(opt.EnergyModeId)
                && !string.Equals(opt.EnergyModeId, opt.EnergyModeDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                attempts.Add(new ExternalBeamMachineParameters(opt.MachineId, opt.EnergyModeId, opt.DoseRate, "ARC", opt.PrimaryFluenceMode));
            }
            else if (!string.IsNullOrEmpty(opt.PrimaryFluenceMode))
            {
                attempts.Add(new ExternalBeamMachineParameters(opt.MachineId, opt.EnergyModeId, opt.DoseRate, "ARC", opt.PrimaryFluenceMode));
            }

            var weights = new List<double>(178);
            for (int i = 0; i < 178; i++)
                weights.Add(1.0);

            foreach (var machine in attempts)
            {
                try
                {
                    plan.AddVMATBeam(machine, weights, 15.0, 181.0, 179.0, GantryDirection.Clockwise, 0.0, iso);
                    plan.AddVMATBeam(machine, weights, 75.0, 179.0, 181.0, GantryDirection.CounterClockwise, 0.0, iso);
                    error = null;
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }
            return false;
        }

        private static string TryEnableJawTracking(ExternalPlanSetup plan)
        {
            if (plan == null)
                return "Jaw tracking is not available.";
            try
            {
                if (plan.OptimizationSetup != null)
                    plan.OptimizationSetup.UseJawTracking = true;
            }
            catch (Exception ex)
            {
                return "Jaw tracking is not supported on this linear accelerator (" + ex.Message + ").";
            }

            try
            {
                var mi = plan.GetType().GetMethod("SetJawTracking", new[] { typeof(bool) });
                if (mi != null)
                    mi.Invoke(plan, new object[] { true });
            }
            catch
            {
            }
            return null;
        }

        private static void EnsureHighResolution(Structure structure, bool highRes)
        {
            if (structure == null || !highRes || structure.IsHighResolution)
                return;
            try
            {
                structure.ConvertToHighResolution();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ConvertToHighResolution " + structure.Id + ": " + ex.Message);
            }
        }

        private bool TryFindPlanningItem(string key, out PlanningItem item, out PlanListItem info)
        {
            item = null;
            info = GetEvaluablePlans().FirstOrDefault(p => p.Key == key);
            if (info == null || CurrentPatient == null)
                return false;

            // C# forbids capturing out/ref parameters in lambdas (CS1628).
            string courseId = info.CourseId;
            string planId = info.PlanId;
            bool isPlanSum = info.IsPlanSum;

            foreach (var course in CurrentPatient.Courses)
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
            if (other == null || CurrentStructureSet == null)
                return false;
            if (ReferenceEquals(other, CurrentStructureSet))
                return true;
            try
            {
                return other.UID == CurrentStructureSet.UID;
            }
            catch
            {
                return string.Equals(other.Id, CurrentStructureSet.Id, StringComparison.OrdinalIgnoreCase);
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
