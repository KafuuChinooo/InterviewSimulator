Shader "360Sphere"
{
	Properties
	{
		_MainTex("Texture", 2D) = "white" {}
	}
		SubShader
		{
			Tags { "RenderType" = "Opaque" }
			LOD 100
			Cull front

			Pass
			{

				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag

				#include "UnityCG.cginc"

				struct appdata
				{
					float4 vertex : POSITION;
					float4 normal: NORMAL;
				};

				struct v2f
				{
					float4 vertex : SV_POSITION;
					float3 worldNormal : TEXCOORD0;
				};

				sampler2D _MainTex;
				float4 _MainTex_ST;

				v2f vert(appdata v)
				{
					v2f o;
					o.vertex = UnityObjectToClipPos(v.vertex);
					o.worldNormal = normalize(mul((float3x3)unity_WorldToObject, v.normal));
					return o;
				}

				float2 RadialCoords(float3 dir)
				{
					dir = normalize(dir);
					float u = 0.5 + atan2(dir.z, dir.x) / (2.0 * 3.14159265);
					float v = acos(dir.y) / 3.14159265;
					return float2(1.0 - u,1.0 - v);
				}

				fixed4 frag(v2f i) : SV_Target
				{
					float2 uv = RadialCoords(i.worldNormal);
					return tex2D(_MainTex, uv);
				}
				ENDCG
			}
		}
}

