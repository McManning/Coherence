using System;
using System.Collections.Specialized;
using UnityEngine;

namespace Coherence
{
    /// <summary>
    /// Plugin to manage active viewports
    /// </summary>
    public class Viewports : MonoBehaviour, IPlugin
    {
        GameObject viewportsContainer;

        /// <summary>
        /// Viewport controller to match Blender's viewport configuration
        /// </summary>
        readonly OrderedDictionary viewports = new OrderedDictionary();

        /// <summary>
        /// Current index in <see cref="viewports"/> to use for
        /// sending a render texture to Blender
        /// </summary>
        int viewportIndex;

        public void OnRegistered()
        {
            Network.Register(RpcRequest.AddViewport, OnAddViewport);
            Network.Register(RpcRequest.UpdateViewport, OnUpdateViewport);
            Network.Register(RpcRequest.RemoveViewport, OnRemoveViewport);

            Network.OnSync += OnSync;
            Network.OnDisconnected += DestroyAllViewports;
        }

        public void OnUnregistered()
        {
            Network.OnSync -= OnSync;
            Network.OnDisconnected -= DestroyAllViewports;
        }

        private void DestroyAllViewports()
        {
            foreach (var viewport in viewports.Values)
            {
                DestroyImmediate(viewport as UnityEngine.Object);
            }

            viewports.Clear();
        }

        private void OnAddViewport(InteropMessage msg)
        {
            var iv = msg.Reinterpret<InteropViewport>();

            if (viewportsContainer == null)
            {
                viewportsContainer = new GameObject("Viewports")
                {
                    tag = "EditorOnly",
                    hideFlags = HideFlags.NotEditable | HideFlags.DontSave
                };

                viewportsContainer.transform.parent = transform;
            }

            var prefab = CoherenceSettings.Instance.viewportCameraPrefab;
            var go = prefab ? Instantiate(prefab.gameObject) : new GameObject();

            go.name = msg.Target;
            go.transform.parent = viewportsContainer.transform;

            var controller = go.AddComponent<ViewportController>();
            controller.UpdateFromInterop(iv);

            viewports[msg.Target] = controller;
        }

        private void OnUpdateViewport(InteropMessage msg)
        {
            var viewport = viewports[msg.Target] as ViewportController;
            viewport.UpdateFromInterop(msg.Reinterpret<InteropViewport>());
        }

        private void OnRemoveViewport(InteropMessage msg)
        {
            var name = msg.Target;
            if (!viewports.Contains(name))
            {
                return;
            }

            var viewport = viewports[name] as ViewportController;

            DestroyImmediate(viewport.gameObject);
            viewports.Remove(name);

            // Reset viewport iterator, in case this causes us to go out of range
            viewportIndex = 0;
        }

        /// <summary>
        /// Publish the RT of the next viewport in the dictionary.
        ///
        /// <para>
        ///     We do this to ensure that every viewport has a chance of writing
        ///     to the circular buffer - in case there isn't enough room to write
        ///     all the viewports in one frame.
        /// </para>
        /// </summary>
        internal void PublishNextRenderTexture()
        {
            if (viewports.Count < 1)
            {
                return;
            }

            viewportIndex = (viewportIndex + 1) % viewports.Count;

            var viewport = viewports[viewportIndex] as ViewportController;

            // TODO: Move this logic into this component. Network should
            // just deal with the actual data writing.
            Network.PublishRenderTexture(
                viewport,
                viewport.CaptureRenderTexture
            );
        }

        public void OnSync()
        {
            if (!Network.IsConnected)
                return;

            PublishNextRenderTexture();
        }
    }
}
