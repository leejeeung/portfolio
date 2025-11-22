using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Text;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Linq;

namespace jjevol.API
{
    public static class WebRequestAPIExtensions
    {
        public static void PostQueue(this WebRequestAPI API)
        {
            var activeScene = SceneManager.GetActiveScene();

            if (activeScene != null)
                HttpPostManager.Instance.PostQueue(API);
        }

        public static void PostQueue(this WebRequestAPI API, Action<bool, int, string, JObject> callback)
        {
            var activeScene = SceneManager.GetActiveScene();

            if (activeScene != null)
            {
                API.SetCallback(callback);

                HttpPostManager.Instance.PostQueue(API);
            }
        }

        public static void Post(this WebRequestAPI API)
        {
            var activeScene = SceneManager.GetActiveScene();

            if (activeScene != null)
                HttpPostManager.Instance.Post(API);
        }

        public static void Post(this WebRequestAPI API, Action<bool, int, string, JObject> callback)
        {
            var activeScene = SceneManager.GetActiveScene();

            if (activeScene != null)
            {
                API.SetCallback(callback);

                HttpPostManager.Instance.Post(API);
            }
        }

        public static int GetPostCount(this WebRequestAPI API)
        {
            return WebRequestAPI.GetAPIPostCount(API);
        }

        public static string GetErrorMessage(this WebRequestAPI API)
        {
            return GetErrorMessage(API.ErrorCode);
        }

        public static string GetErrorMessage(string errorCode)
        {
            string errorMessage = string.Empty;

            //if (!string.IsNullOrEmpty(errorCode))
            //{
            //    errorMessage = I2.Loc.LocalizationManager.GetTranslation(errorCode);

            //    if (string.IsNullOrEmpty(errorMessage))
            //        errorMessage = errorCode;
            //}
            //else
            //{
            //    errorMessage = I2.Loc.LocalizationManager.GetTranslation("Msg/NetworkFail");
            //}

            return errorMessage;
        }
    }

    public class WebRequestBatch : CustomYieldInstruction
    {
        private List<WebRequestAPI> apis;
        private int remaining;
        private bool isDone = false;
        private bool earlyFailed = false;

        private Action<bool, int, string, JObject> onComplete;

        // 최종 결과값
        public bool TransportSuccess { get; private set; } = true;

        public bool RequestSuccess => TransportSuccess && string.IsNullOrEmpty(ErrorMsg);

        public int Code { get; private set; } = 0;
        public string ErrorMsg { get; private set; } = string.Empty;
        public JObject Result { get; private set; } = null;

        public override bool keepWaiting => !isDone;

        public WebRequestBatch(IEnumerable<WebRequestAPI> apiList, Action<bool, int, string, JObject> onComplete = null)
        {
            apis = new List<WebRequestAPI>(apiList);
            remaining = apis.Count;
            this.onComplete = onComplete;

            foreach (var api in apis)
            {
                if (api.Completed)
                {
                    HandleResult(api);
                }
                else
                {
                    api.SetCallback((success, code, error, result) =>
                    {
                        HandleResult(api);
                    });
                }
            }

            CheckIfAllDone();
        }

        private void HandleResult(WebRequestAPI api)
        {
            if (isDone) return;

            if (!api.RequestSuccess)
            {
                // 첫 실패 기준으로 종료
                earlyFailed = true;
                TransportSuccess = false;
                Code = api.StatusCode;
                ErrorMsg = api.ErrorCode;
                Result = api.Result;
                isDone = true;
                return;
            }

            remaining--;
            CheckIfAllDone();
        }

        private void CheckIfAllDone()
        {
            if (!earlyFailed && remaining <= 0)
            {
                // 전부 성공
                TransportSuccess = true;
                Code = 0;
                ErrorMsg = string.Empty;
                Result = null;
                isDone = true;

                onComplete?.Invoke(TransportSuccess, Code, ErrorMsg, Result);
            }
        }
    }

    public abstract class WebRequestAPI : CustomYieldInstruction
    {
        public static Dictionary<string, int> API_POST_COUNT_DICTIONARY = new Dictionary<string, int>();
        public static bool WaitingForResponse()
        {
            return API_POST_COUNT_DICTIONARY.Count > 0;
        }

        public static int GetAPIPostCount(WebRequestAPI api)
        {
            return API_POST_COUNT_DICTIONARY.TryGetValue(api.API, out int count) ? count : 0;
        }

        public bool EnableLoadingIndicator { get; set; } = true;
        public bool UseDefaultErrorHandling { get; set; } = true;

        public virtual string BaseURL
        {
            get { return Config.BASE_URL; }
        }

        protected string _api;

        public virtual string API { get { return _api; } }

        public virtual string URL
        {
            get
            {
                StringBuilder url = new StringBuilder();

                url.Append(BaseURL);
                url.Append(API);

                return url.ToString();
            }
        }

        private JObject mParam;
        public JObject Param
        {
            get
            {
                if (mParam == null)
                    mParam = _CloneDefaultParam();

                return mParam;
            }
        }

        public virtual float GetTestResponseDelayTime()
        {
            return Util.RandFloat(0, 0.8f);
        }

        protected abstract JObject _CloneDefaultParam();

        public byte[] PostData
        {
            get
            {
                string jsonData = Param.ToString();

                if (string.IsNullOrEmpty(jsonData))
                    return new byte[0];

                return Encoding.UTF8.GetBytes(jsonData);
            }
        }

        protected Action<bool, int, string, JObject> mCallback;
        public DateTime StartTime { get; private set; }

        public bool LocalTest { get; protected set; } = true;

        public WebRequestAPI()
        {
            StartTime = Timef.time;
        }

        public WebRequestAPI SetAPI(string api)
        {
            _api = api;

            return this;
        }

        public WebRequestAPI SetParam(JObject additional)
        {
            mParam = _CloneDefaultParam();

            mParam.Merge(additional, new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Union,     // 배열 병합 방식
                MergeNullValueHandling = MergeNullValueHandling.Ignore // null 무시
            });

            return this;
        }

        public WebRequestAPI Add(string key, object value)
        {
            Param[key] = value is JToken token ? token : JToken.FromObject(value);

            return this;
        }

        public WebRequestAPI SetCallback(Action<bool, int, string, JObject> callback)
        {
            mCallback = callback;

            return this;
        }

        public bool Completed { get; protected set; } = false;

        public override bool keepWaiting
        {
            get { return !Completed; }
        }

        public int StatusCode { get; protected set; }
        //통신 성공실패 유무
        public bool TransportSuccess { get; protected set; } = false;
        //통신이 성공하고 에러가 없을시 
        public virtual bool RequestSuccess { get { return TransportSuccess && !IsError; } }
        public JObject Response { get; protected set; } = null;
        public JObject Result { get; protected set; } = null;

        public string ErrorCode { get; protected set; } = string.Empty;
        public bool IsError { get { return !string.IsNullOrEmpty(ErrorCode); } }

        public event Action<WebRequestAPI> OnRequestError;
        public static Action<WebRequestAPI> OnDefaultRequestErrorHandler;

        public event Action<WebRequestAPI> OnTransportSuccess;
        public static Action<WebRequestAPI> DefaultTransportSuccessHandler;

        public JObject GetLocalTestResponse(bool success = true)
        {
            JObject dict = new JObject();

            dict.Add("success", success);
            dict.Add("code", success ? 200 : 400);
            dict.Add("errorCode", string.Empty);
            dict.Add("result", _CreateLocalTestResult());

            return dict;
        }

        public virtual JObject _CreateLocalTestResult()
        {
            return new JObject();
        }

        public virtual void PostStart()
        {
            IncrementPostCount();

            Completed = false;
        }

        public virtual void PostComplete(JObject response)
        {
            DecrementPostCount();

            try
            {
                //응답 파싱
                ParseResponse(response);
            }
            catch (Exception ex)
            {
                // 파싱 실패 시, Network 실패로 간주
                TransportSuccess = false;
                StatusCode = -1;
                ErrorCode = "ParsingError";
                Result = null;
                Debug.LogError($"[RHWebRequestAPI] ParseResponse 실패: {ex}");
            }

            FinishPost();
        }

        private void ParseResponse(JObject response)
        {
            TransportSuccess = response.Get<bool>("success");
            StatusCode = response.Get<int>("code");
            ErrorCode = response.Get<string>("errorCode");
            Response = response;

            if (response.ContainsKey("timeStamp"))
            {
                long currentTime = response.Get<long>("timeStamp");
                Timef.timestamp = currentTime * 1000L;
            }

            if (response.ContainsKey("result"))
            {
                Result = response.Get<JObject>("result");
            }
        }

        private void IncrementPostCount()
        {
            if (!API_POST_COUNT_DICTIONARY.TryGetValue(API, out int count))
                API_POST_COUNT_DICTIONARY[API] = 1;
            else
                API_POST_COUNT_DICTIONARY[API] = count + 1;
        }

        private void DecrementPostCount()
        {
            if (!API_POST_COUNT_DICTIONARY.TryGetValue(API, out int count))
                return;

            if (count <= 1)
                API_POST_COUNT_DICTIONARY.Remove(API);
            else
                API_POST_COUNT_DICTIONARY[API] = count - 1;
        }

        private void FinishPost()
        {
            if (TransportSuccess)
                HandleTransportSuccess();

            if (!RequestSuccess)
                HandleRequestError();

            Completed = true;
            mCallback?.Invoke(TransportSuccess, StatusCode, ErrorCode, Result);
        }

        protected virtual void HandleTransportSuccess()
        {
            if (OnTransportSuccess != null)
                OnTransportSuccess.Invoke(this);
            else
                DefaultTransportSuccessHandler?.Invoke(this);
        }

        protected virtual void HandleRequestError()
        {
            if (OnRequestError != null)
                OnRequestError.Invoke(this);
            else if ( UseDefaultErrorHandling)
                OnDefaultRequestErrorHandler?.Invoke(this);
        }
    }
}