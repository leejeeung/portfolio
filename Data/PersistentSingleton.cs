using UnityEngine;
using System.Collections;
using System.Dynamic;

namespace jjevol
{
    public abstract class PersistentSingleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        protected static T _instance = null;
        public static bool applicationIsQuitting = false;
        protected bool _enabled;

        public static T Instance
        {
            get
            {
                if (applicationIsQuitting)
                {
                    return null;
                }

                if (_instance == null)
                {

                    _instance = GameObject.FindObjectOfType(typeof(T)) as T;

                    if (_instance == null)
                    {

                        _instance = new GameObject().AddComponent<T>();
                        _instance.gameObject.name = _instance.GetType().FullName;
                    }
                }

                return _instance;

            }
        }


        public static bool HasInstance
        {
            get
            {
                return !IsDestroyed;
            }
        }

        public static bool IsDestroyed
        {
            get
            {
                if (_instance == null)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        protected virtual void Awake()
        {
            InitializeSingleton();
        }

        /// <summary>
        /// Initializes the singleton.
        /// </summary>
        protected virtual void InitializeSingleton()
        {
            if (!Application.isPlaying)
            {
                return;
            }


            if (_instance == null)
            {
                //If I am the first instance, make me the Singleton
                _instance = this as T;
                DontDestroyOnLoad(transform.gameObject);
                _enabled = true;
            }
            else
            {
                //If a Singleton already exists and you find
                //another reference in scene, destroy it!
                if (this != _instance)
                {
                    Destroy(this.gameObject);
                }
            }
        }

        protected virtual void OnDestroy()
        {
            _instance = null;
        }


        protected virtual void OnApplicationQuit()
        {
            _instance = null;
            applicationIsQuitting = true;
        }
    }
}