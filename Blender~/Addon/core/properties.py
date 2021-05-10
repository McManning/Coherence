
from bpy.props import (
    BoolProperty,
    EnumProperty,
    FloatProperty,
    FloatVectorProperty,
    IntProperty,
    PointerProperty,
    StringProperty
)

import bpy

from bpy.types import PropertyGroup

from . import runtime
from util.registry import autoregister

def _update_object_properties(self, context):
    """
    Args:
        self (CoherenceObjectSettings)
        context (bpy.types.Context)
    """
    obj = runtime.instance.objects.find_by_bpy_name(context.object.name)
    if obj: obj.update_properties()

@autoregister
class CoherenceSceneProperties(PropertyGroup):
    """The primary Coherence connection settings"""

    #: str, default ``Coherence``: Shared connection name that matches with Unity's Coherence Settings window
    connection_name: StringProperty(
        name='Buffer Name',
        default='Coherence',
        description='This name must match the buffer name in Unity\'s Coherence Settings window'
    )

    #: float, default 0.05: How frequently (in seconds) to sync image pixel data while actively using the Image Paint tool
    texture_slot_update_frequency: FloatProperty(
        name='Image Update Frequency',
        description='How frequently (in seconds) to sync image pixel data while actively using the Image Paint tool',
        default=0.05
    )

    #: bool, default True: Show the Coherence toggle button in the viewport controls menu
    show_view3d_controls: BoolProperty(
        name='Show Viewport Controls',
        description='Show the Coherence toggle button in the viewport controls menu',
        default=True
    )

    @classmethod
    def register(cls):
        bpy.types.Scene.coherence = PointerProperty(
            name='Coherence Renderer Settings',
            description='',
            type=cls
        )

    @classmethod
    def unregister(cls):
        del bpy.types.Scene.coherence

@autoregister
class CoherenceObjectProperties(PropertyGroup):
    #: enum, default 0: Technique used to render this object within Unity
    display_mode: EnumProperty(
        name='Unity Display Mode',
        description='Technique used to render this object within Unity',
        items=[
            # Enum items match ObjectDisplayMode in C#
            # First value will be cast to an int when passed to the bridge
            ('0', 'Material', '', 0),
            ('1', 'Normals', 'Show vertex normals in Unity', 1),
            ('2', 'Vertex Colors', 'Show vertex colors in Unity', 2),

            ('10', 'UV Checker', 'Show UV values in Unity', 10),
            ('11', 'UV2 Checker', 'Show UV2 values in Unity', 11),
            ('12', 'UV3 Checker', 'Show UV3 values in Unity', 12),
            ('13', 'UV4 Checker', 'Show UV4 values in Unity', 13),

            ('100', 'Hidden', 'Do not render this object in Unity', 100),
        ],
        update=_update_object_properties
    )

    #: bool, default True: Optimize (compress) vertex data prior to sending to Unity.
    optimize_mesh: BoolProperty(
        name='Optimize Mesh',
        description='Optimize (compress) vertex data prior to sending to Unity. ' +
                    'With this option turned off - full loops will be transmitted which may negatively ' +
                    'impact performance or lookdev in Unity when compared with an export of the same mesh',
        default=True,
        update=_update_object_properties
    )

    @classmethod
    def register(cls):
        bpy.types.Object.coherence = PointerProperty(
            name='Coherence Settings',
            description='',
            type=cls
        )

    @classmethod
    def unregister(cls):
        del bpy.types.Object.coherence


# TODO: Move to image.py
def validate_image_for_sync(img) -> str:
    """Check if an image can be synced with Unity

    Args:
        img (bpy.types.Image)

    Returns:
        str: Error message, or an empty string for no error
    """
    w, h = img.size

    # Perform additional checks for image format - ensuring we can transfer
    # it in RGBA32 without significant conversion overhead
    #if img.depth != 32:
    #    return 'Image must contain an alpha channel'

    if w < 1 or h < 1 or w > 1024 or h > 1024:
        return 'Image must be between 1x1 and 1024x1024 to enable syncing'

    return ''

def texture_slot_enum_items(self, context):
    """Generate an `EnumProperty` items list from Coherence texture slots

    Args:
        context (:mod:`bpy.context`)

    Returns:
        list of [(slot, slot, ''), ...]
    """
    slots = runtime.instance.get_texture_slots()
    return [(name, name, '') for name in slots]

def _on_update_texture_slot(self, context):
    """

    Args:
        context (:mod:`bpy.context`)
    """
    image = self.id_data

    # Re-validate prior to trying to sync
    # (e.g. to ensure it's not a zero length image or wrong format)
    self.error = validate_image_for_sync(image)

    # Sync immediately to the target slot once changed
    runtime.instance.sync_texture(image)


@autoregister
class CoherenceImageProperties(PropertyGroup):
    #: str, default None: An error with the source image that prevents syncing with Unity
    error: StringProperty(
        name='Source Image Error',
        description='An error with the source image that prevents syncing with Unity',
        default='',
        # Errors are runtime only
        options={'SKIP_SAVE'}
    )

    #: str, default None: The named texture slot to sync the active image
    texture_slot: EnumProperty(
        name='Slot',
        description='The named texture slot to sync the active image',
        items=texture_slot_enum_items,
        update=_on_update_texture_slot,
        default=0,
        # Don't persist slot targets between Blender saves,
        # as these may be modified within Unity.
        options={'SKIP_SAVE'}
    )

    @classmethod
    def register(cls):
        bpy.types.Image.coherence = PointerProperty(
            name='Coherence Image Settings',
            description='',
            type=cls
        )

    @classmethod
    def unregister(cls):
        del bpy.types.Image.coherence
