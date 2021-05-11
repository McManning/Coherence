
Creating Components
--------------------

Merged API of Object Plugins and Global Plugins. Instructions here. Esp around ``poll`` and whatnot.

.. code-block:: python

    import bpy
    import Coherence

    class Light(Coherence.api.Component):
        """Custom component representing our Blender light.

        The class name (Light) will be used as the component name
        """
        @classmethod
        def poll(cls, bpy_obj):
            return bpy.obj.type == 'LIGHT'

        def on_create(self):
            print('Added Light Component!')

        def on_destroy(self):
            print('Removed Light Component!')

    def register():
        Coherence.api.register_component(Light)

    def unregister():
        Coherence.api.unregister_component(Light)

.. code-block:: C#

    using UnityEngine;
    using Coherence;

    [Component("Light")]
    public class BlenderLight : MonoBehaviour, IComponent
    {
        /// Standard Unity OnEnable called when attached to a GameObject
        private void OnEnable()
        {
            Debug.Log("Added Light Component!");
        }

        /// Standard Unity OnDisable called when removing from a GameObject
        private void OnDisable()
        {
            Debug.Log("Removed Light Component!");
        }
    }
