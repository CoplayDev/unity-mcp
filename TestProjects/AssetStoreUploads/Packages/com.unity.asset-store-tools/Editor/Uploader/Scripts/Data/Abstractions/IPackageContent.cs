using System;
using System.Collections.Generic;

namespace AssetStoreTools.Uploader.Data
{
    internal interface IPackageContent
    {
        event Action<IWorkflow> OnActiveWorkflowChanged;

        IWorkflow GetActiveWorkflow();
        List<IWorkflow> GetAvailableWorkflows();
        void SetActiveWorkflow(IWorkflow workflow);
    }
}
