using System.IO;
using UnityEditor;
using UnityEngine;

namespace Project.Samples.Dodgeball.Editor
{
    /// <summary>
    /// Paints the sample's sprites from arithmetic and saves them as PNGs beside its data: shaded
    /// discs for players, heads, hands and balls, a wooden court with each side's throwing zone in
    /// its team's colour, a reticle, a dash, a rounded panel for the interface and a vignette.
    /// Nothing is downloaded or licensed, and rebuilding the scene repaints them from the rules, so
    /// a longer court gets a longer floor.
    /// </summary>
    internal static class DodgeballArt
    {
        public sealed class Set
        {
            public Sprite Disc, Soft, Ring, Ball, Torso, Head, Hand, Reticle, Panel, Vignette, Floor;
            public Material Sprite, SoftParticle, Dash;
            public DodgeballIcons.Set Icons;
        }

        public static readonly Color HumanColor = new Color(0.27f, 0.56f, 1f);
        public static readonly Color MachineColor = new Color(1f, 0.33f, 0.29f);

        private static string Folder => DodgeballPaths.Data + "/Art";

        /// <summary>Floor resolution in pixels per metre.</summary>
        private const int FloorPpm = 64;

        // Light from the top left, a little in front of the court.
        private static readonly Vector3 Light = new Vector3(-0.45f, 0.55f, 0.7f).normalized;

        public static Set Build(DodgeballRules rules)
        {
            EnsureFolder(Folder);
            Set s = new Set
            {
                Disc = Save("Disc", Disc(128), 128),
                Soft = Save("Soft", Soft(128), 128),
                Ring = Save("Ring", Ring(256), 256),
                Ball = Save("Ball", Ball(128), 128),
                Torso = Save("Torso", Torso(128), 128),
                Head = Save("Head", Head(128), 128),
                Hand = Save("Hand", Hand(64), 64),
                Reticle = Save("Reticle", Reticle(128), 128),
                Panel = Save("Panel", Panel(96, 28), 100, new Vector4(32, 32, 32, 32)),
                Vignette = Save("Vignette", Vignette(256), 256),
                Floor = Save("Floor", Floor(rules), FloorPpm)
            };
            s.Icons = DodgeballIcons.Build();
            Texture2D dash = SaveTexture("Dash", Dash(64, 16));

            s.Sprite = SaveMaterial("Dodgeball_Sprite", null);
            s.SoftParticle = SaveMaterial("Dodgeball_Soft", s.Soft.texture);
            s.Dash = SaveMaterial("Dodgeball_Dash", dash);
            return s;
        }

        // ---------------------------------------------------------------- shapes

        private static Texture2D Disc(int n) => Paint(n, n, (x, y) =>
        {
            float a = Cover(1f - Len(x, y), n);
            return new Color(1f, 1f, 1f, a);
        });

        private static Texture2D Soft(int n) => Paint(n, n, (x, y) =>
        {
            float d = Mathf.Clamp01(Len(x, y));
            float a = Mathf.Pow(1f - d, 1.6f);
            return new Color(1f, 1f, 1f, a);
        });

        private static Texture2D Ring(int n) => Paint(n, n, (x, y) =>
        {
            float d = Len(x, y);
            float a = Cover(0.06f - Mathf.Abs(d - 0.9f), n);
            return new Color(1f, 1f, 1f, a);
        });

        private static Texture2D Ball(int n) => Paint(n, n, (x, y) =>
        {
            float d = Len(x, y) / 0.94f;
            float a = Cover(1f - d, n * 0.94f);
            if (a <= 0f) return Color.clear;
            Vector3 normal = Sphere(x / 0.94f, y / 0.94f);
            Color baseColor = new Color(1f, 0.74f, 0.2f);
            // Two curved seams, like a tennis ball's.
            float seamA = Mathf.Abs(Len(x - 1.25f, y) - 0.95f);
            float seamB = Mathf.Abs(Len(x + 1.25f, y) - 0.95f);
            float seam = Mathf.Clamp01(1f - (Mathf.Min(seamA, seamB) - 0.025f) * n * 0.5f);
            baseColor = Color.Lerp(baseColor, new Color(0.82f, 0.36f, 0.1f), seam);
            Color c = Shade(baseColor, normal, 0.35f, 0.5f);
            c = Outline(c, d, 0.07f, 0.55f);
            c.a = a;
            return c;
        });

        private static Texture2D Torso(int n) => Paint(n, n, (x, y) =>
        {
            // Shoulders from above: wide across, shallow front to back. White, so the renderer's
            // colour tints it while the shading stays.
            const float A = 0.96f, B = 0.6f;
            float e = Mathf.Sqrt((x / A) * (x / A) + (y / B) * (y / B));
            float a = Cover((1f - e) * B, n);
            if (a <= 0f) return Color.clear;
            Vector3 normal = Sphere(x / A, y / B * 0.8f);
            Color c = Shade(Color.white, normal, 0.45f, 0.25f);
            // A collar at the front.
            float collar = Cover(0.05f - Mathf.Abs(Len(x, (y - 0.12f) * 1.6f) - 0.42f), n) * (y > 0.12f ? 1f : 0f);
            c = Color.Lerp(c, new Color(0.25f, 0.25f, 0.28f), collar * 0.5f);
            c = Outline(c, e, 0.09f, 0.45f);
            c.a = a;
            return c;
        });

        private static Texture2D Head(int n) => Paint(n, n, (x, y) =>
        {
            float d = Len(x, y) / 0.9f;
            float a = Cover(1f - d, n * 0.9f);
            if (a <= 0f) return Color.clear;
            Vector3 normal = Sphere(x / 0.9f, y / 0.9f);
            // Hair on top; the face shows at the front edge, which is what tells you which way a
            // player faces from above.
            Color hair = new Color(0.2f, 0.15f, 0.12f);
            Color skin = new Color(0.96f, 0.78f, 0.62f);
            float face = Mathf.Clamp01((normal.y - 0.55f) * 8f);
            Color c = Shade(Color.Lerp(hair, skin, face), normal, 0.4f, 0.35f);
            c = Outline(c, d, 0.08f, 0.5f);
            c.a = a;
            return c;
        });

        private static Texture2D Hand(int n) => Paint(n, n, (x, y) =>
        {
            float d = Len(x, y) / 0.86f;
            float a = Cover(1f - d, n * 0.86f);
            if (a <= 0f) return Color.clear;
            Color c = Shade(new Color(0.96f, 0.78f, 0.62f), Sphere(x / 0.86f, y / 0.86f), 0.4f, 0.2f);
            c = Outline(c, d, 0.16f, 0.5f);
            c.a = a;
            return c;
        });

        private static Texture2D Reticle(int n) => Paint(n, n, (x, y) =>
        {
            float d = Len(x, y);
            float ring = Cover(0.05f - Mathf.Abs(d - 0.72f), n);
            float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
            float tick = (Cover(0.045f - Mathf.Min(ax, ay), n)) * (d > 0.5f && d < 0.98f ? 1f : 0f);
            float dot = Cover(0.08f - d, n);
            return new Color(1f, 1f, 1f, Mathf.Max(ring, Mathf.Max(tick, dot)));
        });

        private static Texture2D Dash(int w, int h)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float a = Mathf.Clamp01(Mathf.Min(u - 0.08f, 0.55f - u) * w * 0.5f);
                px[y * w + x] = new Color(1f, 1f, 1f, a);
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        private static Texture2D Panel(int n, float radius)
        {
            float half = n * 0.5f;
            return Paint(n, n, (x, y) =>
            {
                // Signed distance to a rounded square, in pixels.
                float px = Mathf.Abs(x) * half - (half - radius - 1f);
                float py = Mathf.Abs(y) * half - (half - radius - 1f);
                float outside = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                return new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - outside));
            });
        }

        private static Texture2D Vignette(int n) => Paint(n, n, (x, y) =>
        {
            float d = Len(x, y);
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.3f, d)) * 0.7f;
            return new Color(0f, 0f, 0f, a);
        });

        /// <summary>
        /// The court: maple boards running the length of it, each side's throwing zone in its team's
        /// colour so a player can see where throws are allowed, white lines, and a pool of light in
        /// the middle.
        /// </summary>
        private static Texture2D Floor(DodgeballRules r)
        {
            int w = Mathf.CeilToInt(r.courtLength * FloorPpm), h = Mathf.CeilToInt(r.courtWidth * FloorPpm);
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[w * h];
            float px1 = 1f / FloorPpm;
            const float Plank = 0.28f, Board = 2.2f;
            Color maple = new Color(0.82f, 0.64f, 0.44f);

            for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                float x = (i + 0.5f) * px1 - r.HalfLength;
                float y = (j + 0.5f) * px1 - r.HalfWidth;

                int plank = Mathf.FloorToInt((y + r.HalfWidth) / Plank);
                float offset = Hash(plank, 7) * Board;
                int board = Mathf.FloorToInt((x + r.HalfLength + offset) / Board);
                float tone = 0.93f + 0.09f * Hash(plank, board);
                float grain = 0.035f * Mathf.Sin(x * 30f + Mathf.PerlinNoise(x * 0.6f + plank * 3.1f, plank * 1.7f) * 9f)
                              + 0.03f * (Mathf.PerlinNoise(x * 4f, y * 40f) - 0.5f);
                // Each side's throwing zone is painted in its team's colour, the wood still showing
                // through; blended over the bare maple instead, blue turns a muddy grey.
                float depth = Mathf.Abs(x);
                Color albedo = depth >= r.throwingLine
                    ? Color.Lerp(maple, (x < 0f ? HumanColor : MachineColor) * 0.62f, 0.72f)
                    : maple;
                Color c = albedo * (tone + grain);

                float inPlank = Mathf.Repeat(y + r.HalfWidth, Plank);
                if (inPlank < px1 * 1.2f) c *= 0.72f;
                float inBoard = Mathf.Repeat(x + r.HalfLength + offset, Board);
                if (inBoard < px1 * 1.2f) c *= 0.78f;

                // Light pool.
                float nx = x / r.HalfLength, ny = y / r.HalfWidth;
                c *= 1.1f - 0.28f * (nx * nx * 0.7f + ny * ny);

                // Lines.
                float line = 0f;
                float edge = Mathf.Min(r.HalfLength - Mathf.Abs(x), r.HalfWidth - Mathf.Abs(y));
                line = Mathf.Max(line, Band(edge - 0.1f, 0.05f, px1));
                line = Mathf.Max(line, Band(Mathf.Abs(x), 0.05f, px1));
                line = Mathf.Max(line, Band(Mathf.Abs(Len(x, y) - 1.3f), 0.035f, px1));
                line = Mathf.Max(line, Band(Mathf.Abs(Len(x, y) - 0.25f), 0.03f, px1));
                c = Color.Lerp(c, new Color(0.97f, 0.97f, 0.95f), line * 0.9f);

                c.a = 1f;
                px[j * w + i] = c;
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // ---------------------------------------------------------------- painting helpers

        private delegate Color Pixel(float x, float y);

        /// <summary>Paints a texture; <paramref name="pixel"/> gets coordinates from -1 to 1, y up.</summary>
        private static Texture2D Paint(int w, int h, Pixel pixel)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[w * h];
            for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
                px[j * w + i] = pixel((i + 0.5f) / w * 2f - 1f, (j + 0.5f) / h * 2f - 1f);
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        private static float Len(float x, float y) => Mathf.Sqrt(x * x + y * y);

        /// <summary>Anti-aliased coverage of a shape whose signed inside distance is <paramref name="inside"/>, in units where the texture spans 2.</summary>
        private static float Cover(float inside, float pixels) => Mathf.Clamp01(inside * pixels * 0.5f + 0.5f);

        /// <summary>Coverage of a line of half width <paramref name="half"/> at distance <paramref name="d"/>, in metres.</summary>
        private static float Band(float d, float half, float pixel) => Mathf.Clamp01((half - d) / pixel + 0.5f);

        private static Vector3 Sphere(float x, float y)
        {
            float z2 = 1f - x * x - y * y;
            return new Vector3(x, y, Mathf.Sqrt(Mathf.Max(0f, z2)));
        }

        private static Color Shade(Color albedo, Vector3 normal, float ambient, float specular)
        {
            float diffuse = Mathf.Max(0f, Vector3.Dot(normal, Light));
            Vector3 half = (Light + Vector3.forward).normalized;
            float spec = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(normal, half)), 30f) * specular;
            Color c = albedo * (ambient + (1f - ambient) * diffuse) + Color.white * spec;
            c.a = 1f;
            return c;
        }

        /// <summary>Darkens the rim of a shape: <paramref name="d"/> is 1 at its edge.</summary>
        private static Color Outline(Color c, float d, float width, float darkness)
        {
            float rim = Mathf.Clamp01((d - (1f - width)) / width);
            rim = rim * rim;
            return Color.Lerp(c, c * darkness, Mathf.SmoothStep(0f, 1f, rim * 2f));
        }

        private static float Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)(a * 374761393 + b * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        // ---------------------------------------------------------------- saving

        internal static Sprite Save(string name, Texture2D texture, float pixelsPerUnit, Vector4 border = default)
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

        private static Texture2D SaveTexture(string name, Texture2D texture)
        {
            string path = Folder + "/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// The legacy sprite shader, because it reads vertex colour on any renderer (sprites, lines,
        /// trails, particles) and draws unlit in every render pipeline, which the URP sprite shaders
        /// do not promise for lines and particles.
        /// </summary>
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
