
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

from bpy.types import (
    PropertyGroup
)

from util.registry import autoregister

from .driver import (
    bridge_driver
)

def change_sync_texture(self, context):
    """
    Args:
        self (CoherenceMaterialSettings)
        context (bpy.types.Context)
    """
    img = self.sync_texture # bpy.types.Image

    print('CHANGE Texture2D Sync image={}'.format(img))

def update_sync_texture_settings(self, context):
    mat = context.material
    uid = get_material_uid(mat)

    name = mat.name

    if self.use_sync_texture:
        print('UPDATE Texture2D sync for uid={}, name={}'.format(uid, name))
    else:
        print('DISABLE/SKIP Texture2D sync for uid={}, name={}'.format(uid, name))

def update_object_properties(self, context):
    """
    Args:
        self (CoherenceObjectSettings)
        context (bpy.types.Context)
    """
    bridge_driver().on_update_properties(context.object)

@autoregister
class CoherenceSceneSettings(PropertyGroup):
    """Collection of user configurable settings for the renderer"""

    connection_name: StringProperty(
        name='Unity Connection Name',
        default='Coherence',
        description='This name must match the connection name in Unity\'s Coherence Settings window'
    )

    clear_color: FloatVectorProperty(
        name='Clear Color',
        subtype='COLOR',
        default=(0.15, 0.15, 0.15),
        min=0.0, max=1.0,
        description='Background color of the scene'
    )

    texture_slot_update_frequency: FloatProperty(
        name='TSUF',
        description='How frequently (in seconds) to sync image pixel data to Unity while actively using the Image Paint tool',
        default=0.05
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
    display_mode: EnumProperty(
        name='Unity Display Mode',
        description='Technique used to render this object within Unity',
        items=[
            # Enum items match ObjectDisplayMode in C#
            # First value will be cast to an int when passed to the bridge
            ('0', 'Material', '', 0),
            ('1', 'Normals', 'Show vertex normals in Unity', 1),
            ('2', 'Vertex Colors', 'Show vertex colors in Unity', 2),
            ('3', 'UV Checker', 'Show UV values in Unity', 3),
            ('4', 'UV2 Checker', 'Show UV2 values in Unity', 4),
            ('5', 'UV3 Checker', 'Show UV3 values in Unity', 5),
            ('6', 'UV4 Checker', 'Show UV4 values in Unity', 6),
        ],
        update=update_object_properties
    )

    optimize_mesh: BoolProperty(
        name='Optimize Mesh',
        description='Optimize (compress) vertex data prior to sending to Unity. ' +
                    'With this option turned off - full loops will be transmitted which may negatively ' +
                    'impact performance or lookdev in Unity when compared with an export of the same mesh',
        default=True,
        update=update_object_properties
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

@autoregister
class CoherenceMaterialSettings(PropertyGroup):
    use_sync_texture: BoolProperty(
        name='Use Texture Syncing',
        description='Sync a Texture2D channel in the Unity Material with an editable Blender Image',
        update=update_sync_texture_settings
    )

    sync_texture: PointerProperty(
        name='Sync Texture',
        description='Blah blah',
        type=bpy.types.Image,
        update=change_sync_texture
        # TODO: This type reference may not work when loading the plugin.
        # bpy.types isn't available when instantiating plugins, so this'll
        # most likely fail. Need some sort of late-load behavior here.
    )

    sync_texture_map: EnumProperty(
        name='Target Map',
        items=[
            # Default for custom material shaders
            ('_MainTex', 'Main Tex', '', 1),

            #  URP property names
            ('_BaseMap', 'Base Map', '', 2),
            ('_BumpMap', 'Bump Map', '', 3),
            ('_EmissionMap', 'Emission Map', '', 4),
            ('_MetallicGlossMap', 'Metallic/Gloss Map', '', 5),
            ('_OcclusionMap', 'Occlusion Map', '', 6),
            ('_SpecGlossMap', 'Specular/Gloss Map', '', 7),

            # Other standard property names
            ('_ParallaxMap', 'Parallax Map', '', 8),
            ('_DetailMask', 'Detail Mask', '', 9),
            ('_DetailNormalMap', 'Detail Normal Map', '', 10),
            ('_DetailAlbedoMap', 'Detail Albedo Map', '', 11),

            # Custom (read from custom_sync_texture_map)
            ('CUSTOM', 'Custom Property Name', '', 100)
        ],
        update=update_sync_texture_settings
    )

    custom_sync_texture_map: StringProperty(
        name='Property Name',
        description='Use a property name matching the Unity shader (e.g. _MainTex)',
        update=update_sync_texture_settings
    )

    @classmethod
    def register(cls):
        bpy.types.Material.coherence = PointerProperty(
            name='Coherence Material Settings',
            description='',
            type=cls
        )

    @classmethod
    def unregister(cls):
        del bpy.types.Material.coherence


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
    slots = bridge_driver().get_texture_slots()
    return [(name, name, '') for name in slots]

def on_update_texture_slot(self, context):
    image = self.id_data

    # Re-validate prior to trying to sync
    # (e.g. to ensure it's not a zero length image or wrong format)
    self.error = validate_image_for_sync(image)

    # Sync immediately to the target slot once changed
    bridge_driver().sync_texture(image)


@autoregister
class CoherenceImageSettings(PropertyGroup):
    error: StringProperty(
        name='Source Image Error',
        description='An error with the source image that prevents syncing with Unity',
        default='',
        # Errors are runtime only
        options={'SKIP_SAVE'}
    )

    texture_slot: EnumProperty(
        name='Slot',
        description='',
        items=texture_slot_enum_items,
        update=on_update_texture_slot,
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
