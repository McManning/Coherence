
# Order matters in terms of dependencies.
if 'bpy' in locals():
    import importlib
    importlib.reload(mesh)
    importlib.reload(metaball)
else:
    from . import mesh
    from . import metaball

import bpy
