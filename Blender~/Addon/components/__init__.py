
# Order matters in terms of dependencies.
if 'bpy' in locals():
    import importlib
    importlib.reload(mesh)
    importlib.reload(metaballs)
else:
    from . import mesh
    from . import metaballs

import bpy
