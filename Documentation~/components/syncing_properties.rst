
Syncing Properties
===================

You can add Blender properties to a component class to automatically sync changes with Unity.

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


Adding Properties in Blender
-----------------------------

Declare your properties as annotations like you would for any typical PropertyGroup:

.. code-block:: python

    from bpy.props import ( StringProperty, IntProperty, BoolProperty )

    class MyComponent(Coherence.api.Component):
        BoolVal: BoolProperty(name='Boolean Value')
        IntVal: IntProperty(name='Int Value')
        StrVal: StringProperty(name='String Value')

        ...

Declared properties will be editable within your object's *Coherence Components* panel:

.. image:: https://i.imgur.com/q0Z4uSz.png
    :alt: Blender Component UI

Accessing Properties from Unity
--------------------------------

If you have a linked Unity component, properties that are updated in Blender will automatically update their matching C# properties in Unity:

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
