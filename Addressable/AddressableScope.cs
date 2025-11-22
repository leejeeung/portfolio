// Assets/ProjectArcana/Script/Common/AddressableScope.cs
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace jjevol
{
    /// <summary>
    /// AddressablesManager.LoadAssetAsync로 가져온 에셋을 스코프 종료 시 자동 Release하는 리스(Lease).
    /// using(var lease = await AddressableScope.Get<T>(key)) { var asset = lease.Asset; ... }
    /// </summary>
    public sealed class AddressableLease<T> : IDisposable where T : UnityEngine.Object
    {
        public string Key { get; private set; }
        public T Asset { get; private set; }
        bool _disposed;

        internal AddressableLease(string key, T asset)
        {
            Key = key;
            Asset = asset;
        }

        public static implicit operator T(AddressableLease<T> lease) => lease.Asset;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!string.IsNullOrEmpty(Key))
            {
                // AddressablesManager가 ref-count 관리 → ReleaseAsset만 호출하면 됨
                AddressablesManager.ReleaseAsset(Key);
            }
            Asset = null;
            Key = null;
        }
    }

    /// <summary>
    /// 여러 키를 한 스코프에서 관리하고 싶을 때 사용. Dispose 시 모두 Release.
    /// </summary>
    public sealed class AddressableMultiScope : IDisposable
    {
        readonly List<string> _keys = new List<string>(8);
        bool _disposed;

        /// <summary>스코프에 키를 등록 (이미 로드되었다고 가정, 스코프 끝에서 Release)</summary>
        public void Add(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _keys.Add(key);
        }

        /// <summary>스코프에서 관리 중인 키를 즉시 Release하고 목록에서 제거</summary>
        public void ReleaseNow(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_keys.Remove(key))
            {
                AddressablesManager.ReleaseAsset(key);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < _keys.Count; i++)
            {
                AddressablesManager.ReleaseAsset(_keys[i]);
            }
            _keys.Clear();
        }
    }

    public static class AddressableScope
    {
        /// <summary>
        /// 에셋을 로드하고, 스코프가 끝나면 자동 Release되는 리스를 반환.
        /// 사용법:
        /// using (var lease = await AddressableScope.Get&lt;HitEffectSO&gt;(key, ct)) { var fx = lease.Asset; ... }
        /// </summary>
        public static async UniTask<AddressableLease<T>> Get<T>(string key, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(key)) return null;

            var asset = await AddressablesManager.LoadAssetAsync<T>(key, ct);
            if (asset == null) return null;

            return new AddressableLease<T>(key, asset);
        }

        /// <summary>
        /// 이미 로드된(혹은 바로 GetAsset로 꺼낼 수 있는) 키를 스코프에 등록하고, 에셋 참조까지 함께 반환.
        /// - AddressablesManager.GetAsset&lt;T&gt;(key) 호출 + 스코프 등록.
        /// - 호출자가 스코프를 Dispose하면 해당 키 Release.
        /// </summary>
        public static T AddToScope<T>(this AddressableMultiScope scope, string key)
        {
            if (scope == null || string.IsNullOrEmpty(key)) return default;
            var asset = AddressablesManager.Instance.GetAsset<T>(key);
            if (asset != null) scope.Add(key);
            return asset;
        }

        /// <summary>
        /// 프리팹을 로드→리스 반환. 인스턴스 생성까지 같이 하고 싶을 때는 InstantiateWithLease 사용.
        /// </summary>
        public static async UniTask<AddressableLease<GameObject>> GetPrefab(string key, CancellationToken ct = default)
            => await Get<GameObject>(key, ct);

        /// <summary>
        /// 프리팹을 로드하고 인스턴스를 만들어 리턴. using 종료 시 자동 Release(에셋)는 물론,
        /// 인스턴스도 선택적으로 파괴할 수 있게 IDisposable 토큰을 함께 반환.
        /// </summary>
        public static async UniTask<(GameObject instance, IDisposable lease, IDisposable destroyOnDispose)>
            InstantiateWithLease(string key, Transform parent = null, CancellationToken ct = default)
        {
            var lease = await GetPrefab(key, ct);
            if (lease == null || lease.Asset == null) return (null, null, null);

            var go = UnityEngine.Object.Instantiate(lease.Asset, parent);
            // 인스턴스 파괴용 토큰
            var destroyToken = new ActionDisposable(() =>
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            });

            // 호출 측: using(lease) { ... }  +  using(destroyToken) { ... } 식으로 관리 가능
            return (go, lease, destroyToken);
        }

        private sealed class ActionDisposable : IDisposable
        {
            Action _onDispose;
            bool _disposed;
            public ActionDisposable(Action onDispose) { _onDispose = onDispose; }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _onDispose?.Invoke();
                _onDispose = null;
            }
        }
    }
}
