Shader "Custom/HgTrackVisualizer" {
    Properties{
            _MainTex("Base (RGB)", 2D) = "white" {}

    }
        SubShader{
                Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent"}
                LOD 100
                Lighting Off Fog { Mode off }
                CGPROGRAM

                #pragma surface surf Lambert alpha

                struct Input {
                    float2 uv_MainTex;
                };

                //Input Variables here:
                sampler2D _MainTex;
                float4 _MaskCol[24];
                float _Sensitivity[24];
                float _Smooth[24];
                float _Raw[24];
                float _Threashold[24];
                float _Length;

                //Surface shader:
                void surf(Input IN, inout SurfaceOutput o) 
                {              
                    //We get the UV for our plane (Unfortunately, WebcamTextures in Unity have an Inverted Y axis in UVs, So we invert it):
                    float2 UV = float2(IN.uv_MainTex.x,1.0 - IN.uv_MainTex.y);
                    //Get color of current pixel:
                    float4 Col = tex2D(_MainTex, UV);

                    //Go through all our Trackers, Visualizing them one by one:
                    for(int i=0; i<_Length; i++)
                    {
                        //Get if User wants a raw view or a Tracked view:
                        if(_Raw[i] < .5)
                        {
                            //Boaring Luma-Key stuff:
                        float4 Luma = _MaskCol[i];

                        float maskY = 0.2989 * Luma.r + 0.5866 * Luma.g + 0.1145 * Luma.b;
                        float maskCr = 0.7132 * (Luma.r - maskY);
                        float maskCb = 0.5647 * (Luma.b - maskY);

                        float Y = 0.2989 * Col.r + 0.5866 * Col.g + 0.1145 * Col.b;
                        float Cr = 0.7132 * (Col.r - Y);
                        float Cb = 0.5647 * (Col.b - Y);

                        float blendValue = smoothstep(_Sensitivity[i], _Sensitivity[i] + _Smooth[i], distance(float2(Cr, Cb), float2(maskCr, maskCb)));

                        if(blendValue<_Threashold[i])
                        {
                            //Set current pixel color to the Luma color if Detection occurs:
                            Col = Luma;
                        }

                        }

                    }

                    //Output the visual data:
                        o.Albedo = Col;
                        o.Alpha = 1.0;

   
                }
                ENDCG
            }
                FallBack "Diffuse"
}