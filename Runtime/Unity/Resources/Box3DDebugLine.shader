// Unlit vertex-color line shader for Box3DDebugLineMesh. The untagged pass renders as
// SRPDefaultUnlit under URP and as a normal transparent pass under the built-in pipeline.
// _ZTest lets one shader serve both the depth-tested and x-ray overlay materials.
Shader "Box3D/DebugLine"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _ZTest ("ZTest", Int) = 4 // CompareFunction.LessEqual
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Tint;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Tint;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}
