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
        // TODO: This doesn't really need to exist. Can just attach this list onto the object direct.

        /// <summary>
        /// Data that will be shared between applications
        /// </summary>
        internal InteropComponent data;

        /// <summary>
        /// Properties associated with this component. Defined in Blender and Unity.
        /// </summary>
        internal ArrayBuffer<InteropProperty> properties = new ArrayBuffer<InteropProperty>();

        /// <summary>
        /// Combination of target object and component name as a single "target:component" string.
        ///
        /// <para>
        ///     We combine these so that ReplaceOrQueue buffer commands will detect
        ///     the combination of Name+RpcRequest as unique component messages and that
        ///     the receiver application can route to the correct object+component target
        ///     through its own lookup table of event receivers.
        /// </para>
        /// </summary>
        public string Name { get; set; }

        internal Component(string name, string target)
        {
            Name = target + ":" + name;

            data = new InteropComponent
            {
                name = name,
                target = target
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
