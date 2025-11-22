using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class KeyReferenceCounter : IKeyReferenceCounter
{
    private Dictionary<string, int> keyRefCountDict = new Dictionary<string, int>();

    public bool IsContain(string key)
    {
        return keyRefCountDict.ContainsKey(key) && keyRefCountDict[key] > 0;
    }

    public bool IsEnable()
    {
        return keyRefCountDict.Count > 0;
    }

    public void Disable(string name)
    {
        if (keyRefCountDict.ContainsKey(name))
        {
            keyRefCountDict[name]--;

            if (keyRefCountDict[name] <= 0)
                keyRefCountDict.Remove(name);
        }
    }

    public void Clear()
    {
        keyRefCountDict.Clear();
    }

    public void Enable(string name)
    {
        if (keyRefCountDict.ContainsKey(name))
        {
            keyRefCountDict[name]++;
        }
        else
        {
            keyRefCountDict.Add(name, 1);
        }
    }
}

