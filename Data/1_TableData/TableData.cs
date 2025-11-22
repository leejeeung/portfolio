// TableData.cs
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace jjevol
{
    public abstract class TableData : ListDataBase
    {
        public const string NAME_COLLECTION = "name";
        public const string DESCRIPTION_COLLECTION = "desc";

        public int id { get; protected set; }
        public int grade {  get; protected set; }

        // {id}_name, {id}_description 키로 로컬라이즈된 문자열 반환
        public virtual string name => GetLoc(NAME_COLLECTION, $"{id}");
        public virtual string description => GetLoc(DESCRIPTION_COLLECTION, $"{id}"); // 오타 키도 보조로 허용

        public TableData(TSVRow data = null)
        {
            if (data != null)
                Init(data);
        }

        public virtual void Init(TSVRow data)
        {
            _SetData(data);
        }

        private void _SetData(TSVRow data)
        {
            if (data != null)
            {
                if (data.Get("ID") != null)
                    this.id = data.GetInt("ID", 9999);
                else if (data.Get("id") != null)
                    this.id = data.GetInt("id", 9999);

                if (data.Get("grade") != null)
                    this.grade = data.GetInt("grade");
            }
        }

        // --- helpers ---
        public  static string GetLoc(string collection, string key, bool returnDebugKey = true)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (!LocalizationSettings.HasSettings || string.IsNullOrEmpty(collection))
                return key; // 설정 전이면 키를 그대로 반환(개발 중 확인용)

            var e = LocalizationSettings.StringDatabase.GetTableEntry(collection, key);

            if (e.Entry == null)
                return returnDebugKey ? key : string.Empty; // 키가 없으면 키를 그대로 반환

            return e.Entry.LocalizedValue;
        }

        // 보조 키(오타 등)까지 시도
        public static string GetLocWithFallback(string collection, string primaryKey, string secondaryKey)
        {
            var s = GetLoc(collection,primaryKey);
            if (!string.IsNullOrEmpty(s) && s != primaryKey) return s;

            // 2차 키 시도
            s = GetLoc(collection,secondaryKey);
            return (!string.IsNullOrEmpty(s) && s != secondaryKey) ? s : primaryKey;
        }
    }
}
