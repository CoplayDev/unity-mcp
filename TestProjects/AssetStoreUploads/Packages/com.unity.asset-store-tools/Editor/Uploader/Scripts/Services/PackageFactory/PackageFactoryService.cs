using AssetStoreTools.Uploader.Data;
using AssetStoreTools.Uploader.Data.Serialization;
using AssetStoreTools.Uploader.Services.Analytics;
using AssetStoreTools.Uploader.Services.Api;
using System.Collections.Generic;
using PackageModel = AssetStoreTools.Api.Models.Package;

namespace AssetStoreTools.Uploader.Services
{
    internal class PackageFactoryService : IPackageFactoryService
    {
        private IWorkflowServices _workflowServices;

        // Service dependencies
        private ICachingService _cachingService;
        private IPackageDownloadingService _packageDownloadingService;
        private IPackageUploadingService _packageUploadingService;
        private IAnalyticsService _analyticsService;

        public PackageFactoryService(
            ICachingService cachingService,
            IPackageDownloadingService packageDownloadingService,
            IPackageUploadingService packageUploadingService,
            IAnalyticsService analyticsService
            )
        {
            _cachingService = cachingService;
            _packageDownloadingService = packageDownloadingService;
            _packageUploadingService = packageUploadingService;
            _analyticsService = analyticsService;

            _workflowServices = new WorkflowServices(_packageDownloadingService, _packageUploadingService, _analyticsService);
        }

        public IPackage CreatePackage(PackageModel packageModel)
        {
            var package = new Package(packageModel);
            return package;
        }

        public IPackageGroup CreatePackageGroup(string groupName, List<IPackage> packages)
        {
            return new PackageGroup(groupName, packages);
        }

        public IPackageContent CreatePackageContent(IPackage package)
        {
            if (!package.IsDraft)
                return null;

            WorkflowStateData stateData = GetOrCreateWorkflowStateData(package);

            var workflows = CreateWorkflows(package, stateData);
            var packageContent = new PackageContent(workflows, stateData, _cachingService);
            return packageContent;
        }

        public List<IWorkflow> CreateWorkflows(IPackage package, WorkflowStateData stateData)
        {
            var workflows = new List<IWorkflow>
            {
                CreateAssetsWorkflow(package, stateData.GetAssetsWorkflowState()),
                CreateUnityPackageWorkflow(package, stateData.GetUnityPackageWorkflowState()),
#if UNITY_ASTOOLS_EXPERIMENTAL
                CreateHybridPackageWorkflow(package, stateData.GetHybridPackageWorkflowState()),
#endif
            };

            return workflows;
        }

        public AssetsWorkflow CreateAssetsWorkflow(IPackage package, AssetsWorkflowState stateData)
        {
            return new AssetsWorkflow(package, stateData, _workflowServices);
        }

        public UnityPackageWorkflow CreateUnityPackageWorkflow(IPackage package, UnityPackageWorkflowState stateData)
        {
            return new UnityPackageWorkflow(package, stateData, _workflowServices);
        }

        public HybridPackageWorkflow CreateHybridPackageWorkflow(IPackage package, HybridPackageWorkflowState stateData)
        {
            return new HybridPackageWorkflow(package, stateData, _workflowServices);
        }

        private WorkflowStateData GetOrCreateWorkflowStateData(IPackage package)
        {
            if (!_cachingService.GetCachedWorkflowStateData(package.PackageId, out var stateData))
                stateData = new WorkflowStateData(package.PackageId);

            return stateData;
        }
    }
}