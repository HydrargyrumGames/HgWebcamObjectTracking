using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Internal;

[AddComponentMenu("Hydrargyrum games/HgTracker")]
public class HgTracker : MonoBehaviour
{
    public enum TrackingMode { MedianCenter = 0, BoundingBox = 1 };
    public enum UpdatingMode { Update, FixedUpdate, LateUpdate, ScriptOnly };
    public enum TrackerPhase { Began = 0, Moved = 1, Stationary = 2, Ended = 3 };

    [System.Serializable]
    public struct TrackPod
    {
        //This is how each one of our trackers is constructed:
        //All the variables U can edit in a tracker:
        [HideInInspector]
        [Header("Input Variables:")]
        public float MaskY, MaskCb, MaskCr;
        public Color Luma;
        public TrackingMode trackingMode;
        public float DetectionThreshold;
        public float Smoothing;
        public float Sensitivity;
        public bool RawShaderOutput;
        public bool ScreenSpacePositions;
        public bool Invert_X, Invert_Y;

        //These variable cannot be edited:
        [Header("Ouput Variables:")]
        public bool Is_Visible;
        public TrackerPhase Phase;
        public Vector2 RawPosition, DeltaPosition, SmoothedPosition;
        public int TrackerSize;

        //Events to be called on special occasions:
        [Header("Tracker Events:")]
        public UnityEvent OnTrackerEnter;
        public UnityEvent OnTrackerHover;
        public UnityEvent OnTrackerLeave;
    }

    [Header("ComputeShader Asset:")]
    [Tooltip("The ComputeShader used for Object-Tracking on the GPU; Should be assigned to the 'WebcamHandler.compute' file included with this asset;")]
    public ComputeShader CSMain;

    [Header("Webcam Settings:")]
    [Tooltip("Either the index, or the actuall Name assigned by your system to the Webcam; Assign no value to use Webcam[0] for Tracking;")]
    public string WebcamName;
    public FilterMode WebcamFiltering = FilterMode.Point;
    [Range(0, 16)]
    public int WebcamAnsio;
    [HideInInspector]
    public Vector2Int WebcamResolution;
    static WebCamTexture WCT;

    [Header("Tracker Settings:")]
    [Tooltip("The Updating method which will be used for the object-Tracking; Choose either Update() or LateUpdate() for Framerate dependant jobs, while using FixedUpdate() for Physics orianted jobs;")]
    public UpdatingMode UpdateMode;
    [Tooltip("The actual list of the so-called 'Trackers' used for setting up parameters for Object-Tracking;")]
    public TrackPod[] Trackers;

    [Header("Output Shader Settings:")]
    [Tooltip("The Unlit shader used for the visual Output of the Tracker; Should be assigned to the 'WebcamView.shader' file included with this asset;")]
    public Material OutputMaterial;

    //Complimentary and Debug Variables here:
    RenderTexture CSOutput;
    Texture2D CSInput;
    [ReadOnly]
    Color[] Col;
    float[] Sensitivity;
    float[] Smooth;
    float[] Raw;
    float[] Threashold;
    bool Starting = true;
    Texture2D Tex;
    Vector2 Vel;

    private void Start()
    {
        //We Initialize our webcam:
        //Check if there are any webcams connected to thi device:
        if (WebCamTexture.devices.Length > 0)
        {
            //Get if the user wants to use a specific camera or the system default one:
            if (WebcamName != "")
            {
                try
                {
                    //If the input field has an 'Index' written to it:
                    if (int.Parse(WebcamName) <= WebCamTexture.devices.Length)
                    {
                        WCT = new WebCamTexture(WebCamTexture.devices[int.Parse(WebcamName)].name);
                    }
                }
                catch
                {
                    //If the entered value is a Webcam's system defined name:
                    WCT = new WebCamTexture(WebcamName);
                }
            }
            else
            {
                WCT = new WebCamTexture();
            }
            //apply settings for our webcam:
            WCT.filterMode = WebcamFiltering;
            WCT.anisoLevel = WebcamAnsio;
            //Start the Webcam stream:
            WCT.Play();
            //get the resolution of our webcam and write it to a variable for further processing:
            WebcamResolution = new Vector2Int(WCT.width, WCT.height);
        }
        else
        {
            //Throw Exceptions if the device is not equipped with a compatible Webcam:
            Debug.LogError("This Device is not equipped with a compatible Webcam!");
            return;
        }

        //validate all our trackers [called both on Start() and OnValidate() :]
        ValidateTrackerData();
        Starting = false;
    }

    private void OnValidate()
    {
        ValidateTrackerData();
    }

    public void ValidateTrackerData()
    {
        //check for nulls by checking if Trackers array has any members in it:
        if (Trackers.Length > 0)
        {
            //Doing some of these stuff really tortures the Unity editor, So we simply execute the program if the game is running:
            if (Application.isPlaying)
            {
                //We initialize our Input Texture (Specialized to carry floats between our script and ComputShader) [Done when we start / or the Tracker array recieves new members]:
                if (CSInput == null || CSInput.width != Trackers.Length)
                {
                    CSInput = new Texture2D(Trackers.Length, 2, TextureFormat.RGBAFloat, false, true);
                    CSInput.filterMode = FilterMode.Point;
                    CSInput.anisoLevel = 0;
                }

                //We initialize our output rendertexture (Specialized to carry floats between our script and ComputShader) [Done when we start / or the Tracker array recieves new members]:
                if (CSOutput == null || CSOutput.width != Trackers.Length)
                {
                    if (!(CSOutput == null))
                    {
                        //if we want to increase the rendertexture size, we first have to Release it:
                        CSOutput.Release();
                        CSOutput.DiscardContents();
                    }

                    CSOutput = new RenderTexture(Trackers.Length, 2, 0, RenderTextureFormat.ARGBFloat);
                    CSOutput.enableRandomWrite = true;
                    CSOutput.filterMode = FilterMode.Point;
                    CSOutput.anisoLevel = 0;
                    CSOutput.antiAliasing = 1;
                    CSOutput.Create();
                }
            }

            //Executing this for each of our trackers:
            for (int i = 0; i < Trackers.Length; i++)
            {
                //Getting the Tracker data into a variable for optimization only:
                TrackPod TP = Trackers[i];
                //We calculate the Y, Cr and Cb values:
                Trackers[i].MaskY = 0.2989f * TP.Luma.r + 0.5866f * TP.Luma.g + 0.1145f * TP.Luma.b;
                Trackers[i].MaskCr = 0.7132f * (TP.Luma.r - TP.MaskY);
                Trackers[i].MaskCb = 0.5647f * (TP.Luma.b - TP.MaskY);

                //We set the tracker.Phase variable here:
                if (Starting)
                {
                    Trackers[i].Phase = TrackerPhase.Ended;
                }

                //Writing data to the Input texture according to the following scheme:
                if (Application.isPlaying && CSInput != null)
                {
                    CSInput.SetPixel(i, 0, new Color(TP.Sensitivity, TP.Smoothing, TP.MaskCr + .3f, Trackers.Length));
                    CSInput.SetPixel(i, 1, new Color(TP.MaskCb + .3f, (int)TP.trackingMode, TP.DetectionThreshold, WebcamResolution.x));
                    CSInput.Apply();
                }
                //R1     Sensitivity
                //G1     Smoothing
                //B1     MaskCr
                //A1     Trackers.length
                //R2     MaskCb
                //G2     TrackingMode
                //B2     DectetctionThreashold
                //A2     Webcam.x
            }

            //we chack if the user wants to have visual output:
            if (OutputMaterial != null)
            {
                Col = new Color[Trackers.Length];
                Sensitivity = new float[Trackers.Length];
                Smooth = new float[Trackers.Length];
                Raw = new float[Trackers.Length];
                Threashold = new float[Trackers.Length];

                for (int i = 0; i < Trackers.Length; i++)
                {
                    //Carrying variables from our array to the material shader:
                    TrackPod TP = Trackers[i];
                    Col[i] = TP.Luma;
                    Sensitivity[i] = TP.Sensitivity;
                    Smooth[i] = TP.Smoothing;
                    if (TP.RawShaderOutput)
                    {
                        Raw[i] = 1f;
                    }
                    else
                    {
                        Raw[i] = 0f;
                    }
                    Threashold[i] = TP.DetectionThreshold;
                }

                OutputMaterial.SetTexture("_MainTex", WCT);
                OutputMaterial.SetFloatArray("_Sensitivity", Sensitivity);
                OutputMaterial.SetColorArray("_MaskCol", Col);
                OutputMaterial.SetFloatArray("_Smooth", Smooth);
                OutputMaterial.SetFloatArray("_Raw", Raw);
                OutputMaterial.SetFloatArray("_Threashold", Threashold);
                OutputMaterial.SetFloat("_Length", Trackers.Length);
            }
        }
    }

    //Main method for tracking Trackers[]:
    public void Track()
    {
        //check for nulls by checking if Trackers array has any members in it:
        if (Trackers.Length > 0)
        {
            //We check if our rendertexture has enough space to store the output data for all our trackers:
            if (CSOutput.width == Trackers.Length)
            {
                //Sending variables over to the RenderTexture:
                CSMain.SetTexture(0, "CSInput", CSInput);
                CSMain.SetTexture(0, "CSOutput", CSOutput);
                CSMain.SetTexture(0, "WCT", WCT);
                int ThreadX = WebcamResolution.x / 4;
                int ThreadY = WebcamResolution.y / 4;
                //Dispatching our CS:
                CSMain.Dispatch(0, ThreadX, ThreadY, 1);
                //Converting our output rendertexture into a normal texture for reading data more easily:
                Tex = new Texture2D(Trackers.Length, 2, TextureFormat.RGBAFloat, false);
                RenderTexture RTold = RenderTexture.active;
                RenderTexture.active = CSOutput;
                Tex.ReadPixels(new Rect(0, 0, Trackers.Length, 2), 0, 0);
                // For F*** sake, always apply the colors U write to a texture, This had me debugging for weeks:
                /**/
                Tex.Apply();      /**/
                RenderTexture.active = RTold;

                //We go over all our Trackers and read data from the Rendertexture:
                for (int i = 0; i < Trackers.Length; i++)
                {
                    //Output in 2 pixels for each tracker:
                    Color Pixel1 = Tex.GetPixel(i, 0);
                    Color Pixel2 = Tex.GetPixel(i, 1);

                    //See if the tracker is seeing anything:
                    if (Pixel2.r < .5f)
                    {
                        //Creatinga placeholder for our position variable:
                        Vector2 Pos = Vector2.zero;

                        //Do calculations according to tracking mode requested by our user:
                        if (Trackers[i].trackingMode == TrackingMode.MedianCenter)
                        {
                            if (Trackers[i].ScreenSpacePositions)
                            {
                                //R:X  G:Y  B:Count  //This is screenSpace
                                Pos = new Vector2(Pixel1.r / (float)WebcamResolution.x, Pixel1.g / (float)WebcamResolution.y) / Pixel1.a;
                            }
                            else
                            {
                                //Not screenSpace
                                Pos = new Vector2(Pixel1.r, Pixel1.g) / Pixel1.a;
                            }

                            Trackers[i].TrackerSize = Mathf.RoundToInt(Pixel1.a) * 2;
                        }
                        else if (Trackers[i].trackingMode == TrackingMode.BoundingBox)
                        {
                            if (Trackers[i].ScreenSpacePositions)
                            {
                                //R:Xmin  G:Xmax  B:Ymin  A:Ymax  //This is screenSpace
                                Pos = new Vector2((Pixel1.r + Pixel1.g) / (float)WebcamResolution.x, (Pixel1.b + Pixel1.a) / (float)WebcamResolution.y) / 2f;
                            }
                            else
                            {
                                //Not screenSpace
                                Pos = new Vector2(Pixel1.r + Pixel1.g, Pixel1.b + Pixel1.a) / 2f;
                            }

                            Trackers[i].TrackerSize = Mathf.RoundToInt((Pixel1.g - Pixel1.r) * (Pixel1.a - Pixel1.b) / (Screen.width));
                        }

                        //revert X position if User wants to:
                        if (!Trackers[i].Invert_X)
                        {
                            if (Trackers[i].ScreenSpacePositions)
                            {
                                Pos = new Vector2(1f - Pos.x, Pos.y);
                            }
                            else
                            {
                                Pos = new Vector2((float)WebcamResolution.x - Pos.x, Pos.y);
                            }

                        }
                        //revert Y position if User wants to:
                        if (Trackers[i].Invert_Y)
                        {
                            if (Trackers[i].ScreenSpacePositions)
                            {
                                Pos = new Vector2(Pos.x, 1f - Pos.y);
                            }
                            else
                            {
                                Pos = new Vector2(Pos.x, (float)WebcamResolution.y - Pos.y);
                            }
                        }

                        //Set teh phase of our trackers according to some variables:
                        if (Trackers[i].Is_Visible == false)
                        {
                            Trackers[i].Phase = TrackerPhase.Began;
                            Trackers[i].OnTrackerEnter.Invoke();
                        }
                        else
                        {
                            Trackers[i].OnTrackerHover.Invoke();

                            if (Trackers[i].RawPosition == Pos)
                            {
                                Trackers[i].Phase = TrackerPhase.Stationary;
                            }
                            else
                            {
                                Trackers[i].Phase = TrackerPhase.Moved;
                            }
                        }

                        Trackers[i].DeltaPosition = Pos - Trackers[i].RawPosition;
                        Trackers[i].RawPosition = Pos;

                        //Apply some smoothing to positions:
                        Trackers[i].Is_Visible = true;
                        if (float.IsNaN(Trackers[i].SmoothedPosition.x))
                        {
                            Trackers[i].SmoothedPosition = Pos;
                        }
                        else
                        {
                            Trackers[i].SmoothedPosition = Vector2.SmoothDamp(Trackers[i].SmoothedPosition, Pos, ref Vel, .05f);
                        }
                    }
                    //Do this if the tracker is not visible:
                    else
                    {
                        Trackers[i].Is_Visible = false;
                        Trackers[i].Phase = TrackerPhase.Ended;
                        Trackers[i].OnTrackerLeave.Invoke();
                        Trackers[i].RawPosition = new Vector2(float.NaN, float.NaN);
                        Trackers[i].SmoothedPosition = new Vector2(float.NaN, float.NaN);
                        Trackers[i].DeltaPosition = new Vector2(float.NaN, float.NaN);
                        Trackers[i].TrackerSize = 0;
                    }
                }
            }
        }
    }

    private void Update()
    {
        //Track in the user requested loop:
        if (UpdateMode == UpdatingMode.Update)
        {
            Track();
        }
    }

    private void LateUpdate()
    {
        //Track in the user requested loop:
        if (UpdateMode == UpdatingMode.LateUpdate)
        {
            Track();
        }
    }

    private void FixedUpdate()
    {
        //Track in the user requested loop:
        if (UpdateMode == UpdatingMode.FixedUpdate)
        {
            Track();
        }
    }
}
