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
        public string Name { get; set; }

        /// <summary>
        /// Data that will be shared between applications
        /// </summary>
        internal InteropSceneObject data;

        internal List<Component> components;

        internal SceneObject(string name, InteropTransform transform)
        {
            Name = name;
            components = new List<Component>();

            data = new InteropSceneObject
            {
                name = name,
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
