
Installation
=============

Requirements
-------------

Coherence is currently only supported on Windows 10. It uses a number of features for inter-process communication that are only available on Windows.

Testing is done with Blender 2.91 and Unity 2020.1.3 but Coherence should work with later versions.


Download
---------

.. note::
    Release packages are not available yet.

Download artifacts `from the most recent Build Packages job <https://github.com/McManning/Coherence/actions?query=workflow%3A%22Build+Packages%22>`_.

.. warning::
    These artifacts update whenever the master branch is pushed, which may include various breaking changes or bugs.


Installing the Blender Addon
-----------------------------

In Blender, go to *Edit > Preferences > Add-ons* and click *Install*. Select the `blender-addon.zip` you downloaded earlier.

After installation make sure it's selected in the Add-ons UI.

.. image:: https://i.imgur.com/WHm2sLy.png
    :alt: Blender Add-ons UI

If installation is successful, you should have a new Render Engine available under *Render Properties* in the Properties Panel:

.. image:: https://i.imgur.com/FV1a838.png
    :alt: Render Properties UI


Installing the Unity Package
-----------------------------

Unzip the `unity-package.zip` you downloaded.

In Unity, go to *Window > Package Manager > + > Add package from disk* and select the package you just unzipped.

Alternatively, you can just unzip it right into the *Packages* directory of your project.

.. image:: https://i.imgur.com/YlppceL.png
    :alt: Unity Package Manager

After installing the package, you can access the Coherence Settings window via *Window > Coherence*

.. image:: https://i.imgur.com/LrMRiWc.png
    :alt: Coherence Settings Window


Enabling 'Unsafe' Code in Unity
--------------------------------

Coherence uses a number of low level features in C# for interoperability with Blender so you will need to enable unsafe code for your Unity project.

In *File > Project Settings* go to the Player settings and enable `Allow 'unsafe' Code`:

.. image:: https://i.imgur.com/w0KuaUq.png
    :alt: Unity Project Settings Window
