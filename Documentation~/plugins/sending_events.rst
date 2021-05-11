
Sending Events
===============

You can send custom structures of data between two matching Global Plugins or Object Plugins.

There are a number of restrictions you must be aware of before starting to keep data interchangeable between applications:

* Both plugins must have the same name and parent
    * The name of the Blender plugin matches the class name. "MyPlugin" would have been the common name of the previous example. For a Unity ScriptableObject, you would use the :sphinxsharp:type:`PluginAttribute` to define your matching plugin name.
    * For an Object Plugin, you must match both the Object Plugin name and the parent Global Plugin name that instantiated the object from Blender.
* Your structures must contain only blittable types
    * Primitive types such as `System.Int16`, `System.Single`, `System.Byte`, are allowed.
    * Pointers are not supported.
    * C# arrays are not supported, but you *can* use a fixed array in a struct marked `unsafe`.
    * We do provide some interop types such as `InteropString64` as a fixed length version of `System.String`.
* The size of the structure must fit within a single Node
    * To calculate maximum size, subtract roughly 1 KB from the *Node Size* in Unity's Coherence Settings window (in Advanced Settings) to account for additional header information sent alongside your structure.

Starting from Blender, you will want to define your structure through ctypes and send it as an event while Coherence is connected:

.. code-block:: python

    import ctypes
    from Coherence.core.interop import InteropString64

    class FooEvent(ctypes.Structure):
        """ctypes structure that will be sent to/from Unity"""
        _fields_ = [
            ('intval', ctypes.c_int),
            ('byteval', ctypes.c_byte),
            ('strval', InteropString64),
        ]

    class MyPlugin(Coherence.api.Plugin):
        def on_connected(self):
            # add a handler for inbound "Foo" events from Unity
            self.add_handler('Foo', self.on_foo)

            # Send a "Foo" event to Unity
            self.send_foo(1, 2, 'Hello from Blender')

        def on_disconnected(self):
            # Cleanup old handlers
            self.remove_handler('Foo', self.on_foo)

        def on_foo(self, id: str, size: int, data):
            """Handle `Foo` events from Unity

            Args:
                id (str):           Event ID (always "Foo" in this example)
                size (int):         Size of the payload in bytes
                data (c_void_p):    Payload from Unity
            """
            msg = FooEvent.from_address(data)
            print('intval={}, byteval={}, strval={}'.format(
                msg.intval,
                msg.byteval,
                msg.strval
            ))

        def send_foo(self, intval: int, byteval: int, strval: str):
            """Send a `Foo` event to Unity"""
            msg = FooEvent()
            msg.intval = intval
            msg.byteval = byteval
            msg.strval = InteropString64(strval.encode())

            self.send_event('Foo', msg)

You can define as many different events and payload structures as you want for your plugin.

Event IDs and payloads **are not shared** between different plugins so in order to receive the event in Unity you will need to create a matching plugin with the same name and handlers for your event:

.. code-block:: C#

    using System.Runtime.InteropServices;
    using UnityEngine;
    using Coherence;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FooEvent
    {
        public int intval;
        public byte byteval;
        public InteropString64 strval;
    }

    [Plugin("MyPlugin")]
    public class MyPlugin : ScriptableObject, IPlugin
    {
        public void OnConnectCoherence()
        {
            // add a handler for inbound "Foo" events from Blender
            AddHandler<FooEvent>("Foo", OnFoo);

            // Send a "Foo" event to Blender
            SendFoo(1, 2, "Hello from Unity");
        }

        public void OnDisconnectCoherence()
        {
            // Cleanup old handlers
            RemoveHandler<FooEvent>("Foo", OnFoo);
        }

        /// Handle "Foo" events from Blender
        private void OnFoo(string id, FooEvent msg)
        {
            Debug.Log(
                " intval=" + msg.intval.ToString() +
                " byteval=" + msg.byteval.ToString() +
                " strval=" + msg.strval.ToString()
            );
        }

        /// Send a "Foo" event to Blender
        private void SendFoo(int intval, byte byteval, string strval)
        {
            var msg = new FooEvent {
                intval = intval,
                byteval = byteval,
                strval = new InteropString64(strval)
            };

            SendEvent<FooEvent>("Foo", msg);
        }
    }

Once Coherence connects the two applications, a "Hello from Unity" message will be displayed in Blender and a "Hello from Blender" message will be displayed in Unity.

Sending Object Events
-----------------------

The same event API is provided for Object Plugins - e.g. :py:meth:`.ObjectPlugin.send_event` and :sphinxsharp:meth:`IObjectPlugin.SendEvent\<T\>`.

Similar to Global Plugins, only the matching Object Plugin instance between applications can receive the event. If you have multiple objects with a ``Light`` Object Plugin and send an event from Blender - only the GameObject referencing the same :py:class:`bpy.types.Object` will receive the event for its ``Light`` MonoBehaviour.

Using the ``Light`` example from Creating Object Plugins we can use events to transfer light properties from Blender to Unity:

.. code-block:: python

    import bpy
    import Coherence

    class LightProps(ctypes.Structure):
        """Light properties to send to Unity"""
        _fields_ = [
            ('type', InteropString64),
            ('distance', ctypes.c_float),
            ('r', ctypes.c_float),
            ('g', ctypes.c_float),
            ('b', ctypes.c_float),
        ]

    class Light(Coherence.api.ObjectPlugin):
        def on_create(self):
            self.send_props()

        def send_props(self):
            """Send updated light properties to Unity"""
            light = self.bpy_obj.data

            # Copy bpy.types.Light properties to an event struct
            evt = LightProps()
            evt.type = InteropString64(light.type)
            evt.distance = light.distance
            evt.r = light.color[0]
            evt.g = light.color[1]
            evt.b = light.color[2]

            self.send_event('UpdateProps', evt)

    class LightsPlugin(Coherence.api.Plugin):
        def on_add_bpy_object(self, bpy_obj):
            if bpy_obj.type == 'LIGHT':
                self.instantiate(Light, bpy_obj)

    def register():
        Coherence.api.register_plugin(LightsPlugin)

    def unregister():
        Coherence.api.unregister_plugin(LightsPlugin)

.. code-block:: C#

    using UnityEngine;
    using Coherence;

    [ObjectPlugin("Light", Plugin = "LightsPlugin")]
    public class BlenderLight : MonoBehaviour, IObjectPlugin
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Props
        {
            public InteropString64 type;
            public float distance;
            public float r;
            public float g;
            public float b;
        }

        /// Standard Unity OnEnable called when attached to a GameObject
        private void OnEnable()
        {
            AddHandler<Props>("UpdateProps", OnUpdateProps);
        }

        private void OnUpdateProps(string id, Props props)
        {
            // Do something with Blender light properties
        }
    }

A tighter integration to sync Blender lights for the above would involve executing ``send_props`` whenever light properties are modified in Blender's UI in order to notify Unity that properties have been changed by the user.

