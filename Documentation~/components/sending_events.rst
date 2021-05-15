
Sending Events
===============

When you have a linked component defined in both Blender and Unity you can send event messages containing custom structures of data between the two applications.

There are a number of restrictions you must be aware of before starting to keep data interchangeable between applications:

* A component must be instantiated on both sides with the same linked name
    * The name of the Blender component matches the class name (e.g. ``Light`` from prior examples).
    * A Unity MonoBehaviour uses the :sphinxsharp:type:`ComponentAttribute` to define the matching component name and implements :sphinxsharp:type:`IComponent` to access the required API methods.
* Your structures must contain only blittable types
    * Primitive types such as ``System.Int16``, ``System.Single``, ``System.Byte``, are allowed.
    * Pointers are not supported.
    * C# arrays are not supported, but you *can* use a fixed array in a struct marked ``unsafe``.
    * We do provide some interop types such as ``InteropString64`` as a fixed length version of ``System.String``.
* The size of the structure must fit within a single Node
    * To calculate maximum byte size, subtract roughly 1 KB from the *Node Size* in Unity's Coherence Settings window (under Advanced Settings) to account for additional header information sent alongside your structure.

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

    class MyPlugin(Coherence.api.Component):
        def on_enable(self):
            # add a handler for inbound "Foo" events from Unity
            self.add_handler('Foo', self.on_foo)

            # Send a "Foo" event to Unity
            self.send_foo(1, 2, 'Hello from Blender')

        def on_disable(self):
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

You can define as many different events and payload structures as you want for your component.

In Unity, define your matching component and handlers:

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

    [Component("MyPlugin"), ExecuteAlways]
    public class MyPlugin : MonoBehaviour, IComponent
    {
        private void OnEnable()
        {
            // add a handler for inbound "Foo" events from Blender
            AddHandler<FooEvent>("Foo", OnFoo);

            // Send a "Foo" event to Blender
            SendFoo(1, 2, "Hello from Unity");
        }

        private void OnDisable()
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

Once the component is added to an object in Blender and synced between applications, a "Hello from Unity" message will be displayed in Blender and a "Hello from Blender" message will be displayed in Unity.


Example - Blender Lights
-------------------------

Coherence does not have a built-in component to sync :class:`bpy.types.Light` objects to Unity. But by using the event API you can achieve this pretty easily:

.. code-block:: python

    import bpy
    import Coherence

    class LightProps(ctypes.Structure):
        """Light properties to send to Unity"""
        _fields_ = [
            ('type', InteropString64), # value in ['POINT', 'SUN', 'SPOT', 'AREA']
            ('distance', ctypes.c_float),
            ('r', ctypes.c_float),
            ('g', ctypes.c_float),
            ('b', ctypes.c_float),
        ]

    class Light(Coherence.api.Component):
        """Component to sync Blender light properties to Unity"""
        @classmethod
        def poll(cls, bpy_obj):
            # Attach to all Blender lights in the scene
            return bpy_obj.type == 'LIGHT'

        def on_enable(self):
            # Send current light properties once enabled
            self.send_props()

        def send_props(self):
            """Send current light properties to Unity"""
            light = self.bpy_obj.data

            # Copy bpy.types.Light properties to an event struct
            evt = LightProps()
            evt.type = InteropString64(light.type)
            evt.distance = light.distance
            evt.r = light.color[0]
            evt.g = light.color[1]
            evt.b = light.color[2]

            self.send_event('UpdateProps', evt)

    def register():
        Coherence.api.register_component(Light)

    def unregister():
        Coherence.api.unregister_component(Light)

And the matching Unity component to create and update a :class:`UnityEngine.Light` whenever Blender properties change:

.. code-block:: C#

    using UnityEngine;
    using Coherence;

    [ExecuteAlways]
    [Component("Light")]
    public class BlenderLight : MonoBehaviour, IComponent
    {
        private Light light;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Props
        {
            public InteropString64 type;
            public float distance;
            public float r;
            public float g;
            public float b;
        }

        private void OnEnable()
        {
            // Add a light to the synced GameObject
            light = AddComponent<Light>();

            // Listen to property updates from Blender
            AddHandler("UpdateProps", OnUpdateProps);
        }

        private void OnDisable()
        {
            RemoveHandler("UpdateProps", OnUpdateProps);

            Destroy(light);
            light = null;
        }

        private void OnUpdateProps(string id, Props props)
        {
            // Update `light` with properties from Blender
        }
    }

To further enhance the above example, you can add listeners in Blender to execute ``send_props`` whenever light properties are modified through Blender's UI.
