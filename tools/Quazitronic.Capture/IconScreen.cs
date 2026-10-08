using System;
using Microsoft.Xna.Framework;
using Quazitronic.Graphics;
using Quazitronic.Screens;

namespace Quazitronic.Capture;

/// <summary>
/// The app icon, drawn by the game's own renderer: the influence device hovering over a lit deck
/// platform with an energiser glowing beneath it, against deep space.
/// </summary>
internal sealed class IconScreen : Screen
{
    public IconScreen(QuazitronicGame game) : base(game) { }

    /// <summary>True for the wide artwork (store banners): droids either side as well.</summary>
    public bool Wide { get; init; }

    /// <summary>Where to save the ORICTRON logo as a PNG with straight alpha (for the store art), once.</summary>
    public string? LogoPath { get; set; }

    public override void Update(float dt) { }

    public override void Draw(Gfx g)
    {
        if (LogoPath != null)
        {
            var tex = g.Logo;
            var px = new Color[tex.Width * tex.Height];
            tex.GetData(px);
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.A > 0 && c.A < 255) px[i] = new Color(Math.Min(255, c.R * 255 / c.A), Math.Min(255, c.G * 255 / c.A), Math.Min(255, c.B * 255 / c.A), (int)c.A);
            }
            using var copy = new Microsoft.Xna.Framework.Graphics.Texture2D(g.Device, tex.Width, tex.Height);
            copy.SetData(px);
            using var f = System.IO.File.Create(LogoPath);
            copy.SaveAsPng(f, tex.Width, tex.Height);
            LogoPath = null;
        }
        float w = g.Width, h = g.Height;
        g.Begin();
        g.Gradient(0, 0, w, h, new Color(6, 10, 40), new Color(40, 10, 70), 48);
        g.Additive();
        g.GlowAt(new Vector2(w * 0.5f, h * 0.42f), h * 0.75f, new Color(60, 120, 255) * 0.35f);
        g.GlowAt(new Vector2(w * 0.2f, h * 0.15f), h * 0.4f, new Color(200, 60, 255) * 0.2f);
        var rng = new Random(5);
        for (int k = 0; k < 90; k++)
            g.GlowAt(new Vector2((float)rng.NextDouble() * w, (float)rng.NextDouble() * h), 0.6f + (float)rng.NextDouble() * 1.4f, Color.White * (0.4f + (float)rng.NextDouble() * 0.6f));
        g.End();

        var iso = g.Iso;
        float zoom = h / 64f;
        iso.SetCamera(IsoRenderer.ToIso(new Vector3(12, 12, 6)), new Vector2(w / 2, h * 0.5f), zoom, w, h);
        iso.Detail = int.Parse(System.Environment.GetEnvironmentVariable("ICON_DETAIL") ?? "4");
        iso.BeginDynamic();
        var metal = new Color(205, 212, 228);
        // a 2 x 2 tile platform with its slab
        for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
            {
                var cell = i == 1 && j == 1 ? AtlasCell.Energiser : AtlasCell.FloorPanel;
                iso.AddBlock(new Vector3(i * 12, j * 12, -12), new Vector3(i * 12 + 12, j * 12 + 12, 0), cell, AtlasCell.Slab, (i + j) % 2 == 0 ? metal : new Color(190, 198, 218));
            }
        var at = new Vector3(15, 15, 0);
        iso.AddFloorRing(at, 11, new Color(60, 240, 255));
        iso.AddShadow(at, 10, 0.7f);
        iso.AddDroid(at + new Vector3(0, 0, 2), 0, 0.8f, scale: 1.3f);
        iso.AddGlow(at + new Vector3(0, 0, 9), 70, new Color(60, 200, 255) * 0.25f);
        iso.Draw(1f);
        iso.Detail = 1;
    }
}
