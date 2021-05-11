
Global Plugin API
==================

Global plugins can be registered with Coherence either in Blender with the Python API or Unity with the C# API. Global plugins are capable of listening to all Coherence stateful events (enabled, disabled, connected, disconnected, etc), and send or receive events with a matching plugin in the connected application.

Global plugins in Blender also have the ability to instantiate new Object Plugins and attach them to existing :class:`bpy.types.Object` instances to sync to Unity.

See :doc:`index` for examples.

.. include:: ./global_plugins_lifecycle.inc

.. include:: ./global_plugins_blender.inc

.. include:: ./global_plugins_unity.inc
