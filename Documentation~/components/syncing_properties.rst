
Syncing Properties
===================

In your component classes you can declare Blender properties that automatically sync their values with your linked component in Unity.


Adding Properties in Blender
-----------------------------

Declare your properties as annotations like you would for a `Blender PropertyGroup <https://docs.blender.org/api/current/bpy.types.PropertyGroup.html>`_.

.. code-block:: python

    from bpy.props import ( StringProperty, IntProperty, BoolProperty )

    class MyComponent(Coherence.api.Component):
        BoolVal: BoolProperty(name='Boolean Value')
        IntVal: IntProperty(name='Int Value')
        StrVal: StringProperty(name='String Value')

        ...

Properties you declare will be editable within your object's *Coherence Components* panel:

.. image:: https://i.imgur.com/q0Z4uSz.png
    :alt: Blender Component UI


Reading Properties in Unity
--------------------------------

If you have a linked Unity component, properties that are updated in Blender will automatically set their matching C# properties in your component. By adding custom setter logic you can react to these property changes.

.. code-block:: C#

    using UnityEngine;
    using Coherence;

    [ExecuteAlways]
    [Component("MyComponent")]
    public class MyComponent : MonoBehaviour, IComponent
    {
        public bool BoolVal { get; set; }

        public int IntVal { get; set; }

        public string StrVal {
            get { return m_stringVal; }
            set {
                m_StringVal = value;
                Debug.Log("Updated StrVal=" + value);
            }
        }

        private string m_stringVal;
    }

.. important::
    Properties are currently one-way. Updating a property in Unity will not reflect those changes back in Blender.


Limitations
------------

Only a subset of Blender property types are currently supported:

* :class:`bpy.props.StringProperty`
* :class:`bpy.props.IntProperty`
* :class:`bpy.props.BoolProperty`
* :class:`bpy.props.IntProperty`
* :class:`bpy.props.FloatProperty`
* :class:`bpy.props.FloatVectorProperty`
* :class:`bpy.props.EnumProperty`

.. TODO: Subtype/unit support information?

