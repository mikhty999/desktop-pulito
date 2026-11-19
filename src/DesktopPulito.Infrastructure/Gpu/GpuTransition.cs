using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace DesktopPulito.Infrastructure.Gpu;

/// <summary>
/// Transizioni calcolate dalla scheda video (Direct3D 11).
/// Due immagini (prima/dopo) vengono caricate come texture; ogni fotogramma la GPU esegue lo shader
/// e lo presenta sincronizzato al monitor (Present con vsync). Il processore resta quasi fermo.
/// La finestra sta appena sopra il desktop (sotto le tue finestre) ed è visibile ai registratori di schermo.
/// </summary>
static class GpuTransition
{
    [StructLayout(LayoutKind.Sequential)]
    struct Params { public float T, Mode, Aspect, Seed, OX, OY, PxW, PxH; }

    static byte[]? vsCode, psCode;
    static readonly object compileLock = new();

    /// <summary>Compila gli shader in anticipo (in background all'avvio): la prima transizione parte subito.</summary>
    public static void Warmup()
    {
        lock (compileLock)
        {
            if (vsCode != null) return;
            vsCode = Compiler.Compile(Shaders.Source, "VSMain", "transitions.hlsl", "vs_5_0").ToArray();
            psCode = Compiler.Compile(Shaders.Source, "PSMain", "transitions.hlsl", "ps_5_0").ToArray();
        }
    }

    sealed class Surface : NativeWindow, IDisposable
    {
        public Surface(Rectangle r)
        {
            CreateParams cp = new()
            {
                Style = unchecked((int)0x80000000),                     // WS_POPUP
                ExStyle = 0x00000080 | 0x08000000 | 0x00000020,         // TOOLWINDOW | NOACTIVATE | TRANSPARENT
                X = r.X, Y = r.Y, Width = r.Width, Height = r.Height,
            };
            CreateHandle(cp);
        }
        public void Dispose() => DestroyHandle();
    }

    /// <param name="mode">0 bruciatura, 1 pixel, 2 glitch, 3 increspatura, 4 inchiostro</param>
    /// <param name="onFirstFrame">chiamato quando il primo fotogramma è a schermo (per togliere la copertura)</param>
    public static unsafe void Play(Rectangle r, IntPtr insertAfter, IntPtr beforeBits, IntPtr afterBits, int ms, int mode, PointF origin, Action onFirstFrame)
    {
        Warmup();
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1], out ID3D11Device? dev, out ID3D11DeviceContext? ctx).CheckError();
        using ID3D11Device device = dev!;
        using ID3D11DeviceContext context = ctx!;
        using IDXGIFactory2 factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
        using Surface win = new(r);

        SwapChainDescription1 sd = new()
        {
            Width = (uint)r.Width, Height = (uint)r.Height, Format = Format.B8G8R8A8_UNorm,
            BufferCount = 2, BufferUsage = Usage.RenderTargetOutput, SampleDescription = new SampleDescription(1, 0),
            Scaling = Scaling.Stretch, SwapEffect = SwapEffect.FlipDiscard, AlphaMode = AlphaMode.Ignore,
        };
        using IDXGISwapChain1 swap = factory.CreateSwapChainForHwnd(device, win.Handle, sd);
        factory.MakeWindowAssociation(win.Handle, WindowAssociationFlags.IgnoreAll);
        using ID3D11Texture2D back = swap.GetBuffer<ID3D11Texture2D>(0);
        using ID3D11RenderTargetView rtv = device.CreateRenderTargetView(back);

        using ID3D11Texture2D ta = Upload(device, beforeBits, r.Width, r.Height);
        using ID3D11Texture2D tb = Upload(device, afterBits, r.Width, r.Height);
        using ID3D11ShaderResourceView sa = device.CreateShaderResourceView(ta);
        using ID3D11ShaderResourceView sb = device.CreateShaderResourceView(tb);
        using ID3D11VertexShader vs = device.CreateVertexShader(vsCode!);
        using ID3D11PixelShader ps = device.CreatePixelShader(psCode!);
        using ID3D11SamplerState samp = device.CreateSamplerState(SamplerDescription.LinearClamp);
        using ID3D11Buffer cb = device.CreateBuffer(new BufferDescription((uint)sizeof(Params), BindFlags.ConstantBuffer));

        context.OMSetRenderTargets(rtv);
        context.RSSetViewport(0, 0, r.Width, r.Height);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(vs);
        context.PSSetShader(ps);
        context.PSSetShaderResources(0, [sa, sb]);
        context.PSSetSampler(0, samp);
        context.PSSetConstantBuffer(0, cb);

        Params p = new()
        {
            Mode = mode, Aspect = r.Width / (float)Math.Max(1, r.Height), Seed = (float)(Random.Shared.NextDouble() * 50),
            OX = origin.X, OY = origin.Y, PxW = r.Width, PxH = r.Height,
        };

        void Frame(float t)
        {
            p.T = t;
            context.UpdateSubresource(in p, cb);
            context.Draw(3, 0);
            swap.Present(1, PresentFlags.None);   // vsync: ritmo perfetto, nessun fotogramma sprecato
        }

        Frame(0);
        SetWindowPos(win.Handle, insertAfter, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);   // NOSIZE|NOMOVE|NOACTIVATE|SHOWWINDOW
        Frame(0);
        onFirstFrame();

        Stopwatch sw = Stopwatch.StartNew();
        while (true)
        {
            double t = Math.Min(1, sw.Elapsed.TotalMilliseconds / ms);
            Frame((float)(t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2));   // dolce in entrata e in uscita
            if (t >= 1) break;
        }
        context.ClearState();
        context.Flush();
    }

    static ID3D11Texture2D Upload(ID3D11Device device, IntPtr bits, int w, int h)
    {
        Texture2DDescription d = new()
        {
            Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Immutable, BindFlags = BindFlags.ShaderResource,
        };
        return device.CreateTexture2D(d, [new SubresourceData(bits, (uint)(w * 4))]);
    }

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
}
