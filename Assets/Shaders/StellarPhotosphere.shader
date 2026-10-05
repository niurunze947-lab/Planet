Shader "Planet/StellarPhotosphere"
{
    Properties
    {
        _BlackbodyLut ("Planck / CIE Temperature LUT (linear HDR)", 2D) = "white" {}
        _Temperature ("Mean Photosphere Temperature (K)", Float) = 5778
        _TemperatureContrast ("Granular Temperature Contrast (K)", Float) = 260
        _LimbCooling ("Limb Spectral Cooling (K)", Float) = 380
        _LimbCoefficients ("Quadratic Limb Coefficients", Vector) = (0.48, 0.18, 0, 0)
        _DisplayScale ("Fixed HDR Display Normalization", Float) = 0.95
        _GranuleFrequency ("Radius / Granule Diameter", Float) = 773
        _SuperFrequency ("Radius / Supergranule Diameter", Float) = 23.2
        _NormalRelief ("Relief / Physical Radius", Float) = 0.00000014
        _Seed ("Seed", Float) = 314159
        _SpotCount ("Spot Slots", Float) = 8
        _SpotLifetime ("Spot Lifetime (days)", Float) = 12
        _SpotRadius ("Spot Radius (radians)", Float) = 0.035
        _SpotTemperatures ("Umbra / Penumbra (K)", Vector) = (3800, 4800, 0, 0)
        _FocusedObservation ("Focused Solar Observation", Float) = 0
        _FocusExposureEV ("Focused Photosphere Exposure", Range(-8, 0)) = -2.35
        _FocusSpectralResponse ("Focused Spectral Response", Color) = (1, 0.12, 0.018, 1)
        _FocusSpectralMix ("Focused Spectral Mix", Range(0, 1)) = 0.76
        _FocusedGranulationBoost ("Focused Granulation Boost", Range(0.5, 3)) = 1.75
        _FocusLimbGlow ("Focused Limb Glow", Range(0, 2)) = 0.72
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Photosphere"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BlackbodyLut); SAMPLER(sampler_BlackbodyLut);
            CBUFFER_START(UnityPerMaterial)
                float _Temperature, _TemperatureContrast, _LimbCooling, _DisplayScale;
                float4 _LimbCoefficients;
                float _GranuleFrequency, _SuperFrequency, _NormalRelief, _Seed;
                float _SpotCount, _SpotLifetime, _SpotRadius;
                float4 _SpotTemperatures;
                float _FocusedObservation, _FocusExposureEV, _FocusSpectralMix;
                float4 _FocusSpectralResponse;
                float _FocusedGranulationBoost, _FocusLimbGlow;
            CBUFFER_END
            float _EquatorialPhase, _RotationShear, _ActivityDays;
            float4 _ConvectionPhase;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 directionOS : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings Vert(Attributes i)
            {
                Varyings o; o.directionOS = normalize(i.positionOS.xyz);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS); return o;
            }
            float Hash(float3 p)
            {
                p = frac(p * 0.1031); p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float3 Hash3(float3 p)
            {
                return float3(Hash(p), Hash(p + 31.7), Hash(p - 47.1));
            }
            float Noise(float3 p)
            {
                float3 cell = floor(p), f = frac(p); f = f*f*(3-2*f);
                float n000=Hash(cell), n100=Hash(cell+float3(1,0,0)), n010=Hash(cell+float3(0,1,0)), n110=Hash(cell+float3(1,1,0));
                float n001=Hash(cell+float3(0,0,1)), n101=Hash(cell+float3(1,0,1)), n011=Hash(cell+float3(0,1,1)), n111=Hash(cell+1);
                return lerp(lerp(lerp(n000,n100,f.x),lerp(n010,n110,f.x),f.y),
                    lerp(lerp(n001,n101,f.x),lerp(n011,n111,f.x),f.y),f.z);
            }
            float Fbm(float3 p)
            {
                return (Noise(p) + 0.5*Noise(p*2.03+17) + 0.25*Noise(p*4.13-11))/1.75;
            }
            float2 Voronoi(float3 p)
            {
                float3 cell=floor(p), f=frac(p); float f1=100, f2=100;
                [unroll] for(int z=-1;z<=1;z++)
                [unroll] for(int y=-1;y<=1;y++)
                [unroll] for(int x=-1;x<=1;x++)
                {
                    float3 neighbour=float3(x,y,z);
                    float3 delta=neighbour+Hash3(cell+neighbour)-f;
                    float distance=dot(delta,delta);
                    if(distance<f1) {f2=f1;f1=distance;} else f2=min(f2,distance);
                }
                return sqrt(float2(f1,f2));
            }
            float3 RotateY(float3 d, float phase)
            {
                float s,c; sincos(phase,s,c); return float3(c*d.x-s*d.z,d.y,s*d.x+c*d.z);
            }
            float SpotTemperature(float3 d, float3 viewDirectionOS, float temperature, float granulation, out float facula)
            {
                facula = 0;
                [loop] for(int i=0;i<16;i++)
                {
                    if(i>=int(_SpotCount)) break;
                    float slot=float(i)+_Seed*0.0001;
                    float phase=_ActivityDays/_SpotLifetime+Hash(float3(slot,11,3));
                    float cycle=floor(phase), age=frac(phase);
                    float life=smoothstep(0,0.15,age)*(1-smoothstep(0.75,1,age));
                    float lon=Hash(float3(slot,cycle,17))*2*PI+_ActivityDays*0.001;
                    float lat=(7+28*Hash(float3(slot,cycle,29)))*(((uint)i&1u)==0u?1:-1)*PI/180;
                    float3 centre=float3(cos(lat)*cos(lon),sin(lat),cos(lat)*sin(lon));
                    // Do not expose a backside/limb spot as a long crescent. The focused view is
                    // a photospheric inspection, so only activity comfortably on the visible
                    // hemisphere contributes a compact umbra/penumbra disc.
                    if(dot(centre,viewDirectionOS)<0.60) continue;
                    float angularDistance=acos(clamp(dot(d,centre),-1.0,1.0));
                    float q=angularDistance/max(_SpotRadius*(0.65+0.35*life),0.0001);
                    if(q>0.82) continue;
                    float3 east=float3(-sin(lon),0,cos(lon)), north=cross(east,centre);
                    float theta=atan2(dot(d,north),dot(d,east));
                    float filament=sin(theta*64+q*12+Noise(d*170)*2)*0.5+0.5;
                    float penumbra=(1-smoothstep(0.42,0.82,q))*life;
                    float umbra=(1-smoothstep(0.18,0.42,q))*life;
                    facula=max(facula,exp(-pow((q-0.67)/0.16,2))*life*(0.55+0.45*filament));
                    // Magnetic suppression removes granular contrast within the spot.
                    float penTemp=_SpotTemperatures.y+(filament-0.5)*240;
                    temperature=lerp(temperature,penTemp,penumbra);
                    temperature=lerp(temperature,_SpotTemperatures.x,umbra);
                }
                return temperature;
            }
            float4 Frag(Varyings i) : SV_Target
            {
                float3 radial=normalize(i.directionOS);
                // The object's rigid catalog spin is cancelled by the view. All latitude bands
                // now rotate at their own angular speed, rather than rotating a baked sphere.
                float3 d=RotateY(radial,-(_EquatorialPhase+_RotationShear*radial.y*radial.y));
                float3 view=normalize(_WorldSpaceCameraPos.xyz-i.positionWS);
                float3 viewDirectionOS=normalize(TransformWorldToObjectDir(view));
                float seed=frac(_Seed*0.001)*80;
                float gtime=_ConvectionPhase.x, stime=_ConvectionPhase.y;
                float3 flow=float3(sin(gtime*0.3),gtime*0.12,cos(gtime*0.27));
                float3 p=d*_GranuleFrequency+seed+flow;
                float footprint=max(length(ddx(p)),length(ddy(p)));
                // A 900 km granule is close to one pixel at the usual focused framing. Keep a
                // filtered contribution until the footprint is several cells wide, otherwise the
                // entire photosphere becomes a flat red disc before the viewer can inspect it.
                float filteredResolved=1-smoothstep(0.45,3.6,footprint);
                // Focused inspection intentionally keeps a low-amplitude subpixel field across
                // the disc. The normal distance filter would otherwise leave a conspicuous bright
                // equatorial band where the projected derivatives happen to be smallest.
                float resolved=lerp(filteredResolved,1.0,_FocusedObservation);
                float2 cells=Voronoi(p);
                float border=1-smoothstep(0.015,0.11,cells.y-cells.x);
                float centre=1-smoothstep(0.15,0.8,cells.x);
                float convection=(centre*0.85-border*0.75)*(0.85+0.15*sin(gtime+Noise(d*17)*6.28))*resolved;
                float mediumGranulation=(Fbm(d*(_GranuleFrequency*0.32)+seed+flow*0.35)-0.5)*0.10*resolved;
                float subpixelGranulation=(Noise(p*1.73+float3(11.3,4.7,19.1))-0.5)*0.04*resolved;
                convection+=(mediumGranulation+subpixelGranulation)*_FocusedObservation;
                float supercell=Fbm(RotateY(d,stime*0.04)*_SuperFrequency+seed+stime*0.1)-0.5;
                float temperature=_Temperature+_TemperatureContrast*convection+supercell*100;
                convection *= lerp(1.0, _FocusedGranulationBoost, _FocusedObservation);
                temperature=_Temperature+_TemperatureContrast*convection+supercell*100;
                float facula;
                temperature=SpotTemperature(d,viewDirectionOS,temperature,convection,facula);
                float3 worldNormal=normalize(TransformObjectToWorldNormal(radial));
                // The physical 100-m relief is tiny relative to a 696,340-km star. A surface
                // gradient changes normals without displacing a silhouette by fictional kilometres.
                // The physical photospheric relief is only ~100 m over a 696,340 km radius.
                // Keep the silhouette and normal field stable; the temperature field carries the
                // resolved granulation. A screen-space gradient here creates poles/triangle-size
                // singularities that become black arcs under the focused low exposure.
                worldNormal=normalize(TransformObjectToWorldNormal(radial));
                float mu=saturate(dot(worldNormal,view)), edge=1-mu;
                float intensity=max(0,1-_LimbCoefficients.x*edge-_LimbCoefficients.y*edge*edge);
                float spectralTemperature=clamp(temperature-_LimbCooling*edge,2500,9000);
                float u=(spectralTemperature-2500)/6500;
                float3 chromaticity=SAMPLE_TEXTURE2D_LOD(_BlackbodyLut,sampler_BlackbodyLut,float2(u,0.5),0).rgb;
                float3 focusedChromaticity=chromaticity*max(_FocusSpectralResponse.rgb,0.001);
                chromaticity=lerp(chromaticity,focusedChromaticity,_FocusedObservation*_FocusSpectralMix);
                float thermal=pow(temperature/_Temperature,4);
                thermal=lerp(thermal,1+(thermal-1)*_FocusedGranulationBoost,_FocusedObservation*0.8);
                // Linear HDR radiance, normalized by the reference photospheric radiance.
                // No albedo, metallic, reflection, external light or shadow-map term.
                float focusExposure=exp2(_FocusedObservation*_FocusExposureEV);
                float focusFacula=facula*_FocusedObservation*0.35;
                float limbGlow=pow(saturate(1-mu),1.65)*_FocusLimbGlow*_FocusedObservation;
                float3 radiance=chromaticity*thermal*(intensity+focusFacula+limbGlow)*_DisplayScale*focusExposure;
                return float4(radiance,1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Unlit/DepthOnly"
    }
}
