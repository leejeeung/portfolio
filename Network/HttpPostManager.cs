//#define DEBUG_BUILD
#define LOCAL_TEST

using UnityEngine;
using System;
using System.Net;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace jjevol.API
{
    public enum HttpStatus
    {
        Ok = 200,
        BadRequest = 400,
        Forbidden = 403, //InvalidToken
        NotFound = 404,
        ServerException = 500,
        ServerMaintenance = 503,
        Unknown = 9999,
        Timeout = 10000
    }

    public class HttpPostManager : Singleton<HttpPostManager>
    {
#if !LOCAL_TEST
        public static bool IsLocalTest = false;
#else
        public static bool IsLocalTest = true;
#endif
        public string DeviceToken { get; set; } = string.Empty;

        private bool isInit = false;

        //서버와의 통신이 다른 동작이 끝날때까지 대기해야되는 경우 사용(ex 광고시청후 소켓 재연결될때까지 대기)
        public static bool PausePost { get; set; }

        private IKeyReferenceCounter _keyReferenceCounter;

        public IKeyReferenceCounter ReferenceCounter
        {
            get
            {
                if (_keyReferenceCounter == null)
                    _keyReferenceCounter = new KeyReferenceCounter();

                return _keyReferenceCounter;
            }
        }

        public void SetReferenceCounter(IKeyReferenceCounter keyReferenceCounter)
        {
            _keyReferenceCounter = keyReferenceCounter;
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
                Init();
            }
        }

        public void Init()
        {
            if (!isInit)
            {
                StartCoroutine(_UpdatePostQueue());

                isInit = true;
            }
        }

        public virtual void SetLoading(bool isLoading)
        {
            if (isLoading)
                ReferenceCounter.Enable("WWW");
            else
                ReferenceCounter.Disable("WWW");
        }

        private Queue<WebRequestAPI> mPostQueue = new Queue<WebRequestAPI>();

        public Dictionary<string, object> GetTestRespons(bool success = true)
        {
            Dictionary<string, object> dict = new Dictionary<string, object>();

            dict.Add("success", success);
            dict.Add("code", 0);

            return dict;
        }

        #region DefaultFunction
        public void CheckInterNetState(bool isShowLoadingBar, Action<bool> action)
        {
            StartCoroutine(checkInternetConnection(isShowLoadingBar, action));
        }

        IEnumerator checkInternetConnection(bool isShowLoadingBar, Action<bool> action)
        {
            if (isShowLoadingBar)
                SetLoading(true);

            const string echoServer = "https://www.google.com";

            bool result;

            using (var request = UnityWebRequest.Head(echoServer))
            {
                request.timeout = 5;

                yield return request.SendWebRequest();

                result = request.result == UnityWebRequest.Result.Success;
            }

            if (isShowLoadingBar)
                SetLoading(false);

            action(result);
        }

        public void Post(WebRequestAPI requestAPI)
        {
            requestAPI.PostStart();

            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                CheckInterNetState(requestAPI.EnableLoadingIndicator, success =>
                {
                    if (!success)
                    {
                        var response = new JObject();
                        response["success"] = false;
                        response["code"] = (int)HttpStatus.Timeout;
                        response["errorCode"] = HttpStatus.Timeout.FastToString();

                        requestAPI.PostComplete(response);
                    }
                    else
                    {
#if LOCAL_TEST
                        if (requestAPI.LocalTest)
                            StartCoroutine(_Post_LocalTest(requestAPI));
                        else
                            StartCoroutine(_Post(requestAPI));
#else
                        StartCoroutine(_Post(requestAPI));
#endif
                    }
                });
            }
            else
            {
#if LOCAL_TEST
                if (requestAPI.LocalTest)
                    StartCoroutine(_Post_LocalTest(requestAPI));
                else
                    StartCoroutine(_Post(requestAPI));
#else
                StartCoroutine(_Post(requestAPI));
#endif
            }
        }

        public void PostQueue(WebRequestAPI requestAPI)
        {
            if (!ReferenceCounter.IsContain("PostQueue"))
                ReferenceCounter.Enable("PostQueue");

            mPostQueue.Enqueue(requestAPI);
        }

        IEnumerator _Post_LocalTest(WebRequestAPI requestAPI)
        {
#if UNITY_EDITOR || DEBUG_BUILD
            Debug.Log(requestAPI.URL);
            Debug.Log(Newtonsoft.Json.JsonConvert.SerializeObject(requestAPI.Param));
#else
            if (!Config.IS_REAL)
            {
                Debug.Log(requestAPI.URL);
                Debug.Log(Newtonsoft.Json.JsonConvert.SerializeObject(requestAPI.Param));
             }
#endif
            //Indicator
            if (requestAPI.EnableLoadingIndicator)
                SetLoading(true);

            var response = requestAPI.GetLocalTestResponse();

            float responseDelayTime = requestAPI.GetTestResponseDelayTime();

            if (responseDelayTime > 0)
                yield return new WaitForSecondsRealtime(responseDelayTime);

#if UNITY_EDITOR || DEBUG_BUILD
            Debug.Log(requestAPI.URL + " Recive : " + response);
#else
            if (!Config.IS_REAL)
            {
                Debug.Log(requestAPI.URL + " Recive : " + response);
            }
#endif
            requestAPI.PostComplete(response);

            //Indicator
            if (requestAPI.EnableLoadingIndicator)
                SetLoading(false);
        }

        public string GetLangCode()
        {
            switch (Config.CurrentLanguage)
            {
                case "Korean":
                    return "ko";

                case "English":
                    return "en";

                case "Japanese":
                    return "ja";

                case "Taiwan":
                    return "zh-TW";

                case "Spanish":
                    return "es";

                default:
                    return "en";
            }
        }

        IEnumerator _Post(WebRequestAPI requestAPI)
        {
#if UNITY_EDITOR || DEBUG_BUILD
            Debug.Log(requestAPI.URL);
            Debug.Log(Newtonsoft.Json.JsonConvert.SerializeObject(requestAPI.Param));
#else
            if (!Config.IS_REAL)
            {
                Debug.Log(requestAPI.URL);
                Debug.Log(Newtonsoft.Json.JsonConvert.SerializeObject(requestAPI.Param));
             }
#endif
            //Indicator
            if (requestAPI.EnableLoadingIndicator)
                SetLoading(true);

            //long postTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            JObject response = null;

            using (UnityWebRequest web = UnityWebRequest.PostWwwForm(requestAPI.URL, string.Empty))
            {
                web.SetRequestHeader("Content-Type", "application/json");
                /*캐쥬얼 서버에서는 사용안함
                web.SetRequestHeader("Lang", GetLangCode());

                if (!string.IsNullOrEmpty(DeviceToken))
                    web.SetRequestHeader("Device-Token", DeviceToken);
                */
                web.uploadHandler = new UploadHandlerRaw(Encrypt(requestAPI.PostData));
                web.downloadHandler = new DownloadHandlerBuffer();
                web.timeout = 15;

                yield return web.SendWebRequest();

                if (web.result == UnityWebRequest.Result.ConnectionError || web.result == UnityWebRequest.Result.ProtocolError)
                {
                    response = new JObject();

                    response["success"] = false;
                    response["code"] = web.responseCode;

                    if (web.result == UnityWebRequest.Result.ConnectionError)
                        response["errorCode"] = "NETWORK_NOT_CONNETED";
                    else
                        response["errorCode"] = web.error;
                }
                else
                {
                    //if (status == HttpStatus.Ok)
                    if (!string.IsNullOrEmpty(web.downloadHandler.text))
                    {
                        try
                        {
                            response = JObject.Parse(Decrypt(web.downloadHandler.text));
                        }
                        catch
                        {
                            response = new JObject();

                            response["success"] = false;
                            response["code"] = 9999;
                            response["errorCode"] = "Unknown";
                        }
                    }
                }

#if UNITY_EDITOR || DEBUG_BUILD
                Debug.Log("URL : " + requestAPI.URL + " Recive : " + response.ToString());
#else
                if (!Config.IS_REAL)
                       Debug.Log("URL : " + requestAPI.URL + " Recive : " + response.ToString());
#endif
            }

            var success = response.Get<bool>("success");
            var statusCode = response.Get<int>("code");
            var errorCode = response.Get<string>("errorCode");

            requestAPI.PostComplete(response);

            //Indicator
            if (requestAPI.EnableLoadingIndicator)
                SetLoading(false);
        }

        IEnumerator _UpdatePostQueue()
        {
            while (true)
            {
                if (mPostQueue.Count > 0)
                {
                    while (PausePost)
                        yield return null;

                    var api = mPostQueue.Dequeue();

                    Post(api);

                    yield return api;
                }
                else
                {
                    if (ReferenceCounter.IsContain("PostQueue"))
                        ReferenceCounter.Disable("PostQueue");

                    yield return null;
                }
            }
        }
        #endregion

        #region Encryption
        private static byte[] Encrypt(byte[] bytes)
        {
            return System.Text.Encoding.UTF8.GetBytes(System.Convert.ToBase64String(Aes.Encrypt(bytes, Aes.Type.Default)));
        }

        private static string Decrypt(string text)
        {
            byte[] decryptResult = null;
            bool isBase64String = Util.IsValidBase64String(text);

            try
            {
                if (!isBase64String) throw new Exception();

                if (!Aes.Decrypt(Convert.FromBase64String(text), Aes.Type.Default, out decryptResult)) throw new Exception();
                return System.Text.Encoding.UTF8.GetString(decryptResult);
            }
            catch
            {
                try
                {
                    if (!isBase64String) throw new Exception();
                    if (!Aes.Decrypt(Convert.FromBase64String(text), Aes.Type.Simple, out decryptResult)) throw new Exception();
                    return System.Text.Encoding.UTF8.GetString(decryptResult);
                }
                catch
                {
                    try
                    {
                        if (!isBase64String) throw new Exception();
                        return Util.Base64Decode(text, System.Text.Encoding.UTF8);
                    }
                    catch
                    {
                        //throw new Exception();
                    }
                }
            }
            return string.Empty;
        }
        #endregion
    }
}