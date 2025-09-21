# Automated Alt Text in Unity

## Dependancies
[VR Accessibility SDK](https://github.com/JustinMorera/VR-Accessibility-SDK): Implementation of Accessibility Tags and Text-to-Speech (TTS). VR-Accessibility-Vision was built on the foundation of the VR Accessibility SDK and uses the Accessibility Tags class implemented by it. ____ was designed so it can be used by the PartialVis assitance that was implemented by the VR Accessibility SDK or to modify it. 

## Requirements
Tested in Unity 6.0. 

## Intallation: 

### Manual Installation:
1. Download project from GitHub.
2. Add the VR-Accessibility-Vision folder to the /Packages folder of your project. 
3. Allow Unity to reload the project.

### Unity Package Manager:
1. Navigate to 'Window -> Package Manager' using the tabs on the top of the Unity platform.
2. In the Package Manager window, press the '+' icon on the top-left and press 'Add package from git URL...' and enter this project's URL, 'https://github.com/YaroslavRychkov/VR-Accessibility-Vision)'.
3. The package will automatically install.

## How to use
1. Download VR Accessibility SDK and VR-Accessibility-Vision.
2. In your open Unity Scene, create an empty GameObject named "LabelingScript". Attach the LabelingScript.cs to it.
3. Drag and drop your pre-trained NN model (in onnx format) and the corresponding synset text file into the "Model Asset" and "Labels Map" fields. 
4. Under 'Edit' -> 'Project settings' -> 'Tags & Layers', add a User Layer with the tag "Screenshot Layer"
5. Run 'Tools/Add Labels to Game Objects' from the upper menu bar.
6. You will be presented with a confirmation window. 
* To accept a classification, select the checkbox next to the classification text and click on the "Confirm" button. 
* To leave the current Alt text field empty or unchanged but treat the object as previously classified, click "Skip". 
* To return to this object later, click "Save for later"
* You can navigate back and change previous selections at any time.
7. Once every object was classified, skipped, or saved for later, you can review the objects that were saved for later.
8. Before closing the confirmation window, you can choose to save a list with all objects that you already classified. You can use this list when starting a new run by adding it to the "Already Classified Objects" field of the LabelingScript. If you do, objects in this list will be skipped when executing the classification process again.
9. Every Object with a Mesh & Collider now has an Alt text. Use this Alt text directly in your project or with the help of the VR Accessibility SDK. 

## Text to Speech 
1. Follow the instructions found in [JustinMorera's project](https://github.com/JustinMorera/VR-Accessibility-SDK) to enable Partial Vision Tool and Text-to-Speech
2. (optional) Add the "ExtendedPartialVis.cs" script to the "Partial Vision Assistance" Game Object. Right click on the old Partial Vis script and select "Copy Component". Right click on the new Partial Vis script and select "Paste Component Values". Remove the old Partial Vis script. 

## Download links

Any image classification NN model in the ONNX format that has the input format of [batch size, 3, width, height] and output of [1, number_of_output_classes] can be used with this project. Below you can find some examples:


[Animals Classification](https://huggingface.co/AliGhiasvand86/10-animals-classification)

[Instruments Classification](https://huggingface.co/larynx1982/musical-instruments)

[Hugging Face Image Classification Model Database](https://huggingface.co/models?pipeline_tag=image-classification&sort=trending)

[ImageNet based models collection](https://github.com/onnx/models/tree/main)
