using UnityEngine;
using System.Collections;
using System;

namespace jjevol
{

    [Serializable()]
    public class Nullable
    {
        //Extend this class if you want to use the syntax
        //	if(myObject)
        //to check if it is not null
        public static implicit operator bool(Nullable o)
        {
            return (object)o != null;
        }
    }

}

