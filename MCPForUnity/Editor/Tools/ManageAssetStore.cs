using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_asset_store", AutoRegister = false, Group = "core")]
    public static class ManageAssetStore
    {
        // ── Reflection cache ──────────────────────────────────────────────

        private static bool _reflectionInitialized;
        private static bool _reflectionAvailable;

        // UnityEditor.Connect.UnityConnect
        private static Type _unityConnectType;
        private static PropertyInfo _unityConnectInstance;
        private static PropertyInfo _unityConnectLoggedIn;

        // AssetStoreDownloadManager (download packages)
        private static Type _downloadManagerType;
        private static MethodInfo _getDownloadOpMethod;

        // AssetStoreCache (local info for downloaded packages)
        private static Type _assetStoreCacheType;
        private static MethodInfo _cacheGetLocalInfo;

        // AssetStoreLocalInfo (package path extraction)
        private static Type _assetStoreLocalInfoType;
        private static MethodInfo _localInfoToDictionary;

        // AssetStoreListOperation (tracks LoadMore progress)
        private static Type _listOperationType;

        // AssetStoreDownloadOperation (download state tracking)
        private static Type _downloadOperationType;
        private static PropertyInfo _downloadOpErrorMessage;

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            AssemblyReloadEvents.afterAssemblyReload += () =>
            {
                _reflectionInitialized = false;
                _reflectionAvailable = false;
                DestroyHiddenWindow();
                _cachedRoot = null;
                _serviceCache.Clear();
            };
        }

        // ── Entry point ───────────────────────────────────────────────────

        public static async Task<object> HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);

            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                switch (action)
                {
                    case "check_auth":
                        return CheckAuth();
                    case "list_purchases":
                        return await ListPurchasesAsync(p);
                    case "download":
                        return await DownloadAsync(p);
                    case "import":
                        return Import(p);
                    default:
                        return new ErrorResponse(
                            $"Unknown action: '{action}'. Supported actions: check_auth, list_purchases, download, import.");
                }
            }
            catch (Exception ex)
            {
                return new ErrorResponse(ex.Message, new { stackTrace = ex.StackTrace });
            }
        }

        // ── Actions ───────────────────────────────────────────────────────

        private static object CheckAuth()
        {
            if (!EnsureReflection(out var error))
                return error;

            try
            {
                var instance = _unityConnectInstance.GetValue(null);
                bool loggedIn = (bool)_unityConnectLoggedIn.GetValue(instance);

                return new SuccessResponse(
                    loggedIn ? "User is logged into Unity account." : "User is NOT logged in. Log in via Edit > Preferences > Accounts or Unity Hub.",
                    new { logged_in = loggedIn, unity_version = Application.unityVersion }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to check auth status: {e.Message}");
            }
        }

        private static async Task<object> ListPurchasesAsync(ToolParams p)
        {
            if (!EnsureReflection(out var error))
                return error;

            if (!CheckLoggedIn(out var authError))
                return authError;

            int page = p.GetInt("page") ?? 1;
            int pageSize = p.GetInt("page_size") ?? p.GetInt("pageSize") ?? 50;
            if (page < 1) return new ErrorResponse("'page' must be >= 1.");
            if (pageSize < 1) return new ErrorResponse("'page_size' must be >= 1.");

            try
            {
                await EnsureMyAssetsPageActiveAsync();

                var (myAssetsPage, vsl, realTotal) = GetMyAssetsPageInfo();
                int loadedCount = GetVisualStateLoaded(vsl);
                int needed = Math.Min(page * pageSize, realTotal > 0 ? realTotal : int.MaxValue);

                if (needed > loadedCount && loadedCount < realTotal && myAssetsPage != null)
                {
                    long toLoad = needed - loadedCount;

                    var loadMoreMethod = myAssetsPage.GetType().GetMethods(
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "LoadMore");

                    if (loadMoreMethod != null)
                    {
                        var paramType = loadMoreMethod.GetParameters().FirstOrDefault()?.ParameterType;
                        if (paramType == typeof(long))
                            loadMoreMethod.Invoke(myAssetsPage, new object[] { toLoad });
                        else if (paramType == typeof(int))
                            loadMoreMethod.Invoke(myAssetsPage, new object[] { (int)toLoad });

                        object listOp = GetListOperation(myAssetsPage);
                        await WaitForOperationAsync(listOp, TimeSpan.FromSeconds(30));

                        (_, vsl, realTotal) = GetMyAssetsPageInfo();
                        loadedCount = GetVisualStateLoaded(vsl);
                        if (loadedCount < needed && loadedCount < realTotal)
                            return new ErrorResponse(
                                $"Timed out loading Asset Store purchases. Only {loadedCount}/{realTotal} loaded.");
                    }
                }

                var result = ReadPackagesFromVisualStateList(vsl, page, pageSize, realTotal);
                return result ?? new ErrorResponse(
                    "Asset Store purchase listing is not available in this Unity version. " +
                    $"Unity {Application.unityVersion} may not expose the required internal APIs.");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to list purchases: {e.Message}");
            }
            finally
            {
                DestroyHiddenWindow();
            }
        }

        private static async Task EnsureMyAssetsPageActiveAsync()
        {
            var root = GetPackageManagerRoot();
            if (root == null) return;

            var pmField = root.GetType().GetField("m_PageManager",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var pm = pmField?.GetValue(root);
            if (pm == null) return;

            // Check if My Assets page is the active page
            var activePageProp = pm.GetType().GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(prop => prop.Name == "activePage");

            var getPageMethod = pm.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetPage" && m.GetParameters().Length == 1);

            if (activePageProp == null || getPageMethod == null) return;

            var myAssetsPage = getPageMethod.Invoke(pm, new object[] { "MyAssets" });
            if (myAssetsPage == null) return;

            var activePage = activePageProp.GetValue(pm);
            if (activePage != myAssetsPage)
            {
                // Set active page to My Assets — triggers OnActivated which calls ListPurchases
                var setActiveMethod = pm.GetType().GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "SetActivePage" || m.Name == "set_activePage");

                if (setActiveMethod != null)
                    setActiveMethod.Invoke(pm, new[] { myAssetsPage });
                else if (activePageProp.CanWrite)
                    activePageProp.SetValue(pm, myAssetsPage);
            }

            // Wait for initial load: poll until countTotal > 0 or operation finishes
            var vslField = myAssetsPage.GetType().GetFields(
                    BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f => f.Name == "m_VisualStateList");
            var vsl = vslField?.GetValue(myAssetsPage);

            int countTotal = GetVisualStatePropInt(vsl, "countTotal");
            if (countTotal > 0) return; // Already loaded

            // Wait for the initial ListPurchases triggered by OnActivated
            object listOp = GetListOperation(myAssetsPage);
            await WaitForOperationAsync(listOp, TimeSpan.FromSeconds(15));

            // After operation completes, check if countTotal is populated
            // If still 0, wait a bit more (REST response may need a frame to propagate)
            vsl = vslField?.GetValue(myAssetsPage);
            countTotal = GetVisualStatePropInt(vsl, "countTotal");
            if (countTotal == 0)
            {
                // Give it one more second for propagation
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                int frames = 0;
                void WaitFrames()
                {
                    frames++;
                    var total = GetVisualStatePropInt(vslField?.GetValue(myAssetsPage), "countTotal");
                    if (total > 0 || frames > 60) // ~1 second
                    {
                        EditorApplication.update -= WaitFrames;
                        tcs.TrySetResult(true);
                    }
                }
                EditorApplication.update += WaitFrames;
                await tcs.Task;
            }
        }

        private static void DestroyHiddenWindow()
        {
            if (_hiddenWindowInstance != null)
            {
                try { UnityEngine.Object.DestroyImmediate(_hiddenWindowInstance); }
                catch (Exception e) { McpLog.Warn($"[ManageAssetStore] DestroyHiddenWindow: {e.Message}"); }
                _hiddenWindowInstance = null;
            }

            _cachedRoot = null;
            _serviceCache.Clear();
        }

        private static (object myAssetsPage, object visualStateList, int total) GetMyAssetsPageInfo()
        {
            try
            {
                var root = GetPackageManagerRoot();
                if (root == null) return (null, null, 0);

                var pmField = root.GetType().GetField("m_PageManager",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var pm = pmField?.GetValue(root);
                if (pm == null) return (null, null, 0);

                var getPageMethod = pm.GetType().GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "GetPage" && m.GetParameters().Length == 1);
                if (getPageMethod == null) return (null, null, 0);

                var myAssetsPage = getPageMethod.Invoke(pm, new object[] { "MyAssets" });
                if (myAssetsPage == null) return (null, null, 0);

                var vslField = myAssetsPage.GetType().GetFields(
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(f => f.Name == "m_VisualStateList");
                var vsl = vslField?.GetValue(myAssetsPage);
                if (vsl == null) return (myAssetsPage, null, 0);

                int total = GetVisualStatePropInt(vsl, "countTotal");
                return (myAssetsPage, vsl, total);
            }
            catch { return (null, null, 0); }
        }

        private static int GetVisualStateLoaded(object vsl)
        {
            return GetVisualStatePropInt(vsl, "countLoaded");
        }

        private static int GetVisualStatePropInt(object vsl, string propName)
        {
            if (vsl == null) return 0;
            try
            {
                var prop = vsl.GetType().GetProperties(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(p => p.Name == propName);
                if (prop != null)
                {
                    var val = prop.GetValue(vsl);
                    if (val is int i) return i;
                    if (val is long l) return (int)l;
                }
            }
            catch { }
            return 0;
        }

        private static object GetListOperation(object myAssetsPage)
        {
            try
            {
                // myAssetsPage → m_AssetStoreClient → m_ListOperation
                var clientField = myAssetsPage.GetType().GetFields(
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(f => f.Name == "m_AssetStoreClient");
                var client = clientField?.GetValue(myAssetsPage);
                if (client == null) return null;

                var listOpField = client.GetType().GetFields(
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(f => f.Name == "m_ListOperation");
                return listOpField?.GetValue(client);
            }
            catch { return null; }
        }

        private static object ReadPackagesFromVisualStateList(object vsl, int page, int pageSize, int realTotal)
        {
            if (vsl == null) return null;

            try
            {
                var root = GetPackageManagerRoot();
                if (root == null) return null;

                var dbField = root.GetType().GetField("m_PackageDatabase",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var db = dbField?.GetValue(root);
                if (db == null) return null;

                // Get GetPackage method on PackageDatabase
                var getPackageMethod = db.GetType().GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "GetPackage"
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType == typeof(string));

                // Enumerate visual state list to get unique IDs
                var packages = new List<object>();
                int skip = (page - 1) * pageSize;
                int count = 0;
                int index = 0;

                if (vsl is System.Collections.IEnumerable enumerable)
                {
                    foreach (var state in enumerable)
                    {
                        if (state == null) continue;

                        // packageUniqueId is a field, not a property
                        string uniqueId = state.GetType().GetField("packageUniqueId",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                            ?.GetValue(state)?.ToString();

                        if (string.IsNullOrEmpty(uniqueId)) continue;

                        index++;
                        if (index <= skip) continue;
                        if (count >= pageSize) break;

                        // Look up package in database for metadata
                        string displayName = uniqueId;
                        string productId = uniqueId;
                        string publisherName = null;
                        string category = null;

                        if (getPackageMethod != null)
                        {
                            try
                            {
                                var pkg = getPackageMethod.Invoke(db, new object[] { uniqueId });
                                if (pkg != null)
                                {
                                    var props = pkg.GetType().GetProperties(
                                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    displayName = props.FirstOrDefault(p => p.Name == "displayName")?.GetValue(pkg)?.ToString() ?? uniqueId;

                                    var product = props.FirstOrDefault(p => p.Name == "product")?.GetValue(pkg);
                                    if (product != null)
                                    {
                                        var productProps = product.GetType().GetProperties(
                                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                        productId = productProps.FirstOrDefault(p => p.Name == "id")?.GetValue(product)?.ToString() ?? uniqueId;
                                        publisherName = productProps.FirstOrDefault(p => p.Name == "publisherName")?.GetValue(product)?.ToString();
                                        category = productProps.FirstOrDefault(p => p.Name == "category")?.GetValue(product)?.ToString();
                                    }
                                }
                            }
                            catch { }
                        }

                        packages.Add(new
                        {
                            unique_id = uniqueId,
                            display_name = displayName,
                            product_id = productId,
                            publisher = publisherName,
                            category,
                        });
                        count++;
                    }
                }

                return new SuccessResponse(
                    $"Found {packages.Count} Asset Store package(s) (page {page}, {realTotal} total).",
                    new
                    {
                        packages,
                        total = realTotal,
                        page,
                        page_size = pageSize,
                        has_more = page * pageSize < realTotal,
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to read packages: {e.Message}");
            }
        }

        private static async Task<object> DownloadAsync(ToolParams p)
        {
            if (!EnsureReflection(out var error))
                return error;

            if (!CheckLoggedIn(out var authError))
                return authError;

            var productIdResult = p.GetRequired("product_id", "'product_id' parameter is required for download.");
            if (!productIdResult.IsSuccess)
                return new ErrorResponse(productIdResult.ErrorMessage);

            if (!long.TryParse(productIdResult.Value, out long productId))
                return new ErrorResponse($"'product_id' must be a number, got '{productIdResult.Value}'.");

            try
            {
                var startResult = StartDownload(productId);
                if (startResult is ErrorResponse)
                    return startResult;

                await WaitForDownloadAsync(productId, TimeSpan.FromMinutes(5));

                var (_, _, completed, downloadError) = GetDownloadState(productId);
                string packagePath = GetCachedPackagePath(productId);

                if (!string.IsNullOrEmpty(downloadError))
                    return new ErrorResponse($"Download failed for product {productId}: {downloadError}");

                if (!completed && string.IsNullOrEmpty(packagePath))
                    return new ErrorResponse($"Download timed out for product {productId}. The package may still be downloading — try again later.");

                return new SuccessResponse(
                    $"Download completed for product {productId}.",
                    new { product_id = productId, package_path = packagePath }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to start download: {e.Message}");
            }
            finally
            {
                DestroyHiddenWindow();
            }
        }

        private static object Import(ToolParams p)
        {
            if (!EnsureReflection(out var error))
                return error;

            var productIdResult = p.GetRequired("product_id", "'product_id' parameter is required for import.");
            if (!productIdResult.IsSuccess)
                return new ErrorResponse(productIdResult.ErrorMessage);

            if (!long.TryParse(productIdResult.Value, out long productId))
                return new ErrorResponse($"'product_id' must be a number, got '{productIdResult.Value}'.");

            try
            {
                string packagePath = GetCachedPackagePath(productId);
                if (string.IsNullOrEmpty(packagePath))
                    return new ErrorResponse($"No downloaded package found for product ID {productId}. Use 'download' first.");

                if (!System.IO.File.Exists(packagePath))
                    return new ErrorResponse($"Package file not found at '{packagePath}'. The cached download may be corrupt. Re-download with 'download' action.");

                AssetDatabase.ImportPackage(packagePath, false);

                return new SuccessResponse(
                    $"Package imported from '{packagePath}'.",
                    new { product_id = productId, package_path = packagePath }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to import package: {e.Message}");
            }
            finally
            {
                DestroyHiddenWindow();
            }
        }

        // ── Reflection initialization ─────────────────────────────────────

        private static bool EnsureReflection(out object error)
        {
            error = null;
            if (_reflectionInitialized)
            {
                if (!_reflectionAvailable)
                {
                    error = new ErrorResponse(
                        "Asset Store internal APIs are not available in this Unity version. " +
                        $"Unity {Application.unityVersion} may not expose the required types. " +
                        "Try opening Window > Package Manager first, then retry.");
                }
                return _reflectionAvailable;
            }

            try
            {
                // Find UnityConnect for auth check
                _unityConnectType = FindType("UnityEditor.Connect.UnityConnect");
                if (_unityConnectType != null)
                {
                    // Use GetProperties to avoid AmbiguousMatchException
                    _unityConnectInstance = _unityConnectType
                        .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                        .FirstOrDefault(p => p.Name == "instance");
                    _unityConnectLoggedIn = _unityConnectType
                        .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .FirstOrDefault(p => p.Name == "loggedIn");
                }

                // Scan ALL assemblies for internal Asset Store types using GetTypes()
                // (GetExportedTypes only returns public types; Asset Store types are internal)
                var assetStoreTypes = new Dictionary<string, Type>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var type in asm.GetTypes())
                        {
                            if (type.FullName != null && type.FullName.Contains("AssetStore"))
                            {
                                assetStoreTypes[type.FullName] = type;
                            }
                        }
                    }
                    catch (ReflectionTypeLoadException)
                    {
                        // Some assemblies fail to load types
                    }
                    catch
                    {
                        // Skip problematic assemblies
                    }
                }

                McpLog.Info($"[ManageAssetStore] Found {assetStoreTypes.Count} AssetStore-related types.");

                // Match types by name suffix to handle different namespaces across Unity versions
                _listOperationType = FindByNameSuffix(assetStoreTypes, "AssetStoreListOperation");
                _downloadManagerType = FindByNameSuffix(assetStoreTypes, "AssetStoreDownloadManager");
                _assetStoreCacheType = FindByNameSuffix(assetStoreTypes, "AssetStoreCache");
                _assetStoreLocalInfoType = FindByNameSuffix(assetStoreTypes, "AssetStoreLocalInfo");
                _downloadOperationType = FindByNameSuffix(assetStoreTypes, "AssetStoreDownloadOperation");

                // Resolve methods on discovered types
                ResolveReflectionMembers();

                _reflectionAvailable = _unityConnectType != null
                    && _unityConnectInstance != null
                    && _unityConnectLoggedIn != null;

                if (!_reflectionAvailable)
                {
                    error = new ErrorResponse(
                        "Asset Store internal APIs are not available. " +
                        $"Unity {Application.unityVersion} may not expose the required types. " +
                        "Try opening Window > Package Manager first, then retry.");
                }

                // Only memoize on success — allow retry if types weren't found
                _reflectionInitialized = _reflectionAvailable;

                McpLog.Info($"[ManageAssetStore] Reflection init: available={_reflectionAvailable}, " +
                    $"downloadMgr={_downloadManagerType != null}, " +
                    $"cache={_assetStoreCacheType != null}, " +
                    $"listOp={_listOperationType != null}");

                return _reflectionAvailable;
            }
            catch (Exception e)
            {
                _reflectionAvailable = false;
                error = new ErrorResponse($"Failed to initialize Asset Store reflection: {e.Message}");
                McpLog.Warn($"[ManageAssetStore] Reflection init failed: {e}");
                return false;
            }
        }

        private static void ResolveReflectionMembers()
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            if (_downloadManagerType != null)
            {
                _getDownloadOpMethod = _downloadManagerType.GetMethods(all)
                    .FirstOrDefault(m => m.Name == "GetDownloadOperation"
                        && m.GetParameters().Length == 1);
            }

            if (_assetStoreCacheType != null)
            {
                _cacheGetLocalInfo = _assetStoreCacheType.GetMethods(all)
                    .FirstOrDefault(m => m.Name == "GetLocalInfo"
                        && m.GetParameters().Length == 1);
            }

            if (_assetStoreLocalInfoType != null)
            {
                _localInfoToDictionary = _assetStoreLocalInfoType.GetMethods(all)
                    .FirstOrDefault(m => m.Name == "ToDictionary");
            }

            if (_downloadOperationType != null)
            {
                _downloadOpErrorMessage = _downloadOperationType.GetProperties(all)
                    .FirstOrDefault(p => p.Name == "errorMessage");
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = asm.GetType(fullName);
                    if (type != null) return type;
                }
                catch
                {
                    // Some assemblies throw on GetType
                }
            }
            return null;
        }

        private static Type FindByNameSuffix(Dictionary<string, Type> types, string suffix)
        {
            // Exact match on type name (last segment of FullName)
            foreach (var kvp in types)
            {
                if (kvp.Value.Name == suffix)
                    return kvp.Value;
            }
            return null;
        }

        // ── Auth helper ───────────────────────────────────────────────────

        private static bool CheckLoggedIn(out object error)
        {
            error = null;
            try
            {
                var instance = _unityConnectInstance.GetValue(null);
                bool loggedIn = (bool)_unityConnectLoggedIn.GetValue(instance);
                if (!loggedIn)
                {
                    error = new ErrorResponse(
                        "Not logged into Unity account. Log in via Edit > Preferences > Accounts or Unity Hub, then retry.");
                }
                return loggedIn;
            }
            catch (Exception e)
            {
                error = new ErrorResponse($"Failed to check login status: {e.Message}");
                return false;
            }
        }

        // ── Download logic ────────────────────────────────────────────────

        private static object StartDownload(long productId)
        {
            // Try to use AssetStoreDownloadManager.Download via reflection
            if (_downloadManagerType == null)
            {
                return new ErrorResponse(
                    "Asset Store download API is not available in this Unity version. " +
                    $"Unity {Application.unityVersion} may not expose AssetStoreDownloadManager.");
            }

            var managerInstance = GetServiceInstance(_downloadManagerType);
            if (managerInstance == null)
            {
                return new ErrorResponse(
                    "Could not access Asset Store download manager. " +
                    "Try opening Window > Package Manager > My Assets first.",
                    new { manager_type = _downloadManagerType?.FullName ?? "null" });
            }

            var downloadMethods = managerInstance.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.Name == "Download")
                .ToArray();

            if (downloadMethods.Length == 0)
            {
                return new ErrorResponse("No Download method found on AssetStoreDownloadManager.",
                    new { manager_type = managerInstance.GetType().FullName });
            }

            try
            {
                // Try each overload with appropriate parameter conversion
                bool invoked = false;
                string invokedOverload = null;

                // Prefer the IEnumerable<long> overload — the single-arg long overload is a no-op
                var batchOverload = downloadMethods.FirstOrDefault(dm =>
                {
                    var ps = dm.GetParameters();
                    return ps.Length == 1 && typeof(System.Collections.IEnumerable).IsAssignableFrom(ps[0].ParameterType)
                        && ps[0].ParameterType != typeof(long) && ps[0].ParameterType != typeof(string);
                });

                if (batchOverload != null)
                {
                    try
                    {
                        batchOverload.Invoke(managerInstance, new object[] { new List<long> { productId } });
                        invoked = true;
                        invokedOverload = "IEnumerable<long>";
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"[ManageAssetStore] Batch Download failed: {ex.InnerException?.Message ?? ex.Message}");
                    }
                }

                // Fallback to other overloads
                if (!invoked)
                {
                    foreach (var dm in downloadMethods)
                    {
                        var ps = dm.GetParameters();
                        try
                        {
                            if (ps.Length == 1)
                            {
                                object arg = ps[0].ParameterType == typeof(string)
                                    ? (object)productId.ToString() : productId;
                                dm.Invoke(managerInstance, new object[] { arg });
                                invoked = true;
                                invokedOverload = ps[0].ParameterType.Name;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            McpLog.Warn($"[ManageAssetStore] Download overload failed: {ex.InnerException?.Message ?? ex.Message}");
                        }
                    }
                }

                if (!invoked && downloadMethods.Length > 0)
                {
                    return new ErrorResponse(
                        "Download method exists but no overload matched.",
                        new { product_id = productId });
                }

                McpLog.Info($"[ManageAssetStore] Download invoked via {invokedOverload} overload for product {productId}");

                return new SuccessResponse($"Download started for product {productId}.");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to start download for product {productId}: {e.Message}");
            }
        }

        private static (string phase, float progress, bool completed, string error) GetDownloadState(long productId)
        {
            try
            {
                var managerInstance = GetServiceInstance(_downloadManagerType);
                if (managerInstance != null && _getDownloadOpMethod != null)
                {
                    var paramType = _getDownloadOpMethod.GetParameters().FirstOrDefault()?.ParameterType;
                    object arg = paramType == typeof(string) ? (object)productId.ToString() : productId;
                    var op = _getDownloadOpMethod.Invoke(managerInstance, new[] { arg });

                    if (op != null)
                    {
                        // Download still in progress — check for errors
                        string errorMsg = _downloadOpErrorMessage?.GetValue(op) as string;
                        if (!string.IsNullOrEmpty(errorMsg))
                            return ("error", 0f, false, errorMsg);

                        return ("downloading", 0f, false, null);
                    }

                    // Operation is null — download finished (operation removed after completion)
                    string packagePath = GetCachedPackagePath(productId);
                    if (!string.IsNullOrEmpty(packagePath))
                        return ("completed", 1f, true, null);
                }
            }
            catch (Exception e)
            {
                McpLog.Warn($"[ManageAssetStore] GetDownloadState error: {e.Message}");
            }

            return ("downloading", 0f, false, null);
        }

        // ── Service instance access via PM window root ────────────────────

        private static object _cachedRoot;
        private static readonly Dictionary<string, object> _serviceCache = new();

        private static object GetServiceInstance(Type serviceType)
        {
            if (serviceType == null)
                return null;

            // Check cache first
            string cacheKey = serviceType.FullName;
            if (_serviceCache.TryGetValue(cacheKey, out var cached))
                return cached;

            try
            {
                var root = GetPackageManagerRoot();
                if (root == null)
                    return null;

                // Use FindFieldOfTypeAssignable to search up to 3 levels deep
                // This handles: root → m_DropdownHandler → m_AssetStoreDownloadManager
                var result = FindFieldOfTypeAssignable(root, serviceType, 3);
                if (result != null)
                {
                    _serviceCache[cacheKey] = result;
                    return result;
                }
            }
            catch (Exception e)
            {
                McpLog.Warn($"[ManageAssetStore] GetServiceInstance({serviceType.Name}) failed: {e.Message}");
            }

            return null;
        }

        private static object FindFieldOfTypeAssignable(object obj, Type targetType, int maxDepth, int depth = 0)
        {
            if (obj == null || targetType == null || depth > maxDepth)
                return null;

            var objType = obj.GetType();
            var fields = objType.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            // Direct match on this object's fields
            foreach (var field in fields)
            {
                try
                {
                    var value = field.GetValue(obj);
                    if (value != null && targetType.IsAssignableFrom(value.GetType()))
                        return value;
                }
                catch { }
            }

            // Recurse into non-primitive, non-System fields
            foreach (var field in fields)
            {
                if (field.FieldType.IsPrimitive || field.FieldType == typeof(string) || field.FieldType.IsEnum)
                    continue;
                if (field.FieldType.IsValueType)
                    continue;
                // Only recurse into PM-related types
                var ns = field.FieldType.Namespace;
                if (ns == null || (!ns.Contains("PackageManager") && !ns.Contains("AssetStore")))
                {
                    // Also check by runtime type
                    try
                    {
                        var value = field.GetValue(obj);
                        if (value == null) continue;
                        var runtimeNs = value.GetType().Namespace;
                        if (runtimeNs == null || (!runtimeNs.Contains("PackageManager") && !runtimeNs.Contains("AssetStore")))
                            continue;
                        var found = FindFieldOfTypeAssignable(value, targetType, maxDepth, depth + 1);
                        if (found != null)
                            return found;
                    }
                    catch { }
                    continue;
                }

                try
                {
                    var value = field.GetValue(obj);
                    var found = FindFieldOfTypeAssignable(value, targetType, maxDepth, depth + 1);
                    if (found != null)
                        return found;
                }
                catch { }
            }

            return null;
        }

        private static UnityEngine.Object _hiddenWindowInstance;

        private static object GetPackageManagerRoot()
        {
            if (_cachedRoot != null)
                return _cachedRoot;

            var windowType = FindType("UnityEditor.PackageManager.UI.PackageManagerWindow");
            if (windowType == null)
                return null;

            // Reuse an existing visible window if one is open
            var windows = UnityEngine.Resources.FindObjectsOfTypeAll(windowType);

            if (windows == null || windows.Length == 0)
            {
                // Create a hidden (never shown) instance — services are fully functional
                try
                {
                    _hiddenWindowInstance = ScriptableObject.CreateInstance(windowType);
                    windows = new[] { _hiddenWindowInstance };
                }
                catch (Exception e)
                {
                    McpLog.Warn($"[ManageAssetStore] Failed to create hidden PM window: {e.Message}");
                }

                if (windows == null || windows.Length == 0)
                    return null;
            }

            var rootField = windowType.GetField("m_Root",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (rootField == null)
                return null;

            _cachedRoot = rootField.GetValue(windows[0]);
            return _cachedRoot;
        }

        // ── Async wait helper ──────────────────────────────────────────────

        private static Task WaitForOperationAsync(object listOp, TimeSpan timeout)
        {
            if (listOp == null || _listOperationType == null)
                return Task.CompletedTask;

            var prop = _listOperationType.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(p => p.Name == "isInProgress");
            if (prop == null)
                return Task.CompletedTask;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var start = DateTime.UtcNow;
            int frameCount = 0;

            void Tick()
            {
                if (tcs.Task.IsCompleted) { EditorApplication.update -= Tick; return; }
                if (frameCount++ % 30 != 0) return;

                if ((DateTime.UtcNow - start) > timeout || !(bool)(prop.GetValue(listOp) ?? false))
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetResult(true);
                }
            }

            EditorApplication.update += Tick;
            return tcs.Task;
        }

        private static Task WaitForDownloadAsync(long productId, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var start = DateTime.UtcNow;
            int frameCount = 0;

            void Tick()
            {
                if (tcs.Task.IsCompleted) { EditorApplication.update -= Tick; return; }
                if (frameCount++ % 30 != 0) return;

                if ((DateTime.UtcNow - start) > timeout)
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetResult(true);
                    return;
                }

                var (_, _, completed, error) = GetDownloadState(productId);
                if (completed || !string.IsNullOrEmpty(error))
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetResult(true);
                }
            }

            EditorApplication.update += Tick;
            return tcs.Task;
        }

        // ── Cache path lookup ─────────────────────────────────────────────

        private static string GetCachedPackagePath(long productId)
        {
            try
            {
                // Try AssetStoreCache.GetLocalInfo(productId)
                if (_cacheGetLocalInfo != null)
                {
                    var cacheInstance = GetServiceInstance(_assetStoreCacheType);
                    if (cacheInstance != null)
                    {
                        // GetLocalInfo may take long or string
                        object localInfo = null;
                        var paramType = _cacheGetLocalInfo.GetParameters().FirstOrDefault()?.ParameterType;
                        if (paramType == typeof(long))
                            localInfo = _cacheGetLocalInfo.Invoke(cacheInstance, new object[] { productId });
                        else if (paramType == typeof(string))
                            localInfo = _cacheGetLocalInfo.Invoke(cacheInstance, new object[] { productId.ToString() });
                        else
                            localInfo = _cacheGetLocalInfo.Invoke(cacheInstance, new object[] { productId });

                        if (localInfo != null)
                        {
                            // Try ToDictionary first
                            if (_localInfoToDictionary != null)
                            {
                                var dict = _localInfoToDictionary.Invoke(localInfo, Array.Empty<object>());
                                if (dict is System.Collections.IDictionary d)
                                {
                                    foreach (var key in new[] { "packagePath", "PackagePath", "packagepath" })
                                    {
                                        if (d.Contains(key))
                                        {
                                            var path = d[key] as string;
                                            if (!string.IsNullOrEmpty(path))
                                                return path;
                                        }
                                    }
                                }
                            }

                            // Try reading fields directly on AssetStoreLocalInfo
                            var infoFields = localInfo.GetType().GetFields(
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            foreach (var f in infoFields)
                            {
                                if (f.Name.IndexOf("packagePath", StringComparison.OrdinalIgnoreCase) >= 0
                                    || f.Name.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    var path = f.GetValue(localInfo) as string;
                                    if (!string.IsNullOrEmpty(path))
                                        return path;
                                }
                            }

                            McpLog.Warn($"[ManageAssetStore] LocalInfo found for {productId} but no package path could be extracted.");
                        }
                    }
                }

                return null;
            }
            catch (Exception e)
            {
                McpLog.Warn($"[ManageAssetStore] GetCachedPackagePath error: {e.Message}");
                return null;
            }
        }

    }
}
