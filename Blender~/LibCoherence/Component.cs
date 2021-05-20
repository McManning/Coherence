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
        /// Combination of target object and component name.
        /// We combine these so that ReplaceOrQueue buffer commands will detect
        /// the combination of Name+RpcRequest as unique component messages.
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
