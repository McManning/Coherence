
if 'bpy' in locals():
    import importlib
    importlib.reload(debug)
    importlib.reload(registry)
else:
    from . import debug
    from . import registry

import bpy
