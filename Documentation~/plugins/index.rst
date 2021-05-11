
Writing Third Party Plugins
============================

There are two types of plugins supported by Coherence:

* **Global Plugins** are registered globally using the Python or C# API and can listen to Coherence events (enabled, disabled, connected, etc) and send or receive messages with a matching plugin in the connected application.

* **Object Plugins** are associated with individual :class:`bpy.types.Object` instances and can send or receive messages designated for that specific object.

.. include:: ./creating_global_plugins.inc

.. include:: ./creating_object_plugins.inc
