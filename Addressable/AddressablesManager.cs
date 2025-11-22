using Cysharp.Threading.Tasks;
using jjevol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using static jjevol.AddressablesManager;
using static UnityEngine.Rendering.DebugUI;

namespace jjevol
{
    public class AddressablesPreloader : IDisposable
    {
        private List<UnityEngine.Object> loadObjects = new List<UnityEngine.Object>();
        private List<string> bundleNames = new List<string>();


        public void Dispose()
        {
            AddressablesManager.ReleaseAsset(bundleNames);

            loadObjects.Clear();
            bundleNames.Clear();
        }

        public async UniTask PreloadAsset<T>(List<string> bundleNames, CancellationToken ct = default)
  where T : UnityEngine.Object
        {
            var dict = await AddressablesManager.LoadAssetsAsync<T>(bundleNames, ct);

            foreach (var pair in dict)
            {
                bundleNames.Add(pair.Key);
                loadObjects.Add(pair.Value);
            }
        }

    }

    public class AddressablesManager : Singleton<AddressablesManager>
    {
        public struct PreloadInfo
        {
            public string key;
            public List<string> bundleNames;
            public AsyncOperationHandle handle;

            public PreloadInfo(string key, List<string> bundleNames, AsyncOperationHandle handle)
            {
                this.key = key;

                this.bundleNames = bundleNames;

                this.handle = handle;
            }
        }
        public class AssetRefInfo
        {
            public string assetKey;
            public int refCnt;
            public string preloadKey = string.Empty;
            public AsyncOperationHandle handle;
            public object result;
            public bool IsPreload { get { return !string.IsNullOrEmpty(preloadKey); } }

            public bool IsUsed
            {
                get
                {
                    return refCnt > 0;
                }
            }

            public AssetRefInfo(string assetKey, AsyncOperationHandle handle)
            {
                this.assetKey = assetKey;
                this.handle = handle;
                this.result = handle.Result;
                this.refCnt = 1;
            }

            public AssetRefInfo(string preloadKey, string assetKey, object result)
            {
                this.preloadKey = preloadKey;
                this.assetKey = assetKey;
                this.result = result;
                this.refCnt = 1;
            }

            public void AddRef()
            {
                refCnt++;
            }

            public void RemoveRef()
            {
                refCnt--;
            }
        }

        public static string LocalPath
        {
            get
            {
                return Addressables.RuntimePath;
            }
        }

        public static string BuildTarget
        {
            get
            {
#if UNITY_EDITOR || UNITY_ANDROID
                return "Android";
#else
                return "iOS";
#endif
            }
        }

        public static string RemotePath { get { return Application.persistentDataPath; } }

        public static bool Isinitialized = false;

        private Dictionary<string, AssetRefInfo> assetRefInfos = new Dictionary<string, AssetRefInfo>();

        private Dictionary<string, Coroutine> removeCoutines = new Dictionary<string, Coroutine>();

        private Dictionary<string, PreloadInfo> preloadInfos = new Dictionary<string, PreloadInfo>();

        private bool _IsLoaded(string bundleName)
        {
            return assetRefInfos.ContainsKey(bundleName);
        }

        public static bool IsLoaded(string bundleName)
        {
            var manager = Instance;

            if (manager == null) return false;

            return manager._IsLoaded(bundleName);
        }

        public static UniTask<T> LoadAssetAsync<T>(string bundleName, CancellationTokenSource cts = null)
        {
            return LoadAssetAsync<T>(bundleName, cts?.Token ?? PlaySession.Token);
        }

        public static UniTask<IList<T>> LoadAssetsAsync<T>(string tag, CancellationTokenSource cts = null)
        {
            return LoadAssetsAsync<T>(tag, cts?.Token ?? PlaySession.Token);
        }

        // 새로 추가: CancellationToken 버전 오버로드 (현대화된 호출부에서 쓰기 쉽게)
        public static async UniTask<T> LoadAssetAsync<T>(string bundleName, CancellationToken ct)
        {
            var manager = Instance;
            if (manager == null) return default;

            if (manager._IsLoaded(bundleName))
            {
                var info = manager.assetRefInfos[bundleName];
                info.AddRef();
                manager.CancelRemoveCoroutine(bundleName);
                return (T)info.result;
            }
            else
            {
                var handle = Addressables.LoadAssetAsync<T>(bundleName);
                // ToUniTask를 쓰면 토큰만으로 취소 가능
                await handle.ToUniTask(cancellationToken: ct);

                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    if (!manager._IsLoaded(bundleName))
                    {
                        manager.assetRefInfos.Add(bundleName, new AssetRefInfo(bundleName, handle));

                        return (T)handle.Result;
                    }
                    else
                    {
                        manager.assetRefInfos[bundleName].AddRef();
                        manager.CancelRemoveCoroutine(bundleName);

                        Addressables.Release(handle);
                        return (T)manager.assetRefInfos[bundleName].result;
                    }
                }
                else
                {
                    Addressables.Release(handle);
                    return default;
                }
            }
        }

        public static async UniTask<Dictionary<string, T>> LoadAssetsAsync<T>(
    IEnumerable<string> bundleNames, CancellationToken ct) where T : class
        {
            var manager = Instance;
            if (manager == null) return new Dictionary<string, T>();

            var results = new Dictionary<string, T>();
            var loadingTasks = new List<UniTask<(string name, T asset)>>();

            // 1. 이미 로드된 에셋과 새로 로드할 에셋을 분리합니다.
            // Distinct()를 사용하여 중복된 요청을 한 번만 처리합니다.
            foreach (var bundleName in bundleNames.Distinct())
            {
                if (string.IsNullOrEmpty(bundleName)) continue;

                if (manager._IsLoaded(bundleName))
                {
                    var info = manager.assetRefInfos[bundleName];
                    info.AddRef();
                    manager.CancelRemoveCoroutine(bundleName);
                    results[bundleName] = (T)info.result;
                }
                else
                {
                    // 새로 로드해야 하는 에셋은 비동기 작업 목록에 추가합니다.
                    loadingTasks.Add(LoadAndRegisterAsync(bundleName, ct));
                }
            }

            // 2. 새로 로드할 모든 에셋을 병렬로 로딩하고 기다립니다.
            if (loadingTasks.Any())
            {
                var loadedAssets = await UniTask.WhenAll(loadingTasks);
                foreach (var (name, asset) in loadedAssets)
                {
                    // 로드에 성공한 (null이 아닌) 에셋만 결과에 추가합니다.
                    if (asset != null)
                    {
                        results[name] = asset;
                    }
                }
            }

            return results;

            // 로컬 함수: 단일 에셋을 비동기로 로드하고 등록하는 로직을 캡슐화합니다.
            async UniTask<(string name, T asset)> LoadAndRegisterAsync(string name, CancellationToken token)
            {
                var handle = Addressables.LoadAssetAsync<T>(name);
                // SuppressCancellationThrow: 취소 시 예외를 던지는 대신, 결과로 알려줍니다.
                var result = await handle.ToUniTask(cancellationToken: token).SuppressCancellationThrow();

                if (result.IsCanceled || handle.Status != AsyncOperationStatus.Succeeded)
                {
                    Addressables.Release(handle);
                    return (name, null); // 실패 또는 취소 시 null 반환
                }

                // 중요: await 이후에 다른 곳에서 먼저 로드가 완료되었을 수 있는 경쟁 상태(Race Condition)를 체크합니다.
                if (!manager.assetRefInfos.ContainsKey(name))
                {
                    manager.assetRefInfos.Add(name, new AssetRefInfo(name, handle));
                }
                else
                {
                    // 이미 다른 작업이 로드했다면, 새로 만든 핸들은 해제하고 기존 에셋을 사용합니다.
                    manager.assetRefInfos[name].AddRef();
                    Addressables.Release(handle);
                    return (name, (T)manager.assetRefInfos[name].result);
                }

                return (name, handle.Result);
            }
        }

        public static async UniTask<IList<T>> LoadAssetsAsync<T>(string tag, CancellationToken ct)
        {
            var manager = Instance;
            if (manager == null) return null;

            if (manager._IsLoaded(tag))
            {
                var info = manager.assetRefInfos[tag];
                info.AddRef();
                manager.CancelRemoveCoroutine(tag);
                return (IList<T>)info.result;
            }
            else
            {
                var handle = Addressables.LoadAssetsAsync(tag, (T obj) => { });
                await handle.ToUniTask(cancellationToken: ct);

                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    if (!manager.assetRefInfos.ContainsKey(tag))
                        manager.assetRefInfos.Add(tag, new AssetRefInfo(tag, handle));
                    else
                    {
                        manager.assetRefInfos[tag].AddRef();
                        manager.CancelRemoveCoroutine(tag);
                    }
                    return handle.Result;
                }
                else
                {
                    Addressables.Release(handle);
                    return null;
                }
            }
        }

        public static void ReleaseAsset(IEnumerable<string> bundleNames, float delayTime = 0)
        {
            if (Instance == null) return;

            foreach (var bundleName in bundleNames)
                ReleaseAsset(bundleName, delayTime);
        }

        public static void ReleaseAsset(string bundleNameOrTag, float delayTime = 0)
        {
            if (Instance == null) return;

            if (Instance.assetRefInfos.ContainsKey(bundleNameOrTag))
            {
                Instance.assetRefInfos[bundleNameOrTag].RemoveRef();

                if (!Instance.assetRefInfos[bundleNameOrTag].IsUsed)
                {
                    if (delayTime > 0)
                    {
                        Instance.StartRemoveCoroutine(bundleNameOrTag, delayTime);
                    }
                    else
                    {
                        Instance.CancelRemoveCoroutine(bundleNameOrTag);

                        Instance._RemoveAssetRefInfo(bundleNameOrTag);
                    }
                }
            }
        }

        public void CancelRemoveCoroutine(string bundleNameOrTag)
        {
            if (!removeCoutines.ContainsKey(bundleNameOrTag)) return;

            StopCoroutine(removeCoutines[bundleNameOrTag]);

            removeCoutines.Remove(bundleNameOrTag);
        }

        public void StartRemoveCoroutine(string bundleNameOrTag, float delayTime)
        {
            if (removeCoutines.ContainsKey(bundleNameOrTag)) return;
            if (!assetRefInfos.ContainsKey(bundleNameOrTag)) return;

            removeCoutines.Add(bundleNameOrTag, StartCoroutine(_RemoveAsset(bundleNameOrTag, delayTime)));
        }

        private IEnumerator _RemoveAsset(string bundleNameOrTag, float delayTime)
        {
            yield return new WaitForSecondsRealtime(delayTime);

            _RemoveAssetRefInfo(bundleNameOrTag);

            removeCoutines.Remove(bundleNameOrTag);
        }

        private void _RemoveAssetRefInfo(string bundleNameOrTag)
        {
            if (!assetRefInfos.ContainsKey(bundleNameOrTag)) return;

            var info = assetRefInfos[bundleNameOrTag];

            if (!info.IsPreload)
            {
                // 안전 가드
                if (info.handle.IsValid())
                    Addressables.Release(info.handle);
            }
            else
            {
                _ReleasePreloadAsset(info.preloadKey, bundleNameOrTag);
            }

            Instance.assetRefInfos.Remove(bundleNameOrTag);
        }

        public GameObject GetPrefab(string bundleName)
        {
            //if (!preloadPrefab.ContainsKey(key)) return null;

            //return preloadPrefab[key];

            if (!assetRefInfos.ContainsKey(bundleName)) return null;

            var assetRefInfo = assetRefInfos[bundleName];
            assetRefInfo.AddRef();
            CancelRemoveCoroutine(bundleName);

            return (GameObject)assetRefInfo.result;
        }

        public T GetAsset<T>(string bundleName)
        {
            if (!assetRefInfos.ContainsKey(bundleName)) return default(T);

            var assetRefInfo = assetRefInfos[bundleName];
            assetRefInfo.AddRef();
            CancelRemoveCoroutine(bundleName);

            return (T)assetRefInfo.result;
        }

        public async UniTask PreloadAsset<T>(string key, List<string> bundleNames, CancellationToken ct = default)
    where T : UnityEngine.Object
        {
            if (preloadInfos.ContainsKey(key))
            {
                // 이미 프리로드 중/완료: 해당 핸들 완료를 기다려 동기화
                await preloadInfos[key].handle.ToUniTask(cancellationToken: ct);
                return;
            }

            var handle = Addressables.LoadAssetsAsync<T>(bundleNames, null, Addressables.MergeMode.Union);
            await handle.ToUniTask(cancellationToken: ct);

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Addressables.Release(handle);
                throw new Exception($"PreloadAssetAsync failed: key={key}");
            }

            var prefabs = handle.Result;

#if UNITY_EDITOR
            for (int i = 0; i < bundleNames.Count; i++)
            {
                if (i < prefabs.Count)
                {
                    if (bundleNames[i] != prefabs[i].name)
                        Debug.LogWarning($"PreloadAsset Diff bundleName : {bundleNames[i]} prefab Name : {prefabs[i].name}");
                }
                else
                {
                    Debug.LogWarning($"PreloadAsset NotFound : {bundleNames[i]}");
                }
            }
#endif

            for (int i = 0; i < bundleNames.Count; i++)
            {
                string assetKey = bundleNames[i];
                if (!assetRefInfos.ContainsKey(assetKey))
                {
                    // i < prefabs.Count 보장은 위에서 Warn 처리했음. 실서비스는 try-catch로 더 엄격히 막아도 OK
                    assetRefInfos.Add(assetKey, new AssetRefInfo(key, assetKey, prefabs[i]));
                }
                else
                {
                    assetRefInfos[assetKey].AddRef();
                    CancelRemoveCoroutine(assetKey);
                }
            }

            preloadInfos.Add(key, new PreloadInfo(key, bundleNames, handle));
        }

        public async UniTask PreloadAssetByTag<T>(string key, string tag, CancellationToken ct = default)
    where T : UnityEngine.Object
        {
            if (preloadInfos.ContainsKey(key))
            {
                await preloadInfos[key].handle.ToUniTask(cancellationToken: ct);
                return;
            }

            var handle = Addressables.LoadAssetsAsync<T>(tag, null);
            await handle.ToUniTask(cancellationToken: ct);

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Addressables.Release(handle);
                throw new Exception($"PreloadAssetByTagAsync failed: key={key}, tag={tag}");
            }

            var prefabs = handle.Result;
            List<string> bundleNames = new List<string>();

            for (int i = 0; i < prefabs.Count; i++)
            {
                string assetKey = prefabs[i].name;
                if (!assetRefInfos.ContainsKey(assetKey))
                {
                    bundleNames.Add(assetKey);
                    assetRefInfos.Add(assetKey, new AssetRefInfo(key, assetKey, prefabs[i]));
                }
                else
                {
                    assetRefInfos[assetKey].AddRef();
                    CancelRemoveCoroutine(assetKey);
                }
            }

            preloadInfos.Add(key, new PreloadInfo(key, bundleNames, handle));
        }

        public void ReleasePreload(string key, bool force = false)
        {
            if (!preloadInfos.ContainsKey(key)) return;

            if (force)
            {
                foreach (var bundleName in preloadInfos[key].bundleNames)
                {
                    if (assetRefInfos.ContainsKey(bundleName))
                        assetRefInfos.Remove(bundleName);
                }

                Addressables.Release(preloadInfos[key].handle);
                preloadInfos.Remove(key);
            }
            else
            {
                var bundleNames = preloadInfos[key].bundleNames.ToArray();

                foreach (var bundleName in bundleNames)
                    ReleaseAsset(bundleName);
            }
        }

        private void _ReleasePreloadAsset(string key, string bundleName)
        {
            if (!preloadInfos.ContainsKey(key)) return;

            if (preloadInfos[key].bundleNames.Contains(bundleName))
                preloadInfos[key].bundleNames.Remove(bundleName);

            if (preloadInfos[key].bundleNames.Count == 0)
            {
                Addressables.Release(preloadInfos[key].handle);
                preloadInfos.Remove(key);
            }
        }

        public void ReleasePreload()
        {
            var keyList = new List<string>(preloadInfos.Keys);

            foreach (var key in keyList)
                ReleasePreload(key);

            //Resources.UnloadUnusedAssets();
            //System.GC.Collect();
        }

        public string GetCatalogName()
        {
            return string.Format("catalog_{0}.json", Application.version);
        }

        public string GetRemoteCatalogPath()
        {
            return Path.Combine(RemotePath, GetCatalogName());
        }

        public string GetLocalCatalogPath()
        {
#if UNITY_EDITOR
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), LocalPath, "catalog.json");
#else
            return Path.Combine(LocalPath, "catalog.json");
#endif
        }

        protected override void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (HasInstance && _instance != this)
            {
                DestroyImmediate(gameObject);
            }
            else if (_instance != this)
            {
                base.Awake();
                DontDestroyOnLoad(gameObject);
            }
        }

        public async UniTask InitAsync(CancellationToken ct = default)
        {
            if (!Isinitialized)
            {
                Isinitialized = false;
                var initHandle = Addressables.InitializeAsync();
                await initHandle.ToUniTask(cancellationToken: ct);
                Isinitialized = true;
            }
        }

        #region Scene
        public List<SceneInstance> loadedScenes { get; private set; } = new List<SceneInstance>();

        public IEnumerator ReloadActiveSceneAsync()
        {
            SceneInstance activeSceneInstance = loadedScenes.Find(scene => scene.Scene == SceneManager.GetActiveScene());

            string sceneName = activeSceneInstance.Scene.name;

            loadedScenes.Remove(activeSceneInstance);

            // Addressables로 활성화된 씬 재로드 (LoadSceneMode.Single 사용)
            var handle = LoadSceneAsync(sceneName, LoadSceneMode.Single);

            yield return handle;

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                SceneManager.SetActiveScene(handle.Result.Scene);

                yield return UIManagerCreater.Instance.StartInitAsync();

                Debug.Log($"Scene {sceneName} reloaded successfully.");
            }
            else
            {
                Debug.LogError($"Failed to reload scene: {sceneName}");
            }
        }

        public AsyncOperationHandle<SceneInstance> LoadSceneAsync(string sceneName, LoadSceneMode loadSceneMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100)
        {
            var handle = Addressables.LoadSceneAsync(sceneName, loadSceneMode, activateOnLoad, priority);
            handle.Completed += operation =>
            {
                if (operation.Status == AsyncOperationStatus.Succeeded)
                {
                    loadedScenes.Add(operation.Result);
                    Debug.Log($"Scene {sceneName} loaded successfully.");
                }
                else
                {
                    Debug.LogError($"Failed to load scene: {sceneName}");
                }
            };

            return handle;
        }

        public AsyncOperationHandle<SceneInstance> UnloadSceneAsync(SceneInstance sceneInstance)
        {
            if (!loadedScenes.Contains(sceneInstance))
            {
                Debug.LogWarning($"Scene {sceneInstance.Scene.name} is not in the loaded list.");
                return default;
            }

            var handle = Addressables.UnloadSceneAsync(sceneInstance);
            handle.Completed += operation =>
            {
                if (operation.Status == AsyncOperationStatus.Succeeded)
                {
                    loadedScenes.Remove(sceneInstance);
                    Debug.Log($"Scene {sceneInstance.Scene.name} loaded successfully.");
                }
                else
                {
                    Debug.LogError($"Failed to load scene: {sceneInstance.Scene.name}");
                }
            };

            return handle;
        }
        #endregion

        #region Update Catalog
        public async UniTask UpdateCatalog(CancellationToken ct = default)
        {
            List<string> catalogsToUpdate = new List<string>();
            var checkHandle = Addressables.CheckForCatalogUpdates(false);
            await checkHandle.ToUniTask(cancellationToken: ct);
            if (checkHandle.Status == AsyncOperationStatus.Succeeded)
                catalogsToUpdate = checkHandle.Result;

            if (catalogsToUpdate.Count > 0)
            {
                for (int i = 0; i < catalogsToUpdate.Count; i++)
                    Debug.LogWarning("UpdateCatalog : " + catalogsToUpdate[i]);

                var updateHandle = Addressables.UpdateCatalogs(catalogsToUpdate);
                await updateHandle.ToUniTask(cancellationToken: ct);
            }

            Addressables.Release(checkHandle);
        }
        public async UniTask LoadCatalogAsync(CancellationToken ct = default)
        {
            Debug.LogWarning("LocalCatalog Path : " + GetLocalCatalogPath());
            var handle = Addressables.LoadContentCatalogAsync(GetLocalCatalogPath());
            await handle.ToUniTask(cancellationToken: ct);
        }

        public async UniTask LoadRemoteCatalogAsync(CancellationToken ct = default)
        {
            Debug.LogWarning("RemoteCatalog Path : " + GetRemoteCatalogPath());
            var handle = Addressables.LoadContentCatalogAsync(GetRemoteCatalogPath());
            await handle.ToUniTask(cancellationToken: ct);
        }
        #endregion

    }
}
