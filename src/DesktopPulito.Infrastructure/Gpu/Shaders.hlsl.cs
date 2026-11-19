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

// Film burn: la pellicola si scioglie nel proiettore. Buchi che si allargano dal puntatore e da punti casuali,
// bordo incandescente, lampi di luce calda (light leak) che sovraespongono, sfarfallio a 24 fps e grana.
float4 Burn(float2 uv)
{
    float2 q = float2(uv.x * Aspect, uv.y);
    float2 o = float2(Origin.x * Aspect, Origin.y);
    float pulse = sin(T * 3.14159);
    float flick = 0.8 + 0.4 * hash(float2(floor(T * 24), Seed));

    // dove la pellicola si scioglie: un buco dal puntatore + bolle sparse
    float d = distance(q, o) / (Aspect * 0.9 + 0.6);
    float n = fbm(q * 2.6 + Seed);
    float field = min(d * 0.9 + n * 0.45, n * 1.15 + 0.25);
    float e = field - (T * 1.35 - 0.1);
    float hole = smoothstep(0.02, -0.02, e);

    float4 a = TA.Sample(S, uv), b = TB.Sample(S, uv);
    float3 col = lerp(a.rgb, b.rgb, hole);

    // light leak: alone caldo che scorre sull'immagine (fusione "schermo": schiarisce senza coprire)
    float2 lp = o + float2(sin(T * 2.1 + Seed), cos(T * 1.7 + Seed)) * 0.45;
    float leak = saturate(1.0 - distance(q, lp) / (Aspect * 0.6));
    float3 leakCol = lerp(float3(0.95, 0.18, 0.05), float3(1.0, 0.72, 0.28), leak);
    float amt = pulse * flick;
    col = 1 - (1 - col) * (1 - leakCol * leak * amt * 0.7);

    // bordo che brucia: filo bianco-dorato con alone ambrato davanti al fronte
    float on = saturate(T * 6) * saturate((1 - T) * 6);
    col += float3(1.0, 0.86, 0.58) * exp(-abs(e) * 55) * 1.5 * on;
    col += float3(1.0, 0.42, 0.08) * exp(-max(e, 0) * 14) * (1 - hole) * 0.5 * on;

    // nei buchi appena aperti l'immagine nuova arriva sovraesposta, poi si assesta
    float fresh = hole * saturate(1 + e * 6);
    col = lerp(col, float3(1.0, 0.96, 0.88), fresh * 0.65 * on);

    // grana della pellicola
    col += (hash(uv * Px + floor(T * 24) * 7.3) - 0.5) * 0.07 * amt;
    return float4(saturate(col), 1);
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
