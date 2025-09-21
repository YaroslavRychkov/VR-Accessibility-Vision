using com.mukarillo.prominentcolor;
using GLTF.Schema;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Rendering;
using UnityEngine.Timeline;
using UnityEngine.UIElements;
using UnityEssentials.Extensions;
using static ScreenShotCameraWindow;
using static UnityEditor.Experimental.AssetDatabaseExperimental.AssetDatabaseCounters;
using static UnityEngine.Rendering.DebugUI;
using static UnityEngine.Rendering.DebugUI.Table;

public class ScreenShotCameraWindow : EditorWindow
{



    static int targetWidth;
    static int targetHeight;
    private static Worker worker;
    private static Tensor<float> inputTensor;
    private static Tensor<float> tempTensor;
    private static Tensor<float> outputTensor;
    private static byte[] bytes;
    private static string filename;
    private static List<LabeledObject> labeledObjects = new List<LabeledObject>();
    private static int numOfRotations = 16;
    private static int numOfPreviewPictures = 3;
    private static TextAsset alreadyClassifiedObjects;
    private static ModelAsset modelAsset;

    // 
    [MenuItem("Tools/Add Labels to Game Objects")]
    private static bool GenerateLabels(MenuCommand menuCommand)
    {
        try
        {

#if UNITY_EDITOR
            var watch = System.Diagnostics.Stopwatch.StartNew();
            // Store GameObjects
            GameObject[] objects = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            LabelingScript labelingScript = GameObject.Find("LabelingScript").GetComponent<LabelingScript>();
            labeledObjects.Clear();
            alreadyClassifiedObjects = labelingScript.alreadyClassifiedObjects;

            string[] objectIdsToIgnore = null;
            if (alreadyClassifiedObjects) { 
            objectIdsToIgnore = alreadyClassifiedObjects.text.Split(",");
            }
            

            //Always split by newline; split code from label by default using " " character, optional: change delimiter and second delimiter
            var labels = labelingScript.labelsMap.text.Split("\n");
            for (int i = 0; i < labels.Length; i++)
            {
                if (!string.IsNullOrEmpty(labels[i]))
                {
                    string delimiter1;
                    if (labelingScript.delimiter_1 != "")
                    {
                        delimiter1 = labelingScript.delimiter_1;
                    }
                    else
                    {
                        delimiter1 = " ";
                    }
                    var indexOfDelimiter1 = labels[i].IndexOf(delimiter1);
                    labels[i] = (indexOfDelimiter1 >= 0) ? labels[i].Substring(indexOfDelimiter1 + 1) : labels[i];

                    if (labelingScript.delimiter_2 != "")
                    {
                        var indexOfDelimiter2 = labels[i].IndexOf(labelingScript.delimiter_2);
                        labels[i] = (indexOfDelimiter2 >= 0) ? labels[i].Remove(indexOfDelimiter2) : labels[i];
                    }
                    labels[i] = labels[i].Trim();
                }
            }
            //load model 
            modelAsset = labelingScript.modelAsset; 
            Model model = ModelLoader.Load(modelAsset);
            worker = new Worker(model, BackendType.GPUCompute);
            List<Model.Input> inputList = model.inputs;
            DynamicTensorShape inputShape;
            if (inputList.Count == 1)
            {
                inputShape = inputList[0].shape;
            }
            else
            {
                Debug.LogError("more than 1 possible input shape");
                return false;
            }

            //currently does not allow batchsizes != 1; code left intact for potential customization
            int batchsitze;
            if (inputShape.Get(0) == -1)
            {
                batchsitze = 1;
            }
            else
            {
                batchsitze = inputShape.Get(0);
            }


            int channels = inputShape.Get(1);
            if (channels == -1)
            {
                channels = 3;
            }
            targetHeight = inputShape.Get(2);
            targetWidth = inputShape.Get(3);

            //default to 224x224 if no width and height were specified by model
            if (targetHeight == -1 || targetWidth == -1)
            {
                targetHeight = 224;
                targetWidth = 224;
            }
            //Debug.Log(inputShape.ToString());


            if (labelingScript.numberOfRotationsOverwrite != 0)
            {
                numOfRotations = labelingScript.numberOfRotationsOverwrite;
            }

            //A layer with the tag "Screenshot Layer" must exist in scene
            int screenshotLayer = LayerMask.NameToLayer("Screenshot Layer");
            if (screenshotLayer == -1)
            {
                Debug.LogError("Could not find layer. Make sure a layer with the tag 'Screenshot Layer' exists");
            }

            // remember visible layers and cullingmask
            int oldVisibileLayers = Tools.visibleLayers;
            Tools.visibleLayers = 1000;


            SceneView sceneView = SceneView.lastActiveSceneView;
            sceneView.orthographic = false;

            LayerMask oldLayerMask = sceneView.camera.cullingMask;

            sceneView.camera.cullingMask = LayerMask.GetMask("Screenshot Layer");

            // remember camera position, color and settings
            Vector3 oldCameraPosition = sceneView.camera.transform.position;
            Quaternion oldRotation = sceneView.rotation;
            Vector3 oldPivot = sceneView.pivot;

            CameraClearFlags oldClearFlags = sceneView.camera.clearFlags;
            sceneView.camera.clearFlags = CameraClearFlags.SolidColor;
            UnityEngine.Color oldBackgroundColor = sceneView.camera.backgroundColor;

            UnityEngine.Color backGroundColor = labelingScript.customBackgroundColor;
            sceneView.camera.backgroundColor = backGroundColor;


            int cameraWidth = sceneView.camera.pixelWidth;
            int cameraHeight = sceneView.camera.pixelHeight;

            Texture2D captureTexture = new Texture2D(cameraWidth, cameraHeight, TextureFormat.RGB24, false);
            Texture2D[] captureTextures = new Texture2D[numOfPreviewPictures];
            Texture2D resultTexture;
            Renderer renderer;
            Collider collider;
            Rigidbody rigidBody;
            // Iterate through each object
            int counter = 0;
            foreach (GameObject obj in objects)
            {   // Check if object exists
                if (obj != null)

                {
                    // Check if object has an active Collider and a Renderer script attached
                    collider = obj.GetComponent<Collider>();
                    renderer = obj.GetComponent<Renderer>();
                    rigidBody = obj.GetComponent<Rigidbody>();

                    if (collider != null && collider.enabled == true)
                    {
                        //also check for Renderer in child objects
                        Component[] childRenderer = obj.GetComponentsInChildren<Renderer>();
                        if (renderer != null || childRenderer != null)
                        {
                            //if csv file with previously classified objects exists, remove those objects from our objects array 
                            if (objectIdsToIgnore == null || !objectIdsToIgnore.Contains(GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString())) {
                            counter = counter + 1;

                            int oldLayer = obj.layer;
                            SetGameLayerRecursive(obj, screenshotLayer);

                                //comment in to reset layer for all objects back to default layer 0
                                //int oldLayer = 0;

                                obj.layer = screenshotLayer;

                            UnityEditor.SceneView.RepaintAll();

                            AccessibilityTags.AccessibilityTags accessibilityTagsScript = obj.GetComponent<AccessibilityTags.AccessibilityTags>();
                            // If no accessibility tags script on object, add script to object
                            if (accessibilityTagsScript == null)
                            {
                                accessibilityTagsScript = Undo.AddComponent<AccessibilityTags.AccessibilityTags>(obj);
                            }


                            //reset values from previous loop. 
                            // resultList holds all classifications per object, while valueList holds the max value for each classification of the object
                            var resultList = new List<string>();
                            var valueList = new List<(float, string)>();
                            var colorName = "";

                            //calculate distance from camera necassary so max bound of object is within camera frame
                            Vector3 objectSizes = collider.bounds.max - collider.bounds.min;
                            float maximumObjectMeasurement = Mathf.Max(objectSizes.x, objectSizes.y, objectSizes.z);
                            float tan = 2.0f * Mathf.Tan(0.5f * Mathf.Deg2Rad * sceneView.camera.fieldOfView);
                            float distance = maximumObjectMeasurement / tan; // Combined wanted distance from the object
                            distance += maximumObjectMeasurement * 0.5f; //adding half the objects bounds to avoid clipping issues
                            sceneView.camera.transform.forward = obj.transform.forward;

                            Quaternion rotation = Quaternion.identity;

                                //Rotate the camera around the object and classify each rotation.
                                //Defaults to 16 rotations/classifications per object around the y axis if no custom vector and number of rotations was supplied. 
                                for (int rot = 1; rot < 1 + numOfRotations; rot++)
                                {
                                    if (labelingScript.customRotationDegrees != new Vector3(0, 0, 0))
                                    {
                                        Vector3 customRotation = labelingScript.customRotationDegrees;
                                        rotation = Quaternion.AngleAxis(rot * customRotation.x, Vector3.forward) * Quaternion.AngleAxis(rot * customRotation.y, Vector3.up) * Quaternion.AngleAxis(rot * customRotation.z, Vector3.right);
                                    }
                                    else
                                    {
                                        rotation = Quaternion.AngleAxis(rot * 22.5f, Vector3.up);
                                    }

                                    //only y axis rotation
                                    sceneView.camera.transform.rotation = rotation;

                                    
                                    sceneView.camera.transform.position = collider.bounds.center - (distance) * sceneView.camera.transform.forward;

                                    //sceneview camera resets to its old position once per update, setting new position is only possible through setting the sceneviews rotation and pivot point
                                    //sometimes, the camera does not reset its position for unknown reasons, so the manual reset to the old position is left in the code
                                    sceneView.rotation = rotation;
                                    sceneView.pivot = sceneView.camera.transform.position + rotation * new Vector3(0, 0, sceneView.cameraDistance);

                                    sceneView.camera.Render();

                                    RenderTexture.active = sceneView.camera.targetTexture;


                                    captureTexture.ReadPixels(new Rect(0, 0, cameraWidth, cameraHeight), 0, 0);
                                    captureTexture.Apply();

                                    //most classification algorithms expect a crop from the middle of the target picture 
                                    resultTexture = ResampleAndCrop(captureTexture, targetWidth, targetHeight);

                                    //the first (up to) 3 captures per object are saved to be used as preview pictures in the confirmation step
                                    if (rot == 1 || rot == 2 || rot == 3)
                                    {
                                        captureTextures[rot - 1] = copyTextureAsNewObject(resultTexture);
                                    }



                                    // Normalize the input tensor if needed (default: min 0, max 1) by converting it to an array

                                    if (labelingScript.min != 0 || labelingScript.max != 1)
                                    {
                                        tempTensor = new Tensor<float>(new TensorShape(batchsitze, channels, resultTexture.height, resultTexture.width));
                                        TextureConverter.ToTensor(resultTexture, tempTensor, new TextureTransform().SetTensorLayout(TensorLayout.NCHW));
                                        var inputArray = tempTensor.DownloadToArray();

                                        for (int i = 0; i < inputArray.Length; i++)
                                        {
                                            inputArray[i] = ((labelingScript.max - labelingScript.min) * inputArray[i]) - labelingScript.min;
                                        }
                                        inputTensor = new Tensor<float>(tempTensor.shape, inputArray);
                                    }
                                    else {

                                        inputTensor = new Tensor<float>(new TensorShape(batchsitze, channels, resultTexture.height, resultTexture.width));
                                        TextureConverter.ToTensor(resultTexture, inputTensor, new TextureTransform().SetTensorLayout(TensorLayout.NCHW));
                                    }


                                //classification step
                                worker.Schedule(inputTensor);
                                Tensor<float> outputTensor = worker.PeekOutput() as Tensor<float>;
                                var tensorResult = outputTensor.DownloadToArray();

                                float maxValue = tensorResult.Max();

                                int indexOfMaxValue = System.Array.FindIndex(tensorResult, x => x == maxValue);

                                //not a confidence level, only value -> not comparable between different models
                                string resultName = labels[indexOfMaxValue];

                                resultList.Add(resultName);
                                valueList.Add((maxValue, resultName));

                                //get dominant color in image only once
                                if (labelingScript.getPrimaryColor && rot == 1)
                                {
                                    Texture2D newTexture2DInARGB32 = new Texture2D(resultTexture.width, resultTexture.height, TextureFormat.ARGB32, false);
                                    newTexture2DInARGB32.SetPixels(resultTexture.GetPixels());
                                    newTexture2DInARGB32.Apply();

                                    Texture2D textureBoarderRemoved = ProminentColor.RemoveBorder(newTexture2DInARGB32, backGroundColor, 0.001f);


                                    List<Color32> dominantColor = ProminentColor.GetColors32FromImage(textureBoarderRemoved, 1, 0.01f, 10, 0.1f);
                                    colorName = " ";
                                    if (dominantColor != null)
                                    {
                                        colorName = ExtColorToNames.FindColor(dominantColor[0])+" ";
                                    }
                                    else
                                    {
                                        Debug.LogWarning("Color not found for " + obj.name);
                                    }
                                    //comment in to save all captures as pngs while running the classification - used as sanity check
                                    //    bytes = resultTexture.EncodeToPNG();
                                    //    filename = Application.dataPath + "/VisionScene/Screenshots/" + counter + "_rotation_" + rot + "_classified_as_" + colorName + "_" + resultName + ".png";
                                    //    File.WriteAllBytes(filename, bytes);

                                }

                               
                                inputTensor?.Dispose();
                                tempTensor?.Dispose();
                                outputTensor?.Dispose();
                                    // Check if object has a Rigidbody script attached for interactibility
                                    // If isKinematic is false, object can be picked up/manipulated.
                                    // Interactable value is currently unused by the VR Accessibility SDK implementation of Text to speech, but is used floating alt text menu
                                    if (rigidBody != null && rigidBody.isKinematic == false) 
                                {
                                    accessibilityTagsScript.Interactable = true;
                                }
                                else
                                {
                                    accessibilityTagsScript.Interactable = false;
                                }


                            }

                            //information about most common result, as well as which result scored the highest classification score for each object are extracted from resultList and valueList
                            var mostHelper = (from i in resultList
                                        group i by i into grp
                                        orderby grp.Count() descending
                                        select grp.Key).First();
                            var mostCommonClassification = colorName + mostHelper;
                            int mostCommonCount = resultList.Where(x => x.Equals(mostHelper)).Count();

                            var tupleWithMaxItem1 = valueList.OrderByDescending(x => x.Item1).First();
                            var highestValueClassification = colorName + tupleWithMaxItem1.Item2;

                                bytes = captureTexture.EncodeToPNG();
                                filename = Application.dataPath + "/VisionScene/Screenshots/" + counter + "mostCommonClassification" + "_classified_as_" + mostCommonClassification + ".png";
                                File.WriteAllBytes(filename, bytes);

                                filename = Application.dataPath + "/VisionScene/Screenshots/" + counter + "highestValueClassification" + "_classified_as_" + highestValueClassification + ".png";
                                File.WriteAllBytes(filename, bytes);

                                //create a new LabeledObject and add it to the list to be used in the GUI confirmation window
                                labeledObjects.Add(new LabeledObject(obj, captureTextures, mostCommonClassification, mostCommonCount, highestValueClassification));

                            //return object to old layer
                            SetGameLayerRecursive(obj, oldLayer);
                            UnityEditor.AssetDatabase.Refresh();
                        }

                    }
                    }


                    // Mark selected GameObject as dirty to save changes
                    EditorUtility.SetDirty(obj);
                }
            }
            //undo camera changes
            sceneView.camera.transform.position = oldCameraPosition;
            sceneView.pivot = oldPivot;
            sceneView.rotation = oldRotation;
            //undo layer visibility changes (comment out for sanity check)
            Tools.visibleLayers = oldVisibileLayers;
            sceneView.camera.cullingMask = oldLayerMask;
            sceneView.camera.clearFlags = oldClearFlags;
            sceneView.camera.backgroundColor = oldBackgroundColor;
            //Dispose
            worker.Dispose();

            //Open GUI confirmation window
            ScreenShotCameraWindow window = GetWindow<ScreenShotCameraWindow>("Confirm Labels");
            window.Show();  

            // Mark scene dirty to save changes to the scene
            UnityEditor.SceneView.RepaintAll();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            //Log how long the classification took
            watch.Stop();
            var elapsedMs = watch.ElapsedMilliseconds;
            Debug.Log((float)elapsedMs / 1000);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError(ex.ToString());
            worker?.Dispose();
            inputTensor?.Dispose();
            tempTensor?.Dispose();
            outputTensor?.Dispose();
            return false;
        }
    }
#endif

    bool useMostCommonLabel = false;
    bool useHighestValueLabel = false;
    bool useCustomText = false;
    int currentObjectNumber = 0;
    private Texture2D[] currentPreviewTextures;
    private String mostCommonLabel = "";
    private int mostCommonLabelCount = 0;
    private String highestValueLabel = "";
    private String customText = "";
    LabeledObject currentObj;
    HashSet<int> revisionHashSet = new HashSet<int>();
    bool introScreen = true;
    bool revisionMode = false;
    Vector2 scrollPosition;
    List<string> vowels = new List<string>() { "A", "E", "I", "O", "U"};
    List<GlobalObjectId> globalObjectIds = new List<GlobalObjectId>();

    private void OnEnable()
    {
        if (currentObjectNumber < labeledObjects.Count)
        {
            currentObj = labeledObjects[0];
            mostCommonLabel = currentObj.mostCommonLabel;
            mostCommonLabelCount = currentObj.mostCommonCount;
            highestValueLabel = currentObj.highestValueLabel;
            customText = mostCommonLabel;

            currentPreviewTextures = new Texture2D[currentObj.previewTextures.Length];
            for (int i = 0; i < labeledObjects[currentObjectNumber].previewTextures.Length; i++)
            {
                currentPreviewTextures[i] = copyTextureAsNewObject(labeledObjects[currentObjectNumber].previewTextures[i]);
            }
        }


    }
    private void OnGUI()
    {
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("Confirm labels", EditorStyles.boldLabel);
            //Show introduction screen first when window is opened
            if (introScreen)
            {
                GUILayout.Label("Select a label or write your own by selecting the checkbox next to it and clicking confirm.", EditorStyles.label);
                GUILayout.Label(String.Format("Total number of objects found:  {0}", labeledObjects.Count), EditorStyles.label);

                if (GUILayout.Button("Start now"))
                {
                    introScreen = false;
                }
            }
          
            //confirming classifications for each object
            else if (currentObjectNumber < labeledObjects.Count)
            {
                GUILayout.Label(String.Format("Object {0} out of {1}", currentObjectNumber + 1, labeledObjects.Count), EditorStyles.label);


                EditorGUILayout.PrefixLabel("Preview:");
                GUILayout.BeginHorizontal();
                foreach (Texture2D tex in currentPreviewTextures)
                {
                    GUILayout.Box(tex, GUILayout.Width(targetWidth), GUILayout.Height(targetHeight));
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                useMostCommonLabel = GUILayout.Toggle(useMostCommonLabel, String.Format("{0} ({1} out of {2} classifications)", mostCommonLabel, mostCommonLabelCount, numOfRotations));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                useHighestValueLabel = GUILayout.Toggle(useHighestValueLabel, String.Format("{0} (highest score)", highestValueLabel));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                useCustomText = EditorGUILayout.Toggle(useCustomText);
                customText = EditorGUILayout.TextField(customText);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                //if revisionMode is active, the user can choose between confirming, skipping the object and returning to revision list.
                //When an object in revisionMode is confirmed or skipped, it is removed from the hashmap containing all objects that were saved for later
                if (revisionMode) {
                    if (GUILayout.Button("Return to Revision List"))
                    {
                        currentObjectNumber = labeledObjects.Count;
                    }
                    if (GUILayout.Button("Skip"))
                    {
                        revisionHashSet.Remove(currentObjectNumber);
                        currentObjectNumber = labeledObjects.Count;
                    }
                    if (GUILayout.Button("Confirm"))
                    {
                        //if exactly one label was selected, add the selected label to the objects accessibility tags
                        if (ExactlyOneChosen(useMostCommonLabel, useHighestValueLabel, useCustomText))
                        {
                            AccessibilityTags.AccessibilityTags accessibilityTags = currentObj.gameObject.GetComponent<AccessibilityTags.AccessibilityTags>();
                            string altTextLabel = "";
                            if (useMostCommonLabel == true)
                            {
                                altTextLabel = mostCommonLabel;
                            }
                            else if (useHighestValueLabel == true)
                            {
                                altTextLabel = highestValueLabel;
                            }
                            else if (useCustomText == true)
                            {
                                altTextLabel = customText;
                            }
                            //Formats alttext label - if the alt text starts with a vowel, use the article "an", otherwise "a"
                            //imperfect solution, but loading full dictionary for correct article doesnt seem feasible
                            if (!string.IsNullOrEmpty(altTextLabel)) {
                                string article = vowels.Contains(altTextLabel.Substring(0, 1).ToUpper()) ? "an" : "a";
                                accessibilityTags.AltText = String.Format("This is {0} {1}.", article, altTextLabel);
                            } else
                            {
                                accessibilityTags.AltText = "";
                            }
                                EditorUtility.SetDirty(currentObj.gameObject);
                            EditorUtility.SetDirty(accessibilityTags);

                                revisionHashSet.Remove(currentObjectNumber);
                                currentObjectNumber = labeledObjects.Count;
                        }
                        else
                        {
                            GUILayout.Label("Choose exactly 1 of the options");
                            Debug.LogWarning("Choose exactly 1 of the options");
                        }
                   }
                }
                //if revisionMode is not active, navigate through all classified objects. In this mode, users can go back and forth between objects freely.
                else { 
                if (GUILayout.Button("Skip"))
                {
                    goToNextObject();
                }
                if (GUILayout.Button("Save for Later"))
                {
                        revisionHashSet.Add(currentObjectNumber);
                        goToNextObject();
                }

                if (GUILayout.Button("Confirm"))
                {
                    //if exactly one label was selected, add the selected label to the objects accessibility tags
                    if (ExactlyOneChosen(useMostCommonLabel, useHighestValueLabel, useCustomText))
                    {
                            AccessibilityTags.AccessibilityTags accessibilityTags = currentObj.gameObject.GetComponent<AccessibilityTags.AccessibilityTags>();
                            string altTextLabel = "";
                            if (useMostCommonLabel == true)
                        {
                                altTextLabel = mostCommonLabel;
                        }
                        else if (useHighestValueLabel == true)
                        {
                                altTextLabel = highestValueLabel;
                        }
                        else if (useCustomText == true)
                        {
                                altTextLabel = customText;
                        }
                            //Formats alttext label 
                            //imperfect solution, but loading full dictionary for correct article doesnt seem feasible
                            if (!string.IsNullOrEmpty(altTextLabel))
                            {
                                string article = vowels.Contains(altTextLabel.Substring(0, 1).ToUpper()) ? "an" : "a";
                                accessibilityTags.AltText = String.Format("This is {0} {1}.", article, altTextLabel);
                            }
                                else
                            {
                                accessibilityTags.AltText = "";
                            }
                            EditorUtility.SetDirty(currentObj.gameObject);
                            EditorUtility.SetDirty(accessibilityTags);

                            goToNextObject();
                    }
                    else
                    {
                        GUILayout.Label("Choose exactly 1 of the options");
                        Debug.LogWarning("Choose exactly 1 of the options");
                    }
                }
                else if (currentObjectNumber > 0)
                {
                    if (GUILayout.Button("Back"))
                    {
                        goToPreviousObject();
                    }
                }
            }
            }
            else
            {
                //after going through all objects, if any were saved for later, enter revision mode.
                //This disables normal navigation. The normal navigation can be entered again from the revision screen by clicking the "back" button. 
                if (revisionHashSet.Count > 0)
                {
                    revisionMode = true;
                    foreach (int key in revisionHashSet) {
                        for (int i = 0; i < labeledObjects[key].previewTextures.Length; i++)
                        {
                            currentPreviewTextures[i] = copyTextureAsNewObject(labeledObjects[key].previewTextures[i]);
                        }
                        //Debug.Log("set preview Texture to " + key);
                        GUILayout.BeginHorizontal();
                        foreach (Texture2D tex in currentPreviewTextures) { 
                            GUILayout.Box(tex, GUILayout.Width(targetWidth), GUILayout.Height(targetHeight));
                        }
                        GUILayout.EndHorizontal(); 
                        
                        if (GUILayout.Button("Revisit " + key)) {
                            goToObjectByKey(key);
                            break;
                        }
                    }

                }
                else
                {
                    GUILayout.Label("Done!");

                }
                if (GUILayout.Button("Back"))
                {
                    goToPreviousObject();
                    revisionMode = false;

                }

                if (GUILayout.Button("Save classified objects list to file"))
                {
                    
                    //save the GlobalObjectId of every classified object that is not currently marked for revision
                    for (int i = 0; i < labeledObjects.Count; i++)
                    {
                        if (!revisionHashSet.Contains(i))
                        {
                            globalObjectIds.Add(GlobalObjectId.GetGlobalObjectIdSlow(labeledObjects[i].gameObject));
                        }
                    }

                    StringBuilder sb = new StringBuilder();

                    if (globalObjectIds.Count > 0) { 
                        sb.Append(globalObjectIds.First().ToString());
                        foreach (GlobalObjectId id in globalObjectIds.Skip(1))
                            {
                                sb.Append("," + id.ToString());
                            }
                    }

                    string savePath = "";
                    string modelFileName = Path.GetFileName(AssetDatabase.GetAssetPath(modelAsset));

                    //if a csv file previously added to this scripts input, add its contents to the new file
                    //the new files name consists of the old files name and the name of the classification model used in this classification
                    if (alreadyClassifiedObjects != null)
                    {

                        string prevPath = AssetDatabase.GetAssetPath(alreadyClassifiedObjects);
                        savePath = prevPath.Substring(0, prevPath.Length - 4);
                        savePath += "_and_" + modelFileName + ".csv";
                        sb.Append("," + alreadyClassifiedObjects.text);
                    }
                    //otherwise, create a new file with information about the current scene and classification model used contained in its filename
                    else
                    {
                        savePath = Application.dataPath + "/" + EditorSceneManager.GetActiveScene().name + "_classified_with_" + modelFileName + ".csv";

                    }
                    // Create a file to write to.
                    Debug.Log(sb.ToString());
                    Debug.Log(savePath.ToString());
                    using (StreamWriter sw = File.CreateText(savePath))
                    {
                        sw.Write(sb.ToString());
                    }
                    this.Close();


            }
            //All changes to the objects persist if this option is chosen. No file with information which objects were classified is created.
            if (GUILayout.Button("Close without saving csv file"))
                {
                    this.Close();
            }
        }
            GUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
    }


    private void goToNextObject() {
        currentObjectNumber++;
    if (currentObjectNumber < labeledObjects.Count)
    {

            mostCommonLabel = labeledObjects[currentObjectNumber].mostCommonLabel;
            mostCommonLabelCount = labeledObjects[currentObjectNumber].mostCommonCount;
            highestValueLabel = labeledObjects[currentObjectNumber].highestValueLabel;
            for (int i = 0; i < labeledObjects[currentObjectNumber].previewTextures.Length; i++)
            {
                currentPreviewTextures[i] = copyTextureAsNewObject(labeledObjects[currentObjectNumber].previewTextures[i]);
            }

            customText = mostCommonLabel;
        currentObj = labeledObjects[currentObjectNumber];
    }
    }

    private void goToPreviousObject()
    {
        currentObjectNumber--;


        mostCommonLabel = labeledObjects[currentObjectNumber].mostCommonLabel;
        mostCommonLabelCount = labeledObjects[currentObjectNumber].mostCommonCount;
        highestValueLabel = labeledObjects[currentObjectNumber].highestValueLabel;
        for (int i = 0; i < labeledObjects[currentObjectNumber].previewTextures.Length; i++)
        {
            currentPreviewTextures[i] = copyTextureAsNewObject(labeledObjects[currentObjectNumber].previewTextures[i]);
        }
        customText = mostCommonLabel;
        currentObj = labeledObjects[currentObjectNumber];
    }

    private void goToObjectByKey(int key)
    {
        currentObjectNumber = key;
        mostCommonLabel = labeledObjects[currentObjectNumber].mostCommonLabel;
        mostCommonLabelCount = labeledObjects[currentObjectNumber].mostCommonCount;
        highestValueLabel = labeledObjects[currentObjectNumber].highestValueLabel;
        for (int i = 0; i < labeledObjects[currentObjectNumber].previewTextures.Length; i++)
        {
            currentPreviewTextures[i] = copyTextureAsNewObject(labeledObjects[currentObjectNumber].previewTextures[i]);
        }
        customText = mostCommonLabel;
        currentObj = labeledObjects[currentObjectNumber];
        //Debug.Log("set new key: " + key);
    }

    private void OnDisable()
    {
        UnityEditor.SceneView.RepaintAll();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    //returns true if exactly one of 3 booleans is true
    private bool ExactlyOneChosen(bool a, bool b, bool c)
    {
        bool found = false;
        bool alreadyFound = false;
        bool[] bools = { a, b, c };
        foreach (bool boolean in bools)
        {
            if (boolean)
            {
                found = true;
                if (alreadyFound)
                {
                    found = false;
                    break;
                }
                else
                {
                    alreadyFound = true;
                }
            }
        }
        return found;
    }

    public class LabeledObject
    {
        public GameObject gameObject;
        public Texture2D[] previewTextures;
        public String mostCommonLabel;
        public String highestValueLabel;
        public int mostCommonCount;

        public LabeledObject(GameObject gameObject, Texture2D[] previewTextures, String mostCommonLabel, int mostCommonCount, String highestValueLabel)
        {
            this.gameObject = gameObject;
            this.previewTextures = new Texture2D[previewTextures.Length];
            for (int i = 0; i < previewTextures.Length; i++) {
                this.previewTextures[i] = copyTextureAsNewObject(previewTextures[i]);
            }

            this.mostCommonLabel = mostCommonLabel;
            this.highestValueLabel = highestValueLabel;
            this.mostCommonCount = mostCommonCount;
        }




        public override string ToString() => $"({gameObject.name}, {mostCommonLabel}, {highestValueLabel})";
    }


    private static Texture2D copyTextureAsNewObject(Texture2D tex)
    {
        Texture2D resultTex = new Texture2D(tex.width, tex.height);
        resultTex.SetPixels(tex.GetPixels());
        resultTex.Apply();
        return resultTex; 
    }


    private static void SetGameLayerRecursive(GameObject gameObject, int layer)
    {
        gameObject.layer = layer;
        foreach (Transform child in gameObject.transform)
        {
            SetGameLayerRecursive(child.gameObject, layer);
        }
    }

    public static Texture2D ResampleAndCrop(Texture2D source, int targetWidth, int targetHeight)
    {
        int sourceWidth = source.width;
        int sourceHeight = source.height;
        float sourceAspect = (float)sourceWidth / sourceHeight;
        float targetAspect = (float)targetWidth / targetHeight;
        int xOffset = 0;
        int yOffset = 0;
        float factor = 1;
        if (sourceAspect > targetAspect)
        { // crop width
            factor = (float)targetHeight / sourceHeight;
            xOffset = (int)((sourceWidth - sourceHeight * targetAspect) * 0.5f);
        }
        else
        { // crop height
            factor = (float)targetWidth / sourceWidth;
            yOffset = (int)((sourceHeight - sourceWidth / targetAspect) * 0.5f);
        }
        Color32[] data = source.GetPixels32();
        Color32[] data2 = new Color32[targetWidth * targetHeight];
        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                var p = new Vector2(Mathf.Clamp(xOffset + x / factor, 0, sourceWidth - 1), Mathf.Clamp(yOffset + y / factor, 0, sourceHeight - 1));
                // bilinear filtering
                var c11 = data[Mathf.FloorToInt(p.x) + sourceWidth * (Mathf.FloorToInt(p.y))];
                var c12 = data[Mathf.FloorToInt(p.x) + sourceWidth * (Mathf.CeilToInt(p.y))];
                var c21 = data[Mathf.CeilToInt(p.x) + sourceWidth * (Mathf.FloorToInt(p.y))];
                var c22 = data[Mathf.CeilToInt(p.x) + sourceWidth * (Mathf.CeilToInt(p.y))];
                var f = new Vector2(Mathf.Repeat(p.x, 1f), Mathf.Repeat(p.y, 1f));
                data2[x + y * targetWidth] = UnityEngine.Color.Lerp(UnityEngine.Color.Lerp(c11, c12, p.y), UnityEngine.Color.Lerp(c21, c22, p.y), p.x);
            }
        }

        var tex = new Texture2D(targetWidth, targetHeight);
        tex.SetPixels32(data2);
        tex.Apply(true);
        return tex;
    }


}