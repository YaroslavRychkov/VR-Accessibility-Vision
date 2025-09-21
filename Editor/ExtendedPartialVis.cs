using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Meta.WitAi.TTS.Utilities;
using TMPro;
using AccessibilityTags;




public class PartialVis : MonoBehaviour
{   
    public Transform raycastOrigin;
    public InputAction button;

    public TextMeshProUGUI interactable;
    public TextMeshProUGUI details;
    public TextMeshProUGUI obj_name;

    [SerializeField] Color32 trueColor;
    [SerializeField] Color32 falseColor;

    [Header("Text-to-Speech")]
    [SerializeField] private bool ttsEnabled = true, speakAfterMenuClose = false;
    [SerializeField] private TTSSpeaker ttsSpeaker;
    private Assets.VisionReader visionReader;

    AccessibilityTags.AccessibilityTags tags;
    string objectName;
    Camera m_MainCamera;

    private void OnEnable()
    {
        button.Enable();
    }

    private void OnDisable()
    {
        button.Disable();
    }
    
    // Start is called before the first frame update
    void Start()
    {
        trueColor = Color.green;
        falseColor = Color.red;
        m_MainCamera = Camera.main;
        visionReader = new(ttsSpeaker);
    }

    // Update is called once per frame
    void Update()
    {
        if(button.triggered){
            Debug.Log("Activated!");
            Scan();
        }
    }

    

    void Scan(){
        //Raycast send
        RaycastHit hit;
        Ray ray = new Ray(raycastOrigin.position, raycastOrigin.forward);
        
        
        //If the raycast hits
        if(Physics.Raycast(ray, out hit, 100)) // Ray hit something
        {
            //Debug.Log(hit.collider.tag);
            //Debug.Log(hit.collider.gameObject);
            //Debug.Log(hit.collider.name);

            //Set the interactable field appropriately
            if(hit.collider.tag == "Interactable"){
                interactable.text = "True";
                interactable.color = trueColor;
            } else{
                interactable.text = "False";
                interactable.color = falseColor;
            }


            //If the object hit has an accessibility script
            if(hit.collider.gameObject.GetComponent<AccessibilityTags.AccessibilityTags>() != null)
            {
                tags = hit.collider.gameObject.GetComponent<AccessibilityTags.AccessibilityTags>();

                //get angle between ray and camera forward
                Vector3 targetDir = raycastOrigin.forward;
                Vector3 forward = m_MainCamera.transform.forward;
                float angle = Vector3.SignedAngle(targetDir, forward, Vector3.up);
                Debug.Log(angle);
                int clockDirection = (int)((Math.Max((angle + 180.0),0.00001) / 360 * 12) + 6) % 12;
                Debug.Log(clockDirection);

                objectName = hit.collider.gameObject.name;
                Debug.Log("This is a " + objectName);

                details.text = tags.AltText + "\n";
                obj_name.text = objectName;
                interactable.text = tags.Interactable.ToString();

                //combine text with directional information
                String altText = tags.AltText;
                int endIndex = altText.LastIndexOf("");

                String altTextWithDirection = altText.Substring(0, endIndex-1) + " at " + clockDirection + " o'clock.";

                if (ttsEnabled)
                {
                    //visionReader.StartReading(tags.AltText);
                    visionReader.StartReading(altTextWithDirection);
                }
            } 
            else if (hit.collider.gameObject.GetComponent<UnityEngine.Object>() != null)
            {
                objectName = hit.collider.gameObject.name;
                Debug.Log("This is a "+objectName);

                Component[] components = hit.collider.gameObject.GetComponents<Component>();
                foreach (Component component in components)
                {
                    Type type = component.GetType();
                    PropertyInfo descriptionProperty = type.GetProperty("description");

                    if (descriptionProperty != null)
                    {
                        string description = (string)descriptionProperty.GetValue(component, null);
                        details.text = description + "\n";
                        break;
                    }
                }

                obj_name.text = objectName;
            } 
            else 
            {
                details.text = "None";
                obj_name.text = "None";
            }

            // //If the object hit has an object script
            // if(hit.collider.gameObject.GetComponent<Object>() != null){
            //     objectInfo = hit.collider.gameObject.GetComponent<Object>();
            //     Debug.Log("This is a "+objectInfo.objectName);

            //     details.text = objectInfo.description + "\n";
            //     obj_name.text = objectInfo.objectName;
            // } 
            // else {
            //     details.text = "None";
            //     obj_name.text = "None";
            // }
        }
    }
    public void resetText(){
        interactable.text = "";
        details.text = "";
    }

    public void setColors(Color32 trueC, Color32 falseC){
        trueColor = trueC;
        falseColor = falseC;
        
        return;
    }
    
public void StopReading()
    {
        if (!speakAfterMenuClose) ttsSpeaker.Stop();
    }
}

