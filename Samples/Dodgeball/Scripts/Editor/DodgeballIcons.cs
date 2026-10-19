using System;
using UnityEngine;

namespace Project.Samples.Dodgeball.Editor
{
    /// <summary>
    /// The interface's icons and shapes, painted from signed distance functions: a hand for the
    /// catch, chevrons for the dodge, a clock, a no-throw sign, arrows, a burst, keycaps and a
    /// mouse for the controls, the plates the score and the banners sit on, and the bar a
    /// seven-segment digit is made of. All white, so the interface tints them; where a shape has
    /// an edge, the edge is brighter than its face, so a tint gives it a rim.
    /// </summary>
    internal static class DodgeballIcons
    {
        public sealed class Set
        {
            public Sprite Glove, Dash, Clock, NoThrow, Arrow, Stand, Burst, Diamond, Segment, Plate, Banner,
                          Bubble, Key, MouseLeft, MouseRight, Restart, Move, Pip;
        }

        public static Set Build()
        {
            return new Set
            {
                Glove = DodgeballArt.Save("IconGlove", Icon(128, Glove), 128),
                Dash = DodgeballArt.Save("IconDash", Icon(128, Dash), 128),
                Clock = DodgeballArt.Save("IconClock", Icon(128, Clock), 128),
                NoThrow = DodgeballArt.Save("IconNoThrow", Icon(128, NoThrow), 128),
                Arrow = DodgeballArt.Save("IconArrow", Icon(128, Arrow), 128),
                Stand = DodgeballArt.Save("IconStand", Icon(128, Stand), 128),
                Burst = DodgeballArt.Save("IconBurst", Icon(256, Burst), 256),
                Diamond = DodgeballArt.Save("IconDiamond", Icon(64, Diamond), 64),
                Pip = DodgeballArt.Save("IconPip", Icon(64, Pip), 64),
                Restart = DodgeballArt.Save("IconRestart", Icon(128, Restart), 128),
                Move = DodgeballArt.Save("IconMove", Icon(128, MoveCross), 128),
                Segment = DodgeballArt.Save("SevenSegment", Paint(128, 32, Segment), 128),
                Plate = DodgeballArt.Save("Plate", Paint(512, 128, Plate), 512),
                Banner = DodgeballArt.Save("Banner", Paint(512, 128, Banner), 512),
                Bubble = DodgeballArt.Save("Bubble", Paint(128, 128, Bubble), 128),
                Key = DodgeballArt.Save("Keycap", Paint(96, 96, Keycap), 100, new Vector4(30, 34, 30, 30)),
                MouseLeft = DodgeballArt.Save("MouseLeft", Paint(96, 128, (x, y) => Mouse(x, y, -1)), 128),
                MouseRight = DodgeballArt.Save("MouseRight", Paint(96, 128, (x, y) => Mouse(x, y, 1)), 128),
            };
        }

        // ---------------------------------------------------------------- icons, as distances
        // Each returns the signed distance to the shape in units where the texture spans -1 to 1
        // across its width: negative inside.

        private static float Glove(float x, float y)
        {
            float d = RoundBox(x, y + 0.2f, 0.4f, 0.36f, 0.16f);
            d = Mathf.Min(d, Capsule(x, y, -0.3f, 0.05f, -0.32f, 0.58f, 0.11f));
            d = Mathf.Min(d, Capsule(x, y, -0.1f, 0.1f, -0.1f, 0.78f, 0.11f));
            d = Mathf.Min(d, Capsule(x, y, 0.1f, 0.1f, 0.12f, 0.74f, 0.11f));
            d = Mathf.Min(d, Capsule(x, y, 0.3f, 0.05f, 0.34f, 0.56f, 0.1f));
            d = Mathf.Min(d, Capsule(x, y, -0.38f, -0.25f, -0.76f, 0.12f, 0.11f));
            return d;
        }

        private static float Dash(float x, float y)
        {
            float a = Chevron(x + 0.22f, y), b = Chevron(x - 0.28f, y);
            return Mathf.Min(a, b);
        }

        private static float Chevron(float x, float y)
        {
            return Mathf.Min(Capsule(x, y, -0.25f, 0.5f, 0.2f, 0f, 0.12f), Capsule(x, y, 0.2f, 0f, -0.25f, -0.5f, 0.12f));
        }

        private static float Clock(float x, float y)
        {
            float ring = Mathf.Abs(Len(x, y) - 0.76f) - 0.09f;
            float hands = Mathf.Min(Capsule(x, y, 0f, 0f, 0f, 0.48f, 0.07f), Capsule(x, y, 0f, 0f, 0.34f, -0.1f, 0.07f));
            return Mathf.Min(ring, hands);
        }

        private static float NoThrow(float x, float y)
        {
            float ring = Mathf.Abs(Len(x, y) - 0.8f) - 0.12f;
            float slash = Capsule(x, y, -0.56f, 0.56f, 0.56f, -0.56f, 0.12f);
            return Mathf.Min(ring, slash);
        }

        private static float Arrow(float x, float y)
        {
            float d = Capsule(x, y, -0.62f, 0f, 0.5f, 0f, 0.13f);
            d = Mathf.Min(d, Capsule(x, y, 0.62f, 0f, 0.14f, 0.46f, 0.13f));
            return Mathf.Min(d, Capsule(x, y, 0.62f, 0f, 0.14f, -0.46f, 0.13f));
        }

        private static float Stand(float x, float y)
        {
            float ring = Mathf.Abs(Len(x, y) - 0.62f) - 0.1f;
            return Mathf.Min(ring, Len(x, y) - 0.24f);
        }

        private static readonly Vector2[] StarPoints = StarPolygon(12, 0.98f, 0.6f);

        private static float Burst(float x, float y) => Polygon(x, y, StarPoints);

        private static Vector2[] StarPolygon(int points, float outer, float inner)
        {
            Vector2[] p = new Vector2[points * 2];
            for (int i = 0; i < p.Length; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / points;
                float r = (i % 2 == 0 ? outer : inner) * (i % 4 == 0 ? 1f : 0.9f);
                p[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            return p;
        }

        private static float Diamond(float x, float y) => (Mathf.Abs(x) + Mathf.Abs(y)) * 0.7071f - 0.62f;

        private static float Pip(float x, float y) => Len(x, y) - 0.8f;

        private static float Restart(float x, float y)
        {
            // An arc most of the way round, with a head at its end.
            float angle = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
            float ring = Mathf.Abs(Len(x, y) - 0.6f) - 0.12f;
            if (angle > 50f && angle < 110f) ring = Mathf.Max(ring, 0.2f);
            Vector2 tip = new Vector2(Mathf.Cos(50f * Mathf.Deg2Rad), Mathf.Sin(50f * Mathf.Deg2Rad)) * 0.6f;
            float head = Polygon(x, y, new[] { tip + new Vector2(-0.38f, 0.1f), tip + new Vector2(0.18f, 0.32f), tip + new Vector2(0.12f, -0.28f) });
            return Mathf.Min(ring, head);
        }

        private static float MoveCross(float x, float y)
        {
            float d = Len(x, y) - 0.16f;
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                // Rotate the point into the arrow's frame: the arrow points along +x.
                float px = x * c + y * s, py = -x * s + y * c;
                d = Mathf.Min(d, Capsule(px, py, 0.82f, 0f, 0.5f, 0.3f, 0.1f));
                d = Mathf.Min(d, Capsule(px, py, 0.82f, 0f, 0.5f, -0.3f, 0.1f));
            }
            return d;
        }

        // ---------------------------------------------------------------- shapes with a rim
        // These return a grey level as well as coverage: 1 at the rim, lower on the face.

        private static Color Segment(float x, float y)
        {
            // y spans -0.25 to 0.25 here.
            Vector2[] hex = { new Vector2(-0.94f, 0f), new Vector2(-0.72f, 0.2f), new Vector2(0.72f, 0.2f),
                              new Vector2(0.94f, 0f), new Vector2(0.72f, -0.2f), new Vector2(-0.72f, -0.2f) };
            return Fill(Polygon(x, y, hex), 128, 1f, 1f);
        }

        private static Color Plate(float x, float y)
        {
            Vector2[] trap = { new Vector2(-0.99f, 0.24f), new Vector2(0.99f, 0.24f), new Vector2(0.86f, -0.24f), new Vector2(-0.86f, -0.24f) };
            return Fill(Polygon(x, y, trap), 512, 0.7f, 1f, 0.012f);
        }

        private static Color Banner(float x, float y)
        {
            Vector2[] par = { new Vector2(-0.9f, 0.24f), new Vector2(0.99f, 0.24f), new Vector2(0.9f, -0.24f), new Vector2(-0.99f, -0.24f) };
            return Fill(Polygon(x, y, par), 512, 0.8f, 1f, 0.014f);
        }

        private static Color Bubble(float x, float y)
        {
            float box = RoundBox(x, y - 0.12f, 0.82f, 0.66f, 0.34f);
            float tail = Polygon(x, y, new[] { new Vector2(-0.2f, -0.4f), new Vector2(0.2f, -0.4f), new Vector2(0f, -0.94f) });
            return Fill(Mathf.Min(box, tail), 128, 0.85f, 1f, 0.08f);
        }

        private static Color Keycap(float x, float y)
        {
            float outer = RoundBox(x, y, 0.9f, 0.9f, 0.32f);
            if (outer > 0.03f) return Color.clear;
            // A lighter top face over a darker lip, like a key seen from the front.
            float top = RoundBox(x, y - 0.1f, 0.78f, 0.74f, 0.24f);
            float grey = top < 0f ? 0.95f : 0.55f;
            float a = Mathf.Clamp01(0.5f - outer * 48f);
            return new Color(grey, grey, grey, a);
        }

        private static Color Mouse(float x, float y, int lit)
        {
            // y spans -1.33 to 1.33 here.
            float body = RoundBox(x, y, 0.78f, 1.18f, 0.7f);
            float outline = Mathf.Abs(body + 0.06f) - 0.06f;
            float split = Mathf.Min(Capsule(x, y, 0f, 0.1f, 0f, 1.2f, 0.04f), Capsule(x, y, -0.8f, 0.1f, 0.8f, 0.1f, 0.04f));
            // The lit button: the top half of the body on one side of the split, inset from the outline.
            float button = Mathf.Max(Mathf.Max(body + 0.2f, 0.18f - y), lit < 0 ? x + 0.1f : 0.1f - x);
            float d = Mathf.Min(Mathf.Min(outline, split), button);
            float a = Mathf.Clamp01(0.5f - d * 48f);
            return new Color(1f, 1f, 1f, a);
        }

        // ---------------------------------------------------------------- distance functions

        private static float Len(float x, float y) => Mathf.Sqrt(x * x + y * y);

        private static float Capsule(float x, float y, float ax, float ay, float bx, float by, float r)
        {
            Vector2 p = new Vector2(x - ax, y - ay), ab = new Vector2(bx - ax, by - ay);
            float t = Mathf.Clamp01(Vector2.Dot(p, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (p - ab * t).magnitude - r;
        }

        private static float RoundBox(float x, float y, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(x) - hx + r, qy = Mathf.Abs(y) - hy + r;
            return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>Signed distance to a simple polygon, convex or not.</summary>
        private static float Polygon(float x, float y, Vector2[] v)
        {
            Vector2 p = new Vector2(x, y);
            float d = (p - v[0]).sqrMagnitude;
            float sign = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                Vector2 e = v[j] - v[i], w = p - v[i];
                Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / e.sqrMagnitude);
                d = Mathf.Min(d, b.sqrMagnitude);
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) sign = -sign;
            }
            return sign * Mathf.Sqrt(d);
        }

        /// <summary>A filled shape whose face is <paramref name="face"/> grey and whose rim is <paramref name="rim"/>.</summary>
        private static Color Fill(float d, int pixels, float face, float rim, float rimWidth = 0f)
        {
            float a = Mathf.Clamp01(0.5f - d * pixels * 0.5f);
            if (a <= 0f) return Color.clear;
            float grey = rimWidth > 0f && d > -rimWidth ? rim : face;
            return new Color(grey, grey, grey, a);
        }

        // ---------------------------------------------------------------- painting

        private static Texture2D Icon(int n, Func<float, float, float> sdf) =>
            Paint(n, n, (x, y) => new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - sdf(x, y) * n * 0.5f)));

        /// <summary>
        /// Paints a texture; <paramref name="pixel"/> gets x from -1 to 1 and y scaled the same, so
        /// a wide texture spans less than -1 to 1 in y.
        /// </summary>
        private static Texture2D Paint(int w, int h, Func<float, float, Color> pixel)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[w * h];
            float aspect = h / (float)w;
            for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
                px[j * w + i] = pixel((i + 0.5f) / w * 2f - 1f, ((j + 0.5f) / h * 2f - 1f) * aspect);
            t.SetPixels32(px);
            t.Apply();
            return t;
        }
    }
}
