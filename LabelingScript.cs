using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.InferenceEngine;
using UnityEngine;

public class LabelingScript : MonoBehaviour
{
    [Header("Input files")]
    public ModelAsset modelAsset;
    public Model m_RuntimeModel;
    public TextAsset labelsMap;
    public TextAsset alreadyClassifiedObjects;
    [Header("Labels map delimiters (optional)")]
    public string delimiter_1;
    public string delimiter_2;
    [Header("Expected color range for model (optional)")]
    public int min;
    public int max;

    [Header("Additional options")]
    public Vector3 customRotationDegrees;
    public int numberOfRotationsOverwrite;
    public Color customBackgroundColor = Color.white;
    public bool getPrimaryColor = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        m_RuntimeModel = ModelLoader.Load(modelAsset);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
