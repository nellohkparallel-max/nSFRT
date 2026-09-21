using System;
using System.Collections.Generic;
using System.Threading;
using SFRThelper.Models;

namespace SFRThelper.Services
{
    /// <summary>
    /// Abstraction over all VMS.TPS calls. ViewModels consume only POCOs/DTOs from this contract.
    /// </summary>
    public interface IESAPIService
    {
        string GetPatientStatus();
        bool TryEnsureWriteAccess(out string error);
        bool CanModifyStructureSet(out string reason);

        IReadOnlyList<StructureListItem> GetTargetStructures();
        IReadOnlyList<StructureListItem> GetAvoidanceStructures();
        IReadOnlyList<PlanListItem> GetEvaluablePlans();

        StructureListItem GetStructureInfo(string structureId);
        ImageGeometryDto GetImageGeometry();
        IReadOnlyList<IReadOnlyList<Point3D>> GetStructureContoursOnSlice(string structureId, int sliceIndex);

        /// <summary>
        /// Phase 1 (STA): build V_valid = Target.Margin(-(r+M_target)) \ (OAR1.Margin(M1+r) ∪ OAR2.Margin(M2+r))
        /// and rasterize it into a CPU occupancy mask.
        /// </summary>
        LatticeGeometryContext ExtractLatticeGeometry(
            SFRTParameters parameters,
            IProgress<string> progress,
            CancellationToken token);

        /// <summary>
        /// Phase 3 (STA): commit individual Peak_xx, Lattice_Peaks, and Lattice_Valley structures.
        /// Rolls back every created structure if generation fails or is cancelled.
        /// </summary>
        StructureCreationResult CreateLatticeStructures(
            IReadOnlyList<SphereModel> spheres,
            SFRTParameters parameters,
            IProgress<string> progress,
            CancellationToken token);

        void RollbackCreatedStructures(IEnumerable<string> structureIds);

        EvaluationDoseContext ExtractEvaluationDoseContext(string planKey, SFRTParameters parameters);
    }

    public class EvaluationDoseContext
    {
        public PlanListItem Plan { get; set; }
        public StructureDoseSnapshot Target { get; set; }
        public StructureDoseSnapshot Peaks { get; set; }
        public StructureDoseSnapshot Valley { get; set; }
        public List<StructureDoseSnapshot> IndividualPeaks { get; set; }
        public List<StructureDoseSnapshot> Oars { get; set; }
        public string ErrorMessage { get; set; }

        public EvaluationDoseContext()
        {
            IndividualPeaks = new List<StructureDoseSnapshot>();
            Oars = new List<StructureDoseSnapshot>();
        }
    }
}
