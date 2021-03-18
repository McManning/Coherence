using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace Coherence
{
    /// <summary>
    /// Controller to sync a Unity Camera with a Blender Viewport.
    ///
    /// On render updates, this will provide the SyncManager with
    /// new RGB24 pixel data to feed back to Blender.
    /// </summary>
    [ExecuteAlways]
    public class ViewportController : MonoBehaviour
    {
        /// <summary>
        /// Identifier to match this viewport with a Blender viewport
        /// </summary>
        public int ID
        {
            get { return InteropData.id; }
        }

        public InteropViewport InteropData { get; private set; }

        public int Width
        {
            get { return tex.width; }
        }

        public int Height
        {
            get { return tex.height; }
        }

        Camera cam;
        public Texture2D tex;
        RenderTexture rt;

        public SyncManager Sync { get; set; }

        public void Awake()
        {
            gameObject.tag = "EditorOnly";
            gameObject.hideFlags = HideFlags.DontSave;
        }

        private void OnEnable()
        {
            cam = GetComponent<Camera>();

            if (cam == null)
            {
                cam = gameObject.AddComponent<Camera>();
            }
        }

        private void OnDisable()
        {
            if (tex != null)
            {
                DestroyImmediate(tex);
                tex = null;
            }

            if (rt != null)
            {
                cam.targetTexture = null;

                rt.Release();
                rt = null;
            }
        }

        /// <summary>
        /// Setup a RenderTexture to match the viewport and
        /// match the Camera component to Blender's view
        /// </summary>
        /// <param name="camera"></param>
        private void UpdateCamera()
        {
            var camera = InteropData.camera;

            // Resize the render texture / target Texture2D to match the viewport
            if (rt == null || rt.width != camera.width || rt.height != camera.height)
            {
                Profiler.BeginSample("Resize Viewport RT");

                if (cam.targetTexture != null)
                {
                    cam.targetTexture.Release();
                }

                rt = new RenderTexture(
                    camera.width,
                    camera.height,
                    16, RenderTextureFormat.ARGB32
                );
                rt.Create();

                cam.targetTexture = rt;

                tex = new Texture2D(
                    camera.width,
                    camera.height,
                    TextureFormat.RGB24,
                    false
                );

                Profiler.EndSample();
            }

            // TODO: It'd be nice to consolidate this down to an InteropTransform
            // but I don't know the math for Blender view rotation -> Unity look rotation offhand.
            // Ideally the effort happens on Blender's side and transfers as a ready to go
            // transformation (in engine.py:279)

            var p = new Vector3(camera.position.x, camera.position.z, camera.position.y);
            var f = new Vector3(camera.forward.x, camera.forward.z, camera.forward.y);
            var u = new Vector3(camera.up.x, camera.up.z, camera.up.y);

            if (camera.isPerspective)
            {
                transform.position = p;
                transform.rotation = Quaternion.LookRotation(f, u);

                // Here's what I know thus far (from trial and error)
                cam.orthographic = false;
                cam.usePhysicalProperties = true;
                cam.focalLength = camera.lens;
                cam.gateFit = Camera.GateFitMode.Fill;

                // Magic number comes from a number of tests against Blender's viewport
                cam.sensorSize = new Vector2(72, 72);
            }
            else // Orthogonal camera view
            {
                cam.orthographic = true;
                cam.orthographicSize = camera.viewDistance;

                // View distance from the facing direction needs to be factored in,
                // as zooming will change view distance but not position.
                // (2D panning is the only thing that changes position)
                transform.position = p + f * -camera.viewDistance;
                transform.rotation = Quaternion.LookRotation(f, u);
            }
        }

        /// <summary>
        /// Return array of pixels in <c>RGB24</c> format.
        ///
        /// This is only executed if we have room in the pixelProducer
        /// buffer in the SyncManager
        /// </summary>
        /// <returns></returns>
        public byte[] CaptureRenderTexture()
        {
            Profiler.BeginSample("Copy Viewport RT to CPU");

            var prevRT = RenderTexture.active;
            var rt = cam.targetTexture;

            // Render the camera into the RT and extract pixels
            RenderTexture.active = cam.targetTexture;

            cam.Render();
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);

            var data = tex.GetRawTextureData();

            RenderTexture.active = prevRT;

            Profiler.EndSample();

            return data;
        }

        /// <summary>
        /// Add/remove visible objects from this viewport camera
        /// </summary>
        internal void SetVisibleObjects(int[] visibleObjectIds)
        {
            // TODO: Run through objects and read their .visible flag
            // for this viewport. Do some Unity magic to make them visible/invisible
            // to this particular camera when rendering.
        }

        internal void UpdateFromInterop(InteropViewport viewport)
        {
            InteropData = viewport;
            UpdateCamera();
        }
    }
}
