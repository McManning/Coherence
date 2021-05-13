
Syncing Properties
===================

You can add Blender properties to a component class to automatically sync changes with Unity.

Declare your properties as annotations like you would for any typical PropertyGroup:

.. code-block:: python

    from bpy.props import ( StringProperty, IntProperty, BoolProperty )

    class MyComponent(Coherence.api.Component):
        strval: StringProperty(name='String Value')
        intval: IntProperty(name='Int Value')
        boolval: BoolProperty(name='Boolean Value')

        ...

Declared properties will be editable within your object's *Coherence Components* panel:

.. image:: https://i.imgur.com/q0Z4uSz.png
    :alt: Blender Component UI

.. note::
    TODO: Unity API on how to consume these property changes (property setters? Array with props and their values?)

Limitations
------------

Only a subset of Blender property types are supported:

* :class:`bpy.props.StringProperty`
* :class:`bpy.props.IntProperty`
* :class:`bpy.props.BoolProperty`
* :class:`bpy.props.IntProperty`
* :class:`bpy.props.FloatProperty`
* :class:`bpy.props.FloatVectorProperty`
* :class:`bpy.props.EnumProperty`

.. note::
    TODO: Subtype/unit support information?
