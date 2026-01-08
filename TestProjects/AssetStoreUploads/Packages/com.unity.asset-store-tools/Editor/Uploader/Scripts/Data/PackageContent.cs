using AssetStoreTools.Uploader.Data.Serialization;
using AssetStoreTools.Uploader.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetStoreTools.Uploader.Data
{
    internal class PackageContent : IPackageContent
    {
        private IWorkflow _activeWorkflow;
        private List<IWorkflow> _workflows;
        private WorkflowStateData _workflowStateData;

        private ICachingService _cachingService;

        public event Action<IWorkflow> OnActiveWorkflowChanged;

        public PackageContent(List<IWorkflow> workflows, WorkflowStateData workflowStateData, ICachingService cachingService)
        {
            _workflows = workflows;
            _workflowStateData = workflowStateData;
            _cachingService = cachingService;

            foreach (var workflow in _workflows)
            {
                workflow.OnChanged += Serialize;
            }

            Deserialize();
        }

        public IWorkflow GetActiveWorkflow()
        {
            return _activeWorkflow;
        }

        public void SetActiveWorkflow(IWorkflow workflow)
        {
            _activeWorkflow = workflow;

            OnActiveWorkflowChanged?.Invoke(_activeWorkflow);

            Serialize();
        }

        public List<IWorkflow> GetAvailableWorkflows()
        {
            return _workflows;
        }

        private void Serialize()
        {
            _workflowStateData.SetActiveWorkflow(_activeWorkflow.Name);
            _cachingService.CacheWorkflowStateData(_workflowStateData);
        }

        private void Deserialize()
        {
            var serializedWorkflow = _workflowStateData.GetActiveWorkflow();
            var workflow = _workflows.FirstOrDefault(x => x.Name == serializedWorkflow);
            if (workflow != null)
                _activeWorkflow = workflow;
            else
                _activeWorkflow = _workflows[0];
        }
    }
}