using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Project.Samples.ArenaBrawler.Editor
{
    /// <summary>
    /// The fight's interface sprites and effect textures, painted from signed distance functions
    /// and saved as PNGs under the sample's Data/UI folder: a fist for the jab, a swung fist for the
    /// hook, a shield, a roll, arrows, stars for rounds won, a burst, keycaps and a mouse for the
    /// controls, plates for the health bars and banners, and the bar a seven-segment digit is made
    /// of. All white, so the interface tints them. Kept in this sample rather than shared with
    /// Dodgeball's, because samples share nothing.
    /// </summary>
    internal static class BrawlerIcons
    {
        public sealed class Set
        {
            public Sprite Fist, Hook, Shield, Roll, Arrow, Stand, Burst, Star, Pip, Segment, Plate, Bar, Bubble,
                          Key, MouseLeft, MouseRight, Restart, Move, Clock, Disc, Ring, Soft, Vignette;
            public Material Particle;
        }

        private static string Folder => ArenaBrawlerPaths.Data + "/UI";

        public static Set Build()
        {
            EnsureFolder(Folder);
            Set s = new Set
            {
                Fist = Save("IconFist", Icon(128, Fist), 128),
                Hook = Save("IconHook", Icon(128, Hook), 128),
                Shield = Save("IconShield", Icon(128, Shield), 128),
                Roll = Save("IconRoll", Icon(128, Roll), 128),
                Arrow = Save("IconArrow", Icon(128, Arrow), 128),
                Stand = Save("IconStand", Icon(128, Stand), 128),
                Burst = Save("IconBurst", Icon(256, Burst), 256),
                Star = Save("IconStar", Icon(128, Star), 128),
                Pip = Save("IconPip", Icon(64, (x, y) => Len(x, y) - 0.8f), 64),
                Restart = Save("IconRestart", Icon(128, Restart), 128),
                Move = Save("IconMove", Icon(128, MoveCross), 128),
                Clock = Save("IconClock", Icon(128, Clock), 128),
                Disc = Save("Disc", Icon(128, (x, y) => Len(x, y) - 0.96f), 128),
                Ring = Save("Ring", Icon(256, (x, y) => Mathf.Abs(Len(x, y) - 0.9f) - 0.06f), 256),
                Soft = Save("Soft", Paint(128, 128, (x, y) => new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - Len(x, y)), 1.6f))), 128),
                Vignette = Save("Vignette", Paint(256, 256, (x, y) =>
                    new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1.35f, Len(x, y))) * 0.75f)), 256),
                Segment = Save("SevenSegment", Paint(128, 32, Segment), 128),
                Plate = Save("Plate", Paint(512, 128, Plate), 512),
                Bar = Save("Bar", Paint(512, 64, Bar), 512),
                Bubble = Save("Bubble", Paint(128, 128, Bubble), 128),
                Key = Save("Keycap", Paint(96, 96, Keycap), 100, new Vector4(30, 34, 30, 30)),
                MouseLeft = Save("MouseLeft", Paint(96, 128, (x, y) => Mouse(x, y, -1)), 128),
                MouseRight = Save("MouseRight", Paint(96, 128, (x, y) => Mouse(x, y, 1)), 128),
            };
            s.Particle = SaveMaterial("Brawler_Particle", s.Soft.texture);
            return s;
        }

        // ---------------------------------------------------------------- icons, as distances
        // Each returns the signed distance to the shape in units where the texture spans -1 to 1
        // across its width: negative inside.

        private static float Fist(float x, float y)
        {
            // A fist seen from the front: knuckles on top, the thumb folded across the bottom.
            float hand = RoundBox(x, y - 0.05f, 0.6f, 0.5f, 0.24f);
            float thumb = Capsule(x, y, -0.48f, -0.22f, 0.12f, -0.22f, 0.15f);
            float d = Mathf.Max(hand, -(thumb - 0.06f));
            d = Mathf.Min(d, thumb);
            for (int i = -1; i <= 1; i++)
                d = Mathf.Max(d, -Capsule(x, y, i * 0.28f, 0.12f, i * 0.28f, 0.6f, 0.035f));
            return d;
        }

        private static float Hook(float x, float y)
        {
            // A smaller fist at the end of a swing that sweeps in from the side.
            float fist = Fist((x - 0.32f) / 0.62f, (y - 0.22f) / 0.62f) * 0.62f;
            float angle = Mathf.Atan2(y + 0.35f, x - 0.25f) * Mathf.Rad2Deg;
            float swing = Mathf.Abs(Len(x - 0.25f, y + 0.35f) - 0.78f) - 0.1f;
            if (angle < 100f || angle > 215f) swing = Mathf.Max(swing, 0.2f);
            return Mathf.Min(fist, swing);
        }

        private static float Shield(float x, float y)
        {
            Vector2[] heater = { new Vector2(-0.66f, 0.72f), new Vector2(0.66f, 0.72f), new Vector2(0.66f, 0.06f),
                                 new Vector2(0.3f, -0.55f), new Vector2(0f, -0.84f), new Vector2(-0.3f, -0.55f), new Vector2(-0.66f, 0.06f) };
            float outer = Polygon(x, y, heater);
            // A band inset from the edge, so it reads as a shield rather than a blob.
            float band = Mathf.Abs(outer + 0.16f) - 0.04f;
            return Mathf.Max(outer, -band);
        }

        private static float Roll(float x, float y)
        {
            float d = float.MaxValue;
            for (int k = 0; k < 2; k++)
            {
                float rot = k * 180f;
                float a = Mathf.Repeat(Mathf.Atan2(y, x) * Mathf.Rad2Deg - rot, 360f);
                float arc = Mathf.Abs(Len(x, y) - 0.62f) - 0.11f;
                if (a < 20f || a > 150f) arc = Mathf.Max(arc, 0.2f);
                d = Mathf.Min(d, arc);
                float ta = (rot + 20f) * Mathf.Deg2Rad;
                Vector2 tip = new Vector2(Mathf.Cos(ta), Mathf.Sin(ta)) * 0.62f;
                Vector2 tangent = new Vector2(Mathf.Sin(ta), -Mathf.Cos(ta));
                Vector2 normal = new Vector2(Mathf.Cos(ta), Mathf.Sin(ta));
                d = Mathf.Min(d, Polygon(x, y, new[] { tip + tangent * 0.3f, tip - tangent * 0.08f + normal * 0.26f, tip - tangent * 0.08f - normal * 0.26f }));
            }
            return d;
        }

        private static float Arrow(float x, float y)
        {
            float d = Capsule(x, y, -0.62f, 0f, 0.5f, 0f, 0.13f);
            d = Mathf.Min(d, Capsule(x, y, 0.62f, 0f, 0.14f, 0.46f, 0.13f));
            return Mathf.Min(d, Capsule(x, y, 0.62f, 0f, 0.14f, -0.46f, 0.13f));
        }

        private static float Stand(float x, float y) => Mathf.Min(Mathf.Abs(Len(x, y) - 0.62f) - 0.1f, Len(x, y) - 0.24f);

        private static readonly Vector2[] BurstPoints = StarPolygon(12, 0.98f, 0.6f, 0.9f);
        private static readonly Vector2[] StarPoints = StarPolygon(5, 0.95f, 0.42f, 1f);

        private static float Burst(float x, float y) => Polygon(x, y, BurstPoints);
        private static float Star(float x, float y) => Polygon(x, y + 0.06f, StarPoints);

        private static Vector2[] StarPolygon(int points, float outer, float inner, float alternate)
        {
            Vector2[] p = new Vector2[points * 2];
            for (int i = 0; i < p.Length; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / points;
                float r = (i % 2 == 0 ? outer : inner) * (i % 4 == 0 ? 1f : alternate);
                p[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            return p;
        }

        private static float Clock(float x, float y)
        {
            float ring = Mathf.Abs(Len(x, y) - 0.76f) - 0.09f;
            float hands = Mathf.Min(Capsule(x, y, 0f, 0f, 0f, 0.48f, 0.07f), Capsule(x, y, 0f, 0f, 0.34f, -0.1f, 0.07f));
            return Mathf.Min(ring, hands);
        }

        private static float Restart(float x, float y)
        {
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
                float px = x * c + y * s, py = -x * s + y * c;
                d = Mathf.Min(d, Capsule(px, py, 0.82f, 0f, 0.5f, 0.3f, 0.1f));
                d = Mathf.Min(d, Capsule(px, py, 0.82f, 0f, 0.5f, -0.3f, 0.1f));
            }
            return d;
        }

        // ---------------------------------------------------------------- shapes with a rim

        private static Color Segment(float x, float y)
        {
            Vector2[] hex = { new Vector2(-0.94f, 0f), new Vector2(-0.72f, 0.2f), new Vector2(0.72f, 0.2f),
                              new Vector2(0.94f, 0f), new Vector2(0.72f, -0.2f), new Vector2(-0.72f, -0.2f) };
            return Fill(Polygon(x, y, hex), 128, 1f, 1f);
        }

        private static Color Plate(float x, float y)
        {
            Vector2[] trap = { new Vector2(-0.99f, 0.24f), new Vector2(0.99f, 0.24f), new Vector2(0.86f, -0.24f), new Vector2(-0.86f, -0.24f) };
            return Fill(Polygon(x, y, trap), 512, 0.7f, 1f, 0.012f);
        }

        private static Color Bar(float x, float y)
        {
            // A slanted bar, 8:1; y spans -0.125 to 0.125.
            Vector2[] par = { new Vector2(-0.96f, 0.11f), new Vector2(0.99f, 0.11f), new Vector2(0.96f, -0.11f), new Vector2(-0.99f, -0.11f) };
            return Fill(Polygon(x, y, par), 512, 1f, 1f);
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
            float top = RoundBox(x, y - 0.1f, 0.78f, 0.74f, 0.24f);
            float grey = top < 0f ? 0.95f : 0.55f;
            float a = Mathf.Clamp01(0.5f - outer * 48f);
            return new Color(grey, grey, grey, a);
        }

        private static Color Mouse(float x, float y, int lit)
        {
            float body = RoundBox(x, y, 0.78f, 1.18f, 0.7f);
            float outline = Mathf.Abs(body + 0.06f) - 0.06f;
            float split = Mathf.Min(Capsule(x, y, 0f, 0.1f, 0f, 1.2f, 0.04f), Capsule(x, y, -0.8f, 0.1f, 0.8f, 0.1f, 0.04f));
            float button = Mathf.Max(Mathf.Max(body + 0.2f, 0.18f - y), lit < 0 ? x + 0.1f : 0.1f - x);
            float d = Mathf.Min(Mathf.Min(outline, split), button);
            return new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d * 48f));
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

        private static Color Fill(float d, int pixels, float face, float rim, float rimWidth = 0f)
        {
            float a = Mathf.Clamp01(0.5f - d * pixels * 0.5f);
            if (a <= 0f) return Color.clear;
            float grey = rimWidth > 0f && d > -rimWidth ? rim : face;
            return new Color(grey, grey, grey, a);
        }

        // ---------------------------------------------------------------- painting and saving

        private static Texture2D Icon(int n, Func<float, float, float> sdf) =>
            Paint(n, n, (x, y) => new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - sdf(x, y) * n * 0.5f)));

        /// <summary>Paints a texture; x runs from -1 to 1 and y is scaled the same, so a wide texture spans less in y.</summary>
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

        private static Sprite Save(string name, Texture2D texture, float pixelsPerUnit, Vector4 border = default)
        {
            string path = Folder + "/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.spriteBorder = border;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>The legacy sprite shader: reads vertex colour on particles and draws unlit in any pipeline.</summary>
        private static Material SaveMaterial(string name, Texture texture)
        {
            string path = Folder + "/" + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Shader.Find("Sprites/Default");
            mat.mainTexture = texture;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
