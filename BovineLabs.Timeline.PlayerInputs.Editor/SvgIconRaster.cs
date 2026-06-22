using System.Collections.Generic;
using System.IO;
using Unity.VectorGraphics;
using UnityEngine;

#if UNITY_EDITOR
namespace BovineLabs.Timeline.PlayerInputs.Editor
{
    /// <summary>
    /// Editor-only helper that rasterises an SVG string to a cached <see cref="Texture2D"/> using Unity 6.3's
    /// built-in Vector Graphics module (SVGParser → TessellateScene → FillMesh → render mesh to a RenderTexture).
    /// We deliberately avoid <c>VectorUtils.BuildSprite</c> + <c>RenderSpriteToTexture2D</c>: on this 6.3 alpha
    /// <c>Sprite.OverrideGeometry</c> rejects the tessellated geometry ("Not allowed to override geometry on
    /// sprite ''"), yielding a blank texture. Drawing the mesh ourselves sidesteps that entirely.
    /// Reusable by any clip editor that wants to show an explanatory vector icon (e.g. on hover).
    /// </summary>
    internal static class SvgIconRaster
    {
        private static readonly Dictionary<string, Texture2D> Cache = new();
        private static Material material;

        /// <summary>Get (or build + cache) a texture for an SVG keyed by a stable id.</summary>
        internal static Texture2D Get(string key, string svg, int size = 256)
        {
            if (Cache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var tex = Rasterize(svg, size);
            Cache[key] = tex;
            return tex;
        }

        internal static Texture2D Rasterize(string svg, int size)
        {
            Mesh mesh = null;
            try
            {
                SVGParser.SceneInfo sceneInfo;
                using (var reader = new StringReader(svg))
                {
                    sceneInfo = SVGParser.ImportSVG(reader);
                }

                var geoms = VectorUtils.TessellateScene(sceneInfo.Scene, new VectorUtils.TessellationOptions
                {
                    StepDistance = 1f,
                    MaxCordDeviation = 0.5f,
                    MaxTanAngleDeviation = 0.1f,
                    SamplingStepSize = 0.01f,
                });

                mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                VectorUtils.FillMesh(mesh, geoms, 1f, true);

                if (material == null)
                {
                    // "Unlit/Vector" is the solid (non-gradient) vector shader; it renders the mesh's vertex colours.
                    var shader = Shader.Find("Unlit/Vector") ?? Shader.Find("Unlit/VectorGradient") ?? Shader.Find("Sprites/Default");
                    material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }

                return RenderMeshToTexture(mesh, size);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SvgIconRaster] Failed to rasterise SVG: {e.Message}");
                return null;
            }
            finally
            {
                if (mesh != null)
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        private static Texture2D RenderMeshToTexture(Mesh mesh, int size)
        {
            var bounds = mesh.bounds;
            var half = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.02f;
            if (half < 1e-4f)
            {
                half = 1f;
            }

            // Render the vector mesh through a throwaway orthographic camera — it gets the matrices and the
            // colour-space conversion right where hand-rolled GL.DrawMeshNow did not.
            var meshGo = new GameObject("~svgMesh") { hideFlags = HideFlags.HideAndDontSave };
            meshGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshGo.AddComponent<MeshRenderer>().sharedMaterial = material;
            meshGo.transform.position = Vector3.zero;

            var camGo = new GameObject("~svgCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = half;
            cam.aspect = 1f;
            cam.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = ~0;

            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
                tex.Apply();
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(meshGo);
            }

            return tex;
        }
    }
}
#endif
