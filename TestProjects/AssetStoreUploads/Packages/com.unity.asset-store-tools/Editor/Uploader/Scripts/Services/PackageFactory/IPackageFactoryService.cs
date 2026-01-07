using AssetStoreTools.Uploader.Data;
using AssetStoreTools.Uploader.Data.Serialization;
using System.Collections.Generic;
using PackageModel = AssetStoreTools.Api.Models.Package;

namespace AssetStoreTools.Uploader.Services
{
    internal interface IPackageFactoryService : IUploaderService
    {
        IPackageGroup CreatePackageGroup(string groupName, List<IPackage> packages);
        IPackage CreatePackage(PackageModel packageModel);
        IPackageContent CreatePackageContent(IPackage package);
        List<IWorkflow> CreateWorkflows(IPackage package, WorkflowStateData stateData);
        AssetsWorkflow CreateAssetsWorkflow(IPackage package, AssetsWorkflowState stateData);
        UnityPackageWorkflow CreateUnityPackageWorkflow(IPackage package, UnityPackageWorkflowState stateData);
        HybridPackageWorkflow CreateHybridPackageWorkflow(IPackage package, HybridPackageWorkflowState stateData);
    }
}
