using System.Collections.Generic;

namespace SFRThelper.Services
{
    /// <summary>
    /// Tracks structure IDs created during a generation transaction so a failed or aborted
    /// run can sequentially delete every newly instantiated structure.
    /// </summary>
    public sealed class StructureTransaction
    {
        private readonly List<string> _createdIds = new List<string>();
        public bool IsCommitted { get; private set; }
        public bool IsRolledBack { get; private set; }

        public IReadOnlyList<string> CreatedIds
        {
            get { return _createdIds; }
        }

        public void Track(string structureId)
        {
            if (string.IsNullOrEmpty(structureId))
                return;
            if (!_createdIds.Contains(structureId))
                _createdIds.Add(structureId);
        }

        public void Commit()
        {
            IsCommitted = true;
        }

        public void MarkRolledBack()
        {
            IsRolledBack = true;
        }

        public IEnumerable<string> RollbackOrder()
        {
            for (int i = _createdIds.Count - 1; i >= 0; i--)
                yield return _createdIds[i];
        }

        public void Clear()
        {
            _createdIds.Clear();
            IsCommitted = false;
            IsRolledBack = false;
        }
    }
}
