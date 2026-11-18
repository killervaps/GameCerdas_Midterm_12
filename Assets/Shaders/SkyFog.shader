// Kabut warna langit (Sky Fog) untuk kamera orang ketiga.
// Dijalankan oleh Full Screen Pass Renderer Feature (injection: Before Rendering Transparents, requirements: Depth + Color).
// Per piksel: jarak dari kamera (dari depth) menentukan seberapa tebal kabut, lalu warna piksel dicampur dengan
// warna LANGIT di arah piksel itu (bukan satu warna kabut). Kabut penuh = piksel menampilkan langit di belakang benda,
// jadi benda jauh (mis. arena lain yang melayang) seolah melebur ke langit berbintang.
// Nilai diatur ThirdPersonFog lewat variabel global, efek mati jika _SkyFogWeight = 0.
Shader "MathGate/SkyFog"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "SkyFog"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURECUBE(_SkyFogCube);
            SAMPLER(sampler_SkyFogCube);

            float _SkyFogWeight;
            float _SkyFogStart;
            float _SkyFogEnd;
            float _SkyFogHasCube;
            float _SkyFogFlipY;

            half3 SampleSky(float3 direction)
            {
                if (_SkyFogHasCube > 0.5)
                    return SAMPLE_TEXTURECUBE_LOD(_SkyFogCube, sampler_SkyFogCube, direction, 0).rgb;

                // Cadangan: pantulan langit bawaan URP (resolusi Environment Reflections di Lighting window).
                half4 encoded = SAMPLE_TEXTURECUBE_LOD(_GlossyEnvironmentCubeMap, sampler_GlossyEnvironmentCubeMap, direction, 0);
                return DecodeHDREnvironment(encoded, _GlossyEnvironmentCubeMap_HDR);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                if (_SkyFogWeight <= 0.0)
                    return color;

                float rawDepth = SampleSceneDepth(uv);

                // Piksel langit (depth = bidang jauh) dibiarkan apa adanya.
            #if UNITY_REVERSED_Z
                if (rawDepth <= 0.0)
                    return color;
            #else
                if (rawDepth >= 1.0)
                    return color;
            #endif

                float2 positionUV = _SkyFogFlipY > 0.5 ? float2(uv.x, 1.0 - uv.y) : uv;
                float3 worldPosition = ComputeWorldSpacePosition(positionUV, rawDepth, UNITY_MATRIX_I_VP);
                float3 toPixel = worldPosition - _WorldSpaceCameraPos;
                float pixelDistance = length(toPixel);

                float fogAmount = saturate((pixelDistance - _SkyFogStart) / max(0.001, _SkyFogEnd - _SkyFogStart)) * _SkyFogWeight;
                half3 sky = SampleSky(toPixel / max(pixelDistance, 0.0001));

                color.rgb = lerp(color.rgb, sky, fogAmount);
                return color;
            }
            ENDHLSL
        }
    }
}
