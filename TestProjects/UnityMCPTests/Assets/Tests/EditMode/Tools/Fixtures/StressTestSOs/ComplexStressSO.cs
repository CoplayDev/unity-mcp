using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public struct NestedData
{
    public string id;
    public float value;
    public Vector3 position;
}

[System.Serializable]
public class ComplexSubClass
{
    public string name;
    public int level;
    public List<float> scores;
}

public enum TestEnum
{
    Alpha,
    Beta,
    Gamma
}

[CreateAssetMenu(fileName = "ComplexStressSO", menuName = "StressTests/ComplexStressSO")]
public class ComplexStressSO : ScriptableObject
{
    [Header("Basic Types")]
    public int intValue;
    public float floatValue;
    public string stringValue;
    public bool boolValue;
    public Vector3 vectorValue;
    public Color colorValue;
    public TestEnum enumValue;

    [Header("Arrays & Lists")]
    public int[] intArray;
    public List<string> stringList;
    public Vector3[] vectorArray;

    [Header("Complex Types")]
    public NestedData nestedStruct;
    public ComplexSubClass nestedClass;
    public List<NestedData> nestedDataList;

    [Header("Extended Types (Phase 6)")]
    public AnimationCurve animCurve;
    public Quaternion rotation;
}