using System;
using UnityEngine;

namespace Coherence
{
    public class ImageSync : MonoBehaviour, IPlugin
    {
        public void OnRegistered()
        {
            Network.Register(RpcRequest.UpdateImage, OnUpdateImage);
            Network.Register(RpcRequest.UpdateImageData, OnUpdateImageData);
        }

        private void OnUpdateImage(InteropMessage msg)
        {
            GetTexture(msg.Target).UpdateFromInterop(msg.Reinterpret<InteropImage>());
        }

        private void OnUpdateImageData(InteropMessage msg)
        {
            GetTexture(msg.Target).CopyFrom(
                msg.data, msg.header.index, msg.header.count, msg.header.length
            );
        }

        public void OnUnregistered()
        {

        }

        private BlenderTexture GetTexture(string name)
        {
            return CoherenceSettings.Instance.textureSlots.Find(
                (tex) => tex.name == name
            );
        }
    }
}
