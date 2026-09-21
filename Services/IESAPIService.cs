using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SFRThelper.Models;

namespace SFRThelper.Services
{
    /// <summary>
    /// Dedicated STA dispatcher contract for all VMS.TPS calls.
    /// UI threads never touch ESAPI objects; they marshal through this worker.
    /// </summary>
    public interface IEsapiWorker
    {
        bool CheckAccess();
        void Invoke(Action action);
        T Invoke<T>(Func<T> func);
        Task InvokeAsync(Action action, CancellationToken token);
        Task<T> InvokeAsync<T>(Func<T> func, CancellationToken token);
        void BeginShutdown();
    }

    /// <summary>
    /// Abstraction over all VMS.TPS calls. ViewModels consume only POCOs/DTOs from this contract.
    /// Implementations must run ESAPI work on the dedicated <see cref="IEsapiWorker"/> STA thread.
    /// </summary>
    public interface IESAPIService
    {
        bool IsStandalone { get; }
        IEsapiWorker Worker { get; }

        string GetPatientStatus();
        bool TryEnsureWriteAccess(out string error);
        bool CanModifyStructureSet(out string reason);

        IReadOnlyList<StructureListItem> GetTargetStructures();
        IReadOnlyList<StructureListItem> GetAvoidanceStructures();
        IReadOnlyList<PlanListItem> GetEvaluablePlans();

        StructureListItem GetStructureInfo(string structureId);
        ImageGeometryDto GetImageGeometry();
        IReadOnlyList<IReadOnlyList<Point3D>> GetStructureContoursOnSlice(string structureId, int sliceIndex);

        LatticeGeometryContext ExtractLatticeGeometry(
            SFRTParameters parameters,
            IProgress<string> progress,
            CancellationToken token);

        StructureCreationResult CreateLatticeStructures(
            IReadOnlyList<SphereModel> spheres,
            SFRTParameters parameters,
            IProgress<string> progress,
            CancellationToken token);

        void RollbackCreatedStructures(IEnumerable<string> structureIds);

        EvaluationDoseContext ExtractEvaluationDoseContext(string planKey, SFRTParameters parameters);

        string SetupVmatArcs();
        string SeedPhotonObjectives(SFRTParameters parameters);
    }

    public class EvaluationDoseContext
    {
        public PlanListItem Plan { get; set; }
        public StructureDoseSnapshot Target { get; set; }
        public StructureDoseSnapshot Peaks { get; set; }
        public StructureDoseSnapshot Valley { get; set; }
        public List<StructureDoseSnapshot> IndividualPeaks { get; set; }
        public List<StructureDoseSnapshot> Oars { get; set; }
        public List<DoseGradientRow> Gradients { get; set; }
        public double DoseGridXMm { get; set; }
        public double DoseGridYMm { get; set; }
        public double DoseGridZMm { get; set; }
        public double TotalMu { get; set; }
        public string ErrorMessage { get; set; }

        public EvaluationDoseContext()
        {
            IndividualPeaks = new List<StructureDoseSnapshot>();
            Oars = new List<StructureDoseSnapshot>();
            Gradients = new List<DoseGradientRow>();
        }

        public double DoseGridMaxMm
        {
            get { return Math.Max(DoseGridXMm, Math.Max(DoseGridYMm, DoseGridZMm)); }
        }
    }
}
