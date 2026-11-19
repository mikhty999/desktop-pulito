namespace DesktopPulito.Infrastructure.Gpu;

/// <summary>
/// Shader HLSL degli effetti di transizione. Un solo pixel shader con 5 modalità:
/// 0 Bruciatura, 1 Pixel, 2 Glitch, 3 Increspatura, 4 Inchiostro.
/// TA = desktop "prima", TB = desktop "dopo", T = avanzamento 0→1.
/// </summary>
static class Shaders
{
    public const string Source = """
Texture2D TA : register(t0);
Texture2D TB : register(t1);
SamplerState S : register(s0);
cbuffer P : register(b0) { float T; float Mode; float Aspect; float Seed; float2 Origin; float2 Px; };

struct V { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

// triangolo che copre tutto lo schermo, senza vertex buffer
V VSMain(uint id : SV_VertexID)
{
    V o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.uv = uv;
    o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

float hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
float vnoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3 - 2 * f);
    return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
}
float fbm(float2 p)
{
    float v = 0, a = 0.5;
    [unroll] for (int k = 0; k < 5; k++) { v += a * vnoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
    return v;
}

// Bruciatura: un fronte frastagliato parte dal puntatore; davanti bruciacchia, sul bordo fiamma, dietro braci.
float4 Burn(float2 uv)
{
    float2 q = float2(uv.x * Aspect, uv.y);
    float d = distance(q, float2(Origin.x * Aspect, Origin.y));
    float field = d / (Aspect * 0.9 + 0.6) + fbm(q * 3.5 + Seed) * 0.35;
    float e = field - (T * 1.45 - 0.1);
    float4 a = TA.Sample(S, uv), b = TB.Sample(S, uv);
    if (e > 0.09) return a;
    if (e > 0.025) { float k = 1 - (e - 0.025) / 0.065; return float4(lerp(a.rgb, a.rgb * float3(0.45, 0.28, 0.16), k * k), 1); }
    if (e > 0.0)
    {
        float k = 1 - e / 0.025;
        float3 fire = lerp(float3(1.0, 0.5, 0.1), float3(1.0, 0.93, 0.62), k);
        return float4(fire * (1.05 + 0.45 * vnoise(q * 60 + T * 9)), 1);
    }
    if (e > -0.04) { float k = (e + 0.04) / 0.04; return float4(lerp(b.rgb, float3(1.0, 0.33, 0.05), k * k * 0.85), 1); }
    return b;
}

// Pixel: lo schermo si scompone in quadratini che si scambiano in ordine casuale, poi torna nitido.
float4 Pixelate(float2 uv)
{
    float k = sin(T * 3.14159);
    float cell = lerp(1, 56, k * k);
    float2 grid = Px / cell;
    float2 c = floor(uv * grid);
    float2 cuv = (c + 0.5) / grid;
    float pick = step(hash(c + Seed), saturate(T * 1.3 - 0.15));
    float2 suv = k > 0.03 ? cuv : uv;
    return lerp(TA.Sample(S, suv), TB.Sample(S, suv), pick);
}

// Glitch: fasce che saltano, separazione dei colori, grana; il passaggio avviene a blocchi.
float4 Glitch(float2 uv)
{
    float k = sin(T * 3.14159);
    float band = floor(uv.y * 28 + floor(T * 18));
    float on = step(0.55, hash(float2(band * 1.7, floor(T * 30))));
    float jump = (hash(float2(band, floor(T * 24))) - 0.5) * 0.09 * k * on;
    float2 u = float2(uv.x + jump, uv.y);
    float split = 0.012 * k;
    float m = smoothstep(0.35, 0.65, T + (hash(float2(band, 3)) - 0.5) * 0.4 * k);
    float4 ca = float4(TA.Sample(S, u + float2(split, 0)).r, TA.Sample(S, u).g, TA.Sample(S, u - float2(split, 0)).b, 1);
    float4 cb = float4(TB.Sample(S, u + float2(split, 0)).r, TB.Sample(S, u).g, TB.Sample(S, u - float2(split, 0)).b, 1);
    float4 c = lerp(ca, cb, m);
    c.rgb += (hash(uv * Px + T) - 0.5) * 0.12 * k;
    return c;
}

// Increspatura: un'onda d'acqua parte dal puntatore e porta con sé il nuovo desktop.
float4 Ripple(float2 uv)
{
    float2 q = float2(uv.x * Aspect, uv.y), o = float2(Origin.x * Aspect, Origin.y);
    float d = distance(q, o);
    float2 dir = (q - o) / max(d, 1e-4);
    float front = T * (Aspect + 1.2);
    float wave = sin((d - front) * 55) * 0.018 * (1 - T) * exp(-abs(d - front) * 6);
    float2 off = dir * wave;
    off.x /= Aspect;
    float m = smoothstep(front + 0.05, front - 0.05, d);
    return lerp(TA.Sample(S, uv + off), TB.Sample(S, uv + off), m);
}

// Inchiostro: una macchia scura si allarga dal puntatore con bordi organici.
float4 Ink(float2 uv)
{
    float2 q = float2(uv.x * Aspect, uv.y), o = float2(Origin.x * Aspect, Origin.y);
    float d = distance(q, o);
    float field = d / (Aspect * 0.9 + 0.6) + fbm(q * 2.2 + Seed) * 0.45;
    float e = field - (T * 1.5 - 0.12);
    float m = smoothstep(0.05, -0.05, e);
    float rim = exp(-abs(e) * 40) * (1 - T * 0.5);
    float3 c = lerp(TA.Sample(S, uv).rgb, TB.Sample(S, uv).rgb, m) * (1 - rim * 0.75);
    return float4(c, 1);
}

float4 PSMain(V i) : SV_Target
{
    int m = (int)Mode;
    if (m == 0) return Burn(i.uv);
    if (m == 1) return Pixelate(i.uv);
    if (m == 2) return Glitch(i.uv);
    if (m == 3) return Ripple(i.uv);
    return Ink(i.uv);
}
""";
}
