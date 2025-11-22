using Cysharp.Threading.Tasks;
using jjevol;
using System.Threading;
using UnityEngine;
using UnityEngine.U2D;

public sealed class IconLoader : MonoBehaviour
{
    [SerializeField] private SpriteRenderer _renderer;

    private AddressableScopeMB _scope;
    private CancellationTokenSource _cts; // in-flight 로딩 취소용
    private string _currentKey;           // 지금 적용 중(또는 로딩 중)인 키

    void Awake()
    {
        _scope = this.GetAddressableScope(); // AddressableScopeMB 자동 추가/획득
    }

    public async UniTaskVoid SetIconAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            // 아이콘 제거
            _renderer.sprite = null;
            if (!string.IsNullOrEmpty(_currentKey))
            {
                _scope.ReleaseNow(_currentKey);
                _currentKey = null;
            }
            CancelInFlight();
            return;
        }

        // 1) 이전 로딩이 진행 중이면 즉시 취소
        CancelInFlight();

        // 2) 현재 적용 중 아이콘이 있고 "다른 키"면, 먼저 해제
        if (!string.IsNullOrEmpty(_currentKey) && _currentKey != key)
        {
            _scope.ReleaseNow(_currentKey);
            _currentKey = null;
            _renderer.sprite = null;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            // 3) 새 스프라이트 로드(스코프 자동 등록). 취소되면 OperationCanceledException
            var sprite = await _scope.LoadAsync<Sprite>(key, ct);
            if (ct.IsCancellationRequested) return;

            // 4) 적용
            _renderer.sprite = sprite;
            _currentKey = key; // 이제 이 키가 "현재 적용 중"
        }
        catch (System.OperationCanceledException)
        {
            // 취소: 여기서 아무 것도 안 하면 됨
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Icon load failed: key={key}, err={ex.Message}");
        }
        finally
        {
            DisposeCts();
        }
    }

    public async UniTaskVoid SetIconFromAtlasAsync(string atlasKey, string spriteName)
    {
        // 1) 기존 로딩 취소/해제 루틴은 SetIconAsync와 동일하게 유지
        CancelInFlight();

        if (!string.IsNullOrEmpty(_currentKey) && _currentKey != atlasKey)
        {
            _scope.ReleaseNow(_currentKey);
            _currentKey = null;
            _renderer.sprite = null;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            // 2) 아틀라스 로드
            var atlas = await _scope.LoadAsync<SpriteAtlas>(atlasKey, ct);
            if (ct.IsCancellationRequested || atlas == null) return;

            // 3) 조각 이름으로 스프라이트 추출 후 적용
            var sprite = atlas.GetSprite(spriteName);
            _renderer.sprite = sprite;
            _currentKey = atlasKey; // 스코프 해제를 위해 현재 키 갱신
        }
        catch (System.OperationCanceledException) { /* 무시 */ }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Icon atlas load failed: key={atlasKey}, name={spriteName}, err={ex.Message}");
        }
        finally
        {
            DisposeCts();
        }
    }

    private void CancelInFlight()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
            _cts.Cancel();
        DisposeCts();
    }

    private void DisposeCts()
    {
        _cts?.Dispose();
        _cts = null;
    }
}
