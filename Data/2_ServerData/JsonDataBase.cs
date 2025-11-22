using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;

namespace jjevol
{
    /// <summary>
    /// 게임 내 JSON 직렬화 및 병합 로직을 담당하는 기본 클래스.
    /// - 리스트/딕셔너리 지원
    /// - Import/Update 차별화
    /// - 커스텀 비교 지원
    /// </summary>
    public abstract class JsonDataBase : ListDataBase
    {
        private readonly struct MemberAdapter
        {
            public readonly string JsonName;   // JsonProperty 이름 or 멤버명
            public readonly Type MemberType;
            public readonly Func<object> Get;
            public readonly Action<object> Set;

            public MemberAdapter(string jsonName, Type type, Func<object> getter, Action<object> setter)
            {
                JsonName = jsonName;
                MemberType = type;
                Get = getter;
                Set = setter;
            }
        }

        // RHJsonDataConverter 및 DateTimeConverter가 적용된 기본 직렬화기
        protected static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters = { new RHJsonDataConverter(), new DateTimeConverter() },
            NullValueHandling = NullValueHandling.Ignore
        });

        private static readonly ConcurrentDictionary<Type, MemberAdapter[]> s_memberCache = new();

        private MemberAdapter[] GetMembersCached()
        {
            var t = GetType();
            return s_memberCache.GetOrAdd(t, _ => GetReadableWritableMembers().ToArray());
        }

        private IEnumerable<MemberAdapter> GetReadableWritableMembers()
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;

            // Properties
            foreach (var p in GetType().GetProperties(Flags))
            {
                if (!p.CanRead || !p.CanWrite) continue;

                // [JsonProperty] 지원
                var jsonAttr = p.GetCustomAttribute<JsonPropertyAttribute>();
                var jsonName = string.IsNullOrEmpty(jsonAttr?.PropertyName) ? p.Name : jsonAttr.PropertyName;

                yield return new MemberAdapter(
                    jsonName,
                    p.PropertyType,
                    () => p.GetValue(this),
                    v => p.SetValue(this, v)
                );
            }


            
            // Fields
            foreach (var f in GetType().GetFields(Flags))
            {
                // [JsonProperty] 지원
                var jsonAttr = f.GetCustomAttribute<JsonPropertyAttribute>();
                var jsonName = string.IsNullOrEmpty(jsonAttr?.PropertyName) ? f.Name : jsonAttr.PropertyName;

                yield return new MemberAdapter(
                    jsonName,
                    f.FieldType,
                    () => f.GetValue(this),
                    v => f.SetValue(this, v)
                );
            }
        }

        private static object CreateCollectionInstance(Type t)
        {
            if (t.IsInterface && t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                var args = t.GetGenericArguments();

                if (def == typeof(IList<>))
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(args[0]));

                if (def == typeof(IDictionary<,>))
                    return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(args));
            }
            return Activator.CreateInstance(t);
        }

        /// <summary>
        /// 객체를 JSON 문자열로 변환 (간결한 Formatting.None 사용)
        /// </summary>
        public override string ToString() => JObject.FromObject(this, Serializer).ToString(Formatting.None);

        /// <summary>
        /// 빈 생성자
        /// </summary>
        public JsonDataBase() { }

        /// <summary>
        /// 초기 JSON 데이터로 객체 생성
        /// </summary>
        public JsonDataBase(JObject data) => ImportJson(data);

        /// <summary>
        /// 전체 데이터를 JSON으로부터 덮어쓰기(Replace 방식)
        /// - 컬렉션은 교체
        /// - 나머지는 역직렬화
        /// </summary>
        public virtual bool ImportJson(JObject data)
        {
            if (data == null) return false;

            ReplaceCollections(data);   // 컬렉션 우선 처리
            DeserializeFields(data);    // 일반 필드 처리

            return true;
        }

        /// <summary>
        /// 기존 데이터에 JSON을 병합(Update 방식)
        /// - 컬렉션은 병합
        /// - 나머지는 역직렬화
        /// </summary>
        public virtual bool UpdateJson(JObject data)
        {
            if (data == null) return false;

            UpdateCollections(data);    // 컬렉션 병합 처리
            DeserializeFields(data);    // 일반 필드 병합

            return true;
        }

        /// <summary>
        /// 일반 필드를 JSON으로부터 역직렬화 (Json.NET 사용)
        /// </summary>
        protected virtual void DeserializeFields(JObject data)
        {        
            using (var reader = data.CreateReader())
            {
                Serializer.Populate(reader, this);
            }
        }

        /// <summary>
        /// List, Dictionary 컬렉션을 완전히 새로 교체
        /// </summary>
        protected virtual void ReplaceCollections(JObject data)
        {
            foreach (var m in GetMembersCached())
            {
                if (!TryGetToken(data, m.JsonName, out var token)) continue;

                var value = m.Get();
                var isList = typeof(IList).IsAssignableFrom(m.MemberType);
                var isDict = typeof(IDictionary).IsAssignableFrom(m.MemberType);

                if (!isList && !isDict) continue;

                // 컬렉션이 null이면 즉시 생성
                if (value == null)
                {
                    value = CreateCollectionInstance(m.MemberType);
                    m.Set(value);
                }

                if (isList && token is JArray arr)
                {
                    HandleListReplace(m.MemberType, (IList)value, arr);
                    data.Remove(m.JsonName);
                }
                else if (isDict && token is JObject obj)
                {
                    HandleDictionaryReplace(m.MemberType, (IDictionary)value, obj);
                    data.Remove(m.JsonName);
                }
            }
        }

        /// <summary>
        /// List, Dictionary 컬렉션을 병합 처리 (기존 데이터 유지하면서 추가/갱신)
        /// </summary>
        protected virtual void UpdateCollections(JObject data)
        {
            foreach (var m in GetMembersCached())
            {
                if (!TryGetToken(data, m.JsonName, out var token)) continue;

                var value = m.Get();
                var isList = typeof(IList).IsAssignableFrom(m.MemberType);
                var isDict = typeof(IDictionary).IsAssignableFrom(m.MemberType);

                if (!isList && !isDict) continue;

                if (value == null)
                {
                    value = CreateCollectionInstance(m.MemberType);
                    m.Set(value);
                }

                if (isList && token is JArray arr)
                {
                    HandleListUpdate(m.MemberType, (IList)value, arr);
                    data.Remove(m.JsonName);
                }
                else if (isDict && token is JObject obj)
                {
                    HandleDictionaryUpdate(m.MemberType, (IDictionary)value, obj);
                    data.Remove(m.JsonName);
                }
            }
        }

        // 3) 토큰 조회 도우미(대소문자 민감도 낮추고 [JsonProperty]명 우선)
        private static bool TryGetToken(JObject data, string name, out JToken token)
        {
            if (data.TryGetValue(name, out token)) return true;

            // 대소문자 무시 매칭 (필요 시)
            var prop = data.Properties()
                           .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (prop != null) { token = prop.Value; return true; }

            token = null;
            return false;
        }

        // 4) 핸들러들을 타입기반 시그니처로 확장 (PropertyInfo 의존 제거)
        protected virtual void HandleListReplace(Type listType, IList list, JArray arrayData)
        {
            if (arrayData == null) return;
            var elementType = listType.IsGenericType ? listType.GetGenericArguments()[0] : typeof(object);
            list.Clear();

            foreach (var item in arrayData)
            {
                var instance = Activator.CreateInstance(elementType);
                using (var reader = item.CreateReader())
                {
                    Serializer.Populate(reader, instance); // JsonDataBase 상단의 static Serializer 재사용
                }
                list.Add(instance);
            }
        }

        protected virtual void HandleDictionaryReplace(Type dictType, IDictionary dict, JObject dictData)
        {
            if (dictData == null) return;
            var args = dictType.GetGenericArguments();
            var keyType = args[0];
            var valueType = args[1];

            var keysToRemove = new List<object>();
            foreach (var key in dict.Keys)
            {
                var keyString = key.ToString();
                if (!dictData.ContainsKey(keyString))
                    keysToRemove.Add(key);
            }

            foreach (var pair in dictData)
            {
                var key = Convert.ChangeType(pair.Key, keyType);
                if (dict.Contains(key))
                {
                    if (dict[key] is JsonDataBase existingModel && pair.Value is JObject obj)
                    {
                        existingModel.ImportJson(obj);
                    }
                    else
                    {
                        using (var reader = pair.Value.CreateReader())
                        {
                            Serializer.Populate(reader, dict[key]);
                        }
                    }
                }
                else
                {
                    var newValue = Activator.CreateInstance(valueType);
                    using (var reader = pair.Value.CreateReader())
                    {
                        Serializer.Populate(reader, newValue);
                    }
                    dict[key] = newValue;
                }
            }

            foreach (var key in keysToRemove)
                dict.Remove(key);
        }

        protected virtual void HandleListUpdate(Type listType, IList list, JArray arrayData)
        {
            if (arrayData == null) return;
            var elementType = listType.IsGenericType ? listType.GetGenericArguments()[0] : typeof(object);

            foreach (var item in arrayData)
            {
                var newObj = Activator.CreateInstance(elementType);
                using (var reader = item.CreateReader())
                {
                    Serializer.Populate(reader, newObj);
                }

                bool matched = false;
                foreach (var existingObj in list)
                {
                    if (existingObj is JsonDataBase existingModel && newObj is JsonDataBase newModel)
                    {
                        if (existingModel.CompareModel(newModel))
                        {
                            existingModel.UpdateJson(JObject.FromObject(newModel, Serializer));
                            matched = true;
                            break;
                        }
                    }
                }
                if (!matched) list.Add(newObj);
            }
        }

        protected virtual void HandleDictionaryUpdate(Type dictType, IDictionary dict, JObject dictData)
        {
            if (dictData == null) return;
            var args = dictType.GetGenericArguments();
            var keyType = args[0];
            var valueType = args[1];

            foreach (var pair in dictData)
            {
                var key = Convert.ChangeType(pair.Key, keyType);
                if (dict.Contains(key))
                {
                    if (dict[key] is JsonDataBase existingModel && pair.Value is JObject obj)
                    {
                        existingModel.UpdateJson(obj);
                    }
                    else
                    {
                        using (var reader = pair.Value.CreateReader())
                        {
                            Serializer.Populate(reader, dict[key]);
                        }
                    }
                }
                else
                {
                    var newValue = Activator.CreateInstance(valueType);
                    using (var reader = pair.Value.CreateReader())
                    {
                        Serializer.Populate(reader, newValue);
                    }
                    dict[key] = newValue;
                }
            }
        }

        /// <summary>
        /// 객체 동일성을 판단하는 기준 메서드
        /// - 기본은 참조 비교
        /// - 오버라이드하여 ID 기반 비교 등으로 변경 가능
        /// </summary>
        public virtual bool CompareModel(JsonDataBase other)
        {
            return ReferenceEquals(this, other);
        }

        /// <summary>
        /// 다른 객체로부터 JSON을 가져와 현재 객체에 적용
        /// </summary>
        public virtual void AssignFrom(JsonDataBase other)
        {
            var json = JObject.FromObject(other);
            ImportJson(json);
        }
    }
}
