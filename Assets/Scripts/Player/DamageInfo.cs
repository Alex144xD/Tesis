using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public struct DamageInfo
{
    public float amount;
    public Transform source;   
    public Vector3 hitPoint;   
    public string tag;         

    public DamageInfo(float amount, Transform source, Vector3 hitPoint, string tag = null)
    {
        this.amount = amount;
        this.source = source;
        this.hitPoint = hitPoint;
        this.tag = tag;
    }
}