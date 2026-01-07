using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class Level3
{
    public string detail;
    public Vector3 pos;
}

[System.Serializable]
public class Level2
{
    public string midName;
    public Level3 deep;
}

[System.Serializable]
public class Level1
{
    public string topName;
    public Level2 mid;
}

[CreateAssetMenu(fileName = "DeepStressSO", menuName = "StressTests/DeepStressSO")]
public class DeepStressSO : ScriptableObject
{
    public Level1 level1;
    public Color overtone;
}