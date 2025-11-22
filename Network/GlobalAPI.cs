using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Text;
using Newtonsoft.Json.Linq;

namespace jjevol.API
{
    public class GlobalAPI : WebRequestAPI
    {
        private static JObject _defaultParam;

        public static void SetDefaultParam(JObject param)
        {
            _defaultParam = param;
        }


        protected override JObject _CloneDefaultParam()
        {
            if (_defaultParam == null)
                return new JObject();

            return new JObject(_defaultParam);
        }
    }
}