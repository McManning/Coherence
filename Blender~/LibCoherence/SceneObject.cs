using SharedMemory;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Coherence
{
    /// <summary>
    /// A distinct object in a Blender scene
    /// </summary>
    class SceneObject : IInteropSerializable<InteropSceneObject>
    {
        /// <summary>
        /// Data that will be shared with Unity
        /// </summary>
        internal InteropSceneObject data;

        public string Name { get; set; }

        internal SceneObject(string name, SceneObjectType type, InteropTransform transform)
        {
            Name = name;

            data = new InteropSceneObject
            {
                name = name,
                type = type,
                transform = transform
            };
        }

        public InteropSceneObject Serialize()
        {
            return data;
        }

        public void Deserialize(InteropSceneObject interopData)
        {
            throw new InvalidOperationException();
        }
    }
}
