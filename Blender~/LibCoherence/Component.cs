using SharedMemory;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Coherence
{
    /// <summary>
    /// A component attached to an object
    /// </summary>
    class Component : IInteropSerializable<InteropComponent>
    {
        /// <summary>
        /// Data that will be shared between applications
        /// </summary>
        internal InteropComponent data;

        public string Name { get; set; }

        internal Component(string name, string target, bool enabled)
        {
            Name = name;

            data = new InteropComponent
            {
                name = name,
                target = target,
                enabled = enabled
            };
        }

        public InteropComponent Serialize()
        {
            return data;
        }

        public void Deserialize(InteropComponent interopData)
        {
            throw new NotImplementedException("TODO: Receive updates from Unity");
        }
    }
}
