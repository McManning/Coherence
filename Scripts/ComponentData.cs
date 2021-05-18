using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.CompilerServices;
using System.Reflection;

namespace Coherence
{
    /// <summary>
    /// Coherence component metadata associated with a MonoBehaviour
    /// </summary>
    internal class ComponentData
    {
        internal SyncManager Sync { get; set; }

        internal string name;

        internal Dictionary<string, List<IEventHandler>> Messages { get; }
                = new Dictionary<string, List<IEventHandler>>();

        internal Dictionary<string, List<IVertexDataStreamHandler>> VertexDataStreams { get; }
            = new Dictionary<string, List<IVertexDataStreamHandler>>();

        internal Dictionary<string, Action> Events { get; set; }
    }
}
