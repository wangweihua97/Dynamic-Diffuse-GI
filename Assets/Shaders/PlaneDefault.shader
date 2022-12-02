Shader "Unlit/PlaneDefault"
{
    Properties
    {
        [MainTexture]_BaseMap ("Base Texture",2D) = "white"{}
        _BaseColor("Base Color",Color)=(1,1,1,1)
        [Toggle]_IsSpecular("是否开启高光", Float) = 1
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"//这是一个URP Shader！
            "Queue"="Geometry"
            "RenderType"="Opaque"
        }
        HLSLINCLUDE
         //CG中核心代码库 #include "UnityCG.cginc"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
       
        //除了贴图外，要暴露在Inspector面板上的变量都需要缓存到CBUFFER中
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half _IsSpecular;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Tags{"LightMode"="UniversalForward"}//这个Pass最终会输出到颜色缓冲里
            
            cull Off

            HLSLPROGRAM //CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 normalOS : NORMAL;
                float2 uv : TEXCOORD;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varings//这就是v2f
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD;
                float3 positionWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            Varings vert(Attributes IN)
            {
                Varings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                //在CG里面，我们这样转换空间坐标 o.vertex = UnityObjectToClipPos(v.vertex);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS.xyz);
                OUT.positionCS = positionInputs.positionCS;
                  
                OUT.uv=TRANSFORM_TEX(IN.uv,_BaseMap);
                OUT.positionWS = positionInputs.positionWS;
                OUT.viewDirWS = GetCameraPositionWS() - positionInputs.positionWS;
                OUT.normalWS = normalInputs.normalWS;
                return OUT;
            }

            float4 frag(Varings IN):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                float4 SHADOW_COORDS = TransformWorldToShadowCoord(IN.positionWS);
                Light light = GetMainLight(SHADOW_COORDS);
                half3 n = normalize(IN.normalWS);
                half3 v = normalize(IN.viewDirWS);
                half3 h = normalize(light.direction + v);
                
                
                half nl = max(0.0,dot(light.direction ,n));
                half nh = max(0.0,dot(h ,n));
                
                half atten = step(0.5, light.shadowAttenuation);
                half3 diffuse = atten * lerp(0.5*baseMap.xyz ,baseMap.xyz ,nl) + (1 - atten) * 0.4 * baseMap.xyz* light.color;
                half3 specular = _IsSpecular * atten * light.color * step(0.8,pow(nh ,8));
                
                
                
                //计算附加光照
                uint pixelLightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0; lightIndex < pixelLightCount; ++lightIndex)
                {
                    Light add_light = GetAdditionalLight(lightIndex, IN.positionWS);
                    half3 add_h = normalize(add_light.direction + v);
                
                
                    half add_nl = max(0.0,dot(add_light.direction ,n));
                    half add_nh = max(0.0,dot(add_h ,n));
                    diffuse += baseMap.xyz * add_nl* add_light.color * add_light.distanceAttenuation;
                    specular += _IsSpecular * add_light.color * add_light.distanceAttenuation * step(0.8,pow(add_nh ,8));
                }
                half3 color=diffuse*_BaseColor.xyz;
                //half3 color=diffuse*_BaseColor.xyz +specular;
                           
                return half4(color ,1.0);
            }
            ENDHLSL  //ENDCG          
        }
        
        pass {
			Tags{ "LightMode" = "ShadowCaster" }
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
 
			struct appdata
			{
				float4 vertex : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
 
			struct v2f
			{
				float4 pos : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
 
			sampler2D _MainTex;
			float4 _MainTex_ST;
 
			v2f vert(appdata v)
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
				o.pos = mul(UNITY_MATRIX_MVP,v.vertex);
				return o;
			}
			float4 frag(v2f i) : SV_Target
			{
			    UNITY_SETUP_INSTANCE_ID(i);
				return half4(0.0,0.0,0.0,1.0);
			}
			ENDHLSL
		}
    }
}