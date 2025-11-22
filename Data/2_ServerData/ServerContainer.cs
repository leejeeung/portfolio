using UnityEngine;
using Newtonsoft.Json.Linq;
using jjevol.API;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace jjevol
{
    /// <summary>
    /// 서버로부터 데이터를 요청하는 인터페이스
    /// </summary>
    public interface IServerDataRequest
    {
        public WebRequestAPI RequestDatas(Action<bool, int, string, JObject> callback = null);
    }

    /// <summary>
    /// 키-값 형태의 게임 데이터를 저장하고 관리하는 제네릭 컨테이너 클래스.
    /// - JSON 구조 정규화
    /// - Import/Update 병합 처리
    /// - 로컬 저장 지원
    /// </summary>
    public abstract class ServerContainer<TKey, TValue> : ServerData where TValue : ServerData
    {
        [JsonIgnore]
        /// <summary>
        /// JSON 내부에서 리스트를 감싸는 Key (ex. "items", "resources" 등)
        /// </summary>
        protected abstract string containerKey { get; }

        /// <summary>
        /// JSON 데이터로부터 고유 키를 추출하는 함수
        /// </summary>
        protected abstract bool _ExportKey(JObject data, out TKey key);

        /// <summary>
        /// JSON 데이터를 기반으로 TValue 객체를 생성하는 함수
        /// </summary>
        protected abstract TValue _CreateData(JObject data);

        /// <summary>
        /// 실제 데이터를 보관하는 사전 구조 (key → TValue)
        /// </summary>
        [JsonIgnore]
        public Dictionary<TKey, TValue> hasItems { get; protected set; } = new();

        /// <summary>
        /// 전체 데이터 변경 시 발생하는 이벤트
        /// </summary>
        public event Action OnChange;

        /// <summary>
        /// 특정 항목 변경 시 발생하는 이벤트
        /// </summary>
        public event Action<TValue> OnChangeItem;

        /// <summary>
        /// 데이터 구독 여부 (기본값: true)
        /// </summary>
        protected bool mSubscribe = false;

        /// <summary>
        /// 구독 상태 변경 처리. subscribe 상태가 변경될 경우 true 반환
        /// </summary>
        protected virtual bool _SetSubscribe(bool subscribe)
        {
            if (mSubscribe != subscribe)
            {
                mSubscribe = subscribe;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 기본 생성자 – 구독 상태 활성화
        /// </summary>
        public ServerContainer() : base()
        {
            _SetSubscribe(true);
        }

        /// <summary>
        /// JSON 데이터를 받아 초기화하는 생성자 – 구독 상태 활성화
        /// </summary>
        public ServerContainer(JObject data) : base(data)
        {
            _SetSubscribe(true);
        }

        /// <summary>
        /// 내부 데이터 초기화 (필요 시 상속하여 확장)
        /// </summary>
        public virtual void Clear()
        {
            hasItems.Clear();
        }

        public override JObject ExportJson()
        {
            var json = base.ExportJson();

            var array = new JArray();

            foreach (var value in hasItems.Values)
                array.Add(value.ExportJson());

            json[containerKey] = array;

            return json;
        }

        /// <summary>
        /// JSON 데이터를 받아 정규화 → Dictionary 초기화 → ImportJson 처리
        /// </summary>
        public override bool ImportJson(JObject data)
        {
            if (data == null) return false;

            hasItems.Clear();

            foreach (var containData in data.GetJSONList(containerKey))
            {
                if (_ExportKey(containData, out TKey key))
                    _ImportData(key, containData);
            }

            base.ImportJson(data);

            return true;
        }

        /// <summary>
        /// JSON 데이터를 받아 정규화 → Dictionary 초기화 → UpdateJson 병합 처리
        /// </summary>
        public override bool UpdateJson(JObject data)
        {
            if (data == null) return false;

            foreach (var containData in data.GetJSONList(containerKey))
            {
                if (_ExportKey(containData, out TKey key))
                    _UpdateData(key, containData);
            }

            base.UpdateJson(data);

            return true;
        }

        /// <summary>
        /// 단일 항목 Import 처리
        /// - 기존 키가 있으면 덮어쓰기
        /// - 없으면 새로 생성
        /// </summary>
        protected virtual void _ImportData(TKey key, JObject containData)
        {
            if (hasItems.ContainsKey(key))
            {
                hasItems[key].ImportJson(containData);
            }
            else
            {
                hasItems.Add(key, _CreateData(containData));
            }

            OnChangeItem?.Invoke(hasItems[key]);
        }

        /// <summary>
        /// 단일 항목 Update 처리
        /// - 기존 키가 있으면 병합
        /// - 없으면 새로 생성
        /// </summary>
        protected virtual void _UpdateData(TKey key, JObject containData)
        {
            if (hasItems.ContainsKey(key))
            {
                hasItems[key].UpdateJson(containData);
            }
            else
            {
                hasItems.Add(key, _CreateData(containData));
            }

            OnChangeItem?.Invoke(hasItems[key]);
        }

        /// <summary>
        /// JSON 데이터를 정규화하여 containerKey 기반 구조로 맞춤
        /// - 단일 객체는 배열로 감싸고 반환
        /// - 이미 정규화된 경우는 그대로 반환
        /// </summary>
        public override JObject NormalizeData(JObject data)
        {
            var token = data.Get(DataKey);

            if (token != null)
            {
                if (token.Type == JTokenType.Array)
                {
                    if (DataKey == containerKey)
                        return data;
                    else
                        return new JObject { [containerKey] = token.ToObject<JArray>() };
                }
                else
                {
                    return token as JObject;
                }
            }

            token = data.Get(containerKey);

            if (token != null)
            {
                if (token.Type == JTokenType.Array)
                {
                    return data;
                }
                else
                {
                    var obj = token as JObject;
                    return new JObject { [containerKey] = new JArray(obj) };
                }
            }

            return null;
        }

        /// <summary>
        /// 키에 해당하는 항목 조회
        /// </summary>
        public TValue Get(TKey id)
        {
            return hasItems.TryGetValue(id, out var item) ? item : null;
        }

        public bool TryGet(TKey id, out TValue item)
        {
            item = Get(id);

            return item != null;
        }

        /// <summary>
        /// JSON을 받아 키 기준으로 Add 또는 Replace 처리
        /// - 변경 이벤트 발생
        /// </summary>
        public void AddOrReplace(TKey key, JObject json)
        {
            _UpdateData(key, json);
            OnChange?.Invoke();
        }

        /// <summary>
        /// 객체(TValue)를 받아 키 기준으로 Add 또는 Replace 처리
        /// - 변경 이벤트 발생
        /// </summary>
        public void AddOrReplace(TKey key, TValue value)
        {
            hasItems[key] = value;
            OnChangeItem?.Invoke(value);
            OnChange?.Invoke();
        }

        /// <summary>
        /// 리소스 해제 및 구독 종료
        /// </summary>
        public override void Dispose()
        {
            _SetSubscribe(false);
        }

        /// <summary>
        /// PlayerPrefs에서 해당 데이터 키 삭제
        /// </summary>
        public virtual void Delete()
        {
            PlayerPrefs.DeleteKey(DataKey);
        }
    }
}
