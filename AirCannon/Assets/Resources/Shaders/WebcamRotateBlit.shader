// 웹캠 프레임을 링 버퍼로 복사할 때 회전/반전을 같이 적용한다 (WebcamRingBuffer에서 Graphics.Blit으로 사용).
// _Rotation: 시계 방향 회전 (0, 1=90, 2=180, 3=270)
// _MirrorX: 결과 이미지 좌우 반전, _FlipSourceY: 웹캠 원본 상하 반전 보정 (videoVerticallyMirrored)
Shader "Hidden/AirCannon/WebcamRotateBlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Rotation;
            float _MirrorX;
            float _FlipSourceY;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                if (_MirrorX > 0.5) uv.x = 1.0 - uv.x;

                float2 src;
                int r = (int)round(_Rotation);
                if (r == 1)      src = float2(1.0 - uv.y, uv.x);
                else if (r == 2) src = float2(1.0 - uv.x, 1.0 - uv.y);
                else if (r == 3) src = float2(uv.y, 1.0 - uv.x);
                else             src = uv;

                if (_FlipSourceY > 0.5) src.y = 1.0 - src.y;
                return tex2D(_MainTex, src);
            }
            ENDCG
        }
    }
}
