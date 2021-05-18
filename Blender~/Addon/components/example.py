import bpy
from bpy.props import (
    IntProperty,
    FloatProperty,
    BoolProperty,
    StringProperty,
    FloatVectorProperty
)

from .. import api
from ..util import error

class Example(api.Component):
    int_val: IntProperty(name="Int Val")
    bool_val: BoolProperty(name="Bool Val")
    str_val: StringProperty(name="Str Val")
    float_val: FloatProperty(name="Float Val")
    vec_val = FloatVectorProperty(name="Vec Val")
