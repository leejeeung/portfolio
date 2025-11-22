using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IKeyReferenceCounter 
{
    public bool IsContain(string key);

    public bool IsEnable();

    public void Disable(string name);

    public void Enable(string name);
}
