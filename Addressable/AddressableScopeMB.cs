// Assets/ProjectArcana/Script/Common/AddressableScopeMB.cs
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace jjevol
{
    /// <summary>
    /// AddressablesManager 로드/인스턴스를 컴포넌트 수명에 맞춰 자동 정리하는 스코프.
    /// - OnDisable/OnDestroy 시 등록된 키 Release, 트래킹된 인스턴스 Destroy
    /// - 임의 시점 수동 정리(ReleaseAll / DestroyAllInstances)도 가능
    /// </summary>
    public sealed class AddressableScopeMB : MonoBehaviour
    {
        [Header("수명 훅 옵션")]
        [SerializeField] private bool _releaseOnDisable = false;   // 비활성화 시에도 정리할지
        [SerializeField] private bool _releaseOnDestroy = true;    // 파괴 시 정리 (권장)
        [SerializeField] private bool _destroyInstancesOnRelease = true; // 정리 시 인스턴스도 파괴

        private readonly List<string> _keys = new List<string>(8);
        private readonly List<GameObject> _instances = new List<GameObject>(8);
        private bool _released;

        /// <summary>키를 스코프에 등록(나중에 ReleaseAll 시 자동 해제).</summary>
        public void RegisterKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _keys.Add(key);
        }

        /// <summary>이미 로드된(또는 AddressablesManager.GetAsset로 얻을) 키를 스코프에 등록하고 에셋 반환.</summary>
        public T Borrow<T>(string key, bool registerToRelease = true) where T : Object
        {
            var asset = AddressablesManager.Instance?.GetAsset<T>(key);
            if (asset != null && registerToRelease) RegisterKey(key);
            return asset;
        }

        /// <summary>해당 키만 즉시 해제.</summary>
        public void ReleaseNow(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_keys.Remove(key))
            {
                AddressablesManager.ReleaseAsset(key);
            }
        }

        /// <summary>Addressables 에셋 로드 + 스코프 자동 등록.</summary>
        public async UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default) where T : Object
        {
            if (string.IsNullOrEmpty(key)) return null;
            var asset = await AddressablesManager.LoadAssetAsync<T>(key, ct);
            if (asset != null) RegisterKey(key);
            return asset;
        }

        /// <summary>프리팹 로드 + 인스턴스 생성 + 스코프 자동 등록/추적.</summary>
        public async UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken ct = default)
        {
            var prefab = await LoadAsync<GameObject>(key, ct);
            if (prefab == null) return null;
            var go = Instantiate(prefab, parent);
            TrackInstance(go);
            return go;
        }

        /// <summary>외부에서 만든 인스턴스를 스코프에 등록(정리 시 함께 파괴).</summary>
        public void TrackInstance(GameObject go)
        {
            if (go != null) _instances.Add(go);
        }

        /// <summary>해당 인스턴스만 즉시 파괴 및 추적 해제.</summary>
        public void DestroyInstanceNow(GameObject go)
        {
            if (go == null) return;
            if (_instances.Remove(go)) Destroy(go);
        }

        /// <summary>스코프에 등록된 모든 Addressable 키 Release.</summary>
        public void ReleaseAll()
        {
            if (_released) return;
            _released = true;

            for (int i = 0; i < _keys.Count; i++)
            {
                AddressablesManager.ReleaseAsset(_keys[i]);
            }
            _keys.Clear();

            if (_destroyInstancesOnRelease)
            {
                for (int i = 0; i < _instances.Count; i++)
                {
                    var go = _instances[i];
                    if (go) Destroy(go);
                }
            }
            _instances.Clear();
        }

        /// <summary>스코프에 등록된 인스턴스만 모두 파괴(키 해제는 하지 않음).</summary>
        public void DestroyAllInstances()
        {
            for (int i = 0; i < _instances.Count; i++)
            {
                var go = _instances[i];
                if (go) Destroy(go);
            }
            _instances.Clear();
        }

        private void OnDisable()
        {
            if (_releaseOnDisable) ReleaseAll();
        }

        private void OnDestroy()
        {
            if (_releaseOnDestroy) ReleaseAll();
        }
    }

    /// <summary>컴포넌트에서 스코프를 편하게 얻어오는 확장.</summary>
    public static class AddressableScopeMBExtensions
    {
        public static AddressableScopeMB GetAddressableScope(this Component c)
        {
            if (c == null) return null;
            var s = c.GetComponent<AddressableScopeMB>();
            if (s == null) s = c.gameObject.AddComponent<AddressableScopeMB>();
            return s;
        }
    }
}
