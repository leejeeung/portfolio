using System.Collections.Generic;
using UnityEngine;
using System;
using jjevol.API;
using Newtonsoft.Json.Linq;

namespace jjevol
{
    public abstract class ServerDataRepository
    {
        public bool Inited { get; private set; }//초기화 유무 초기화 안되있으면 해당 클래스 사용하면 안됨
        public Dictionary<string, ServerData> DataDict = new Dictionary<string, ServerData>();

        public virtual void ImportJson(JObject data, bool isLocalData = false)
        {
            if (data == null) return;

            foreach (var db in DataDict.Values)
            {
                if ((!isLocalData || db.IsSaveLocal))
                {
                    var normalizeData = db.NormalizeData(data);

                    if (normalizeData != null)
                        db.ImportJson(normalizeData);
                }
            }

            JObject updatedData = data.GetJSONObject("updatedData");

            if (updatedData != null)
            {
                foreach (var db in DataDict.Values)
                {
                    if ((!isLocalData || db.IsSaveLocal))
                    {
                        var normalizeData = db.NormalizeData(updatedData);

                        if (normalizeData != null)
                            db.UpdateJson(normalizeData);
                    }
                }
            }
        }

        public virtual JObject ExportJson()
        {
            Dictionary<string, object> data = new Dictionary<string, object>();

            foreach (var db in DataDict.Values)
            {
                if (db.IsSaveLocal)
                    data.Add(db.DataKey, db.ExportJson());
            }

            return JObject.FromObject(data);
        }

        public virtual void Save()
        {
            string jsonString = ExportJson().ToString();
            byte[] encryptResult = Aes.Encrypt(System.Text.Encoding.UTF8.GetBytes(jsonString), Aes.Type.None);
            string saveData = Convert.ToBase64String(encryptResult);

            Debug.LogWarning("Save : " + jsonString);
            PlayerPrefs.SetString("SaveData", saveData);
        }

        public virtual void Load()
        {
            string loadData = PlayerPrefs.GetString("SaveData", "");

            if (loadData != "")
            {
                byte[] decryptResult = null;
                Aes.Decrypt(Convert.FromBase64String(loadData), Aes.Type.None, out decryptResult);
                loadData = System.Text.Encoding.UTF8.GetString(decryptResult);

                Debug.LogWarning("loadData : " + loadData);

                ImportJson(JObject.Parse(loadData), true);
            }
        }

        public WebRequestBatch LoadServerData(Action<bool, int, string, JObject> onComplete = null)
        {
            List<WebRequestAPI> apiList = new List<WebRequestAPI>();

            foreach (var db in DataDict.Values)
            {
                if (db is IServerDataRequest loadServer)
                    apiList.Add(loadServer.RequestDatas());
            }

            return new WebRequestBatch(apiList, onComplete);
        }

        public void Init()
        {
            if (!Inited)
            {
                DataDict.Clear();

                _Init();

                Inited = true;
            }
        }

        protected abstract void _Init();

        //DB정보 초기화
        public virtual void Release()
        {
            foreach (var dbData in DataDict.Values)
                dbData.Dispose();

            DataDict.Clear();

            Inited = false;

            Init();
        }
    }
}