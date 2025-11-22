using UnityEngine;
using System;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace jjevol
{
    /// <summary>
    /// 게임에서 사용하는 데이터 모델의 기본 클래스.
    /// - JSON 직렬화/역직렬화 지원
    /// - 로컬 저장 및 불러오기 기능 내장
    /// </summary>
    public abstract class ServerData : JsonDataBase
    {
        [JsonIgnore]
        /// <summary>
        /// 데이터의 고유 식별 키 (PlayerPrefs 저장 시 사용됨)
        /// </summary>
        public string DataKey { get; protected set; }

        [JsonIgnore]
        /// <summary>
        /// 로컬 저장 여부 설정 (기본값: true)
        /// </summary>
        public bool IsSaveLocal { get; protected set; } = true;

        /// <summary>
        /// 기본 생성자 – 빈 데이터 초기화
        /// </summary>
        public ServerData() : base() { }

        /// <summary>
        /// JSON 기반 생성자 – 초기 데이터를 받아 역직렬화
        /// </summary>
        public ServerData(JObject data) : base(data) { }

        /// <summary>
        /// 객체 소멸 시 호출되는 소멸자 – 리소스 정리
        /// </summary>
        ~ServerData()
        {
            Dispose();
        }

        /// <summary>
        /// 해제 처리가 필요한 리소스 정리용 메서드 – 상속 시 오버라이드 가능
        /// </summary>
        public virtual void Dispose()
        {
        }

        /// <summary>
        /// 현재 객체 상태를 JSON 객체로 직렬화하여 반환
        /// </summary>
        public virtual JObject ExportJson()
        {
            var json = JObject.FromObject(this, Serializer);
            return json;
        }

        public virtual JObject NormalizeData(JObject data)
        {
            return data.GetJSONObject(DataKey);
        }

        /// <summary>
        /// 현재 데이터를 암호화하여 PlayerPrefs에 저장
        /// </summary>
        public void Save()
        {
            // JSON 직렬화 → UTF8 변환 → AES 암호화 → Base64 인코딩
            byte[] encryptResult = Aes.Encrypt(System.Text.Encoding.UTF8.GetBytes(ExportJson().ToString()), Aes.Type.None);
            string saveData = Convert.ToBase64String(encryptResult);

            // PlayerPrefs에 저장
            PlayerPrefs.SetString(DataKey, saveData);
        }

        /// <summary>
        /// PlayerPrefs에서 데이터를 불러와 복호화 후 ImportJson 수행
        /// </summary>
        public void Load()
        {
            string loadData = PlayerPrefs.GetString(DataKey, "");

            if (loadData != "")
            {
                byte[] decryptResult = null;

                // Base64 → AES 복호화 → UTF8 디코딩
                Aes.Decrypt(Convert.FromBase64String(loadData), Aes.Type.None, out decryptResult);
                loadData = System.Text.Encoding.UTF8.GetString(decryptResult);

                JObject jsonData = JObject.Parse(loadData);

#if UNITY_EDITOR || DEBUG_BUILD
                Debug.Log("Load " + DataKey + " : \n" + loadData);
#endif
                // 불러온 데이터를 객체에 반영
                ImportJson(jsonData);
            }
            else
            {
                Debug.LogWarning("RHEntity Load Fail : " + DataKey);
            }
        }
    }
}
