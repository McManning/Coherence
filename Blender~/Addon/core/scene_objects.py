
import bpy
from .plugin import Plugin
from .interop import (lib, to_interop_transform, update_transform)

from .utils import (
    debug,
    error,
    get_string_buffer
)

class SceneObjects(Plugin):
    """Management of synced scene objects and their components."""
    #: dict[str, set[Component]]: Mapping of object name to component instances
    objects: dict

    #: dict[Type[Component], set[Component]]: Mapping of component class to instances
    registered: dict

    #: set[Type[Component]]: All component classes that autobind
    autobinds: set

    #: dict[str, Component]
    dirty_geometry: dict

    #: set[str]: Object names found during the last scene scan
    current_names: set()

    def __init__(self):
        self.objects = dict()
        self.registered = dict()
        self.autobinds = set()
        self.dirty_geometry = dict()
        self.current_names = set()

    def on_new_object(self, obj):
        """Check if the given object should have components added.

        All autobind components will ``poll`` against the new object to
        determine if they should be added.

        Any components that are registered and exist in the added object's
        meta properties will also be automatically re-instantiated. This will
        typically happen when an object is copied or the user undoes the
        deletion of an object that had components.

        Args:
            obj (bpy.types.Object):

        Raises:
            KeyError: If the object is already tracked (duplicate add)
        """
        # Check for any pre-existing components to add
        for meta in obj.coherence.components:
            component = self.get_registered_component(meta.name)
            if component:
                self.add_component(obj, component)

        # Check for any autobind components to add
        for component in self.autobinds:
            if component.poll(obj):
                self.add_component(obj, component)

    def destroy_object(self, obj_name):
        """Destroy all components referencing the given object.

        Since there is no underlying object when this is called (as we assume
        it was already removed from the scene) this does not make an attempt to
        update the meta or remove PropertyGroups.

        Args:
            obj_name (str): Name of the now destroyed :class:`bpy.types.Object`
        """
        if obj_name not in self.objects:
            return # Not tracked for components

        self.on_destroy_object(obj_name)

        # Destroy all components
        for instance in self.objects[obj_name]:
            component_name = instance.name()

            self.on_destroy_component(obj_name, component_name)

            # Disable/destroy the component
            try:
                instance.enabled = False
                instance.on_destroy()
            except Exception as err:
                self.on_component_error(component_name, err, obj_name)
                pass

            # Remove from the Class -> instances map
            self.registered[instance.__class__].remove(instance)

        del self.objects[obj_name]

    def register_component(self, component):
        """
        Args:
            component (Type[Component]):
        """
        print('REGISTER COMPONENT {} ON {}'.format(component, self))
        component.register_property_group()

        # Callback on initial registration
        try:
            component.on_registered()
        except Exception as err:
            self.on_component_error(component.name(), err)
            return

        autobind = component.is_autobind()
        component_name = component.name()

        # Track component
        self.registered[component] = set()
        if autobind:
            self.autobinds.add(component)

        # Instantiate for objects already referencing this component
        # or match the autobind rules
        for obj_name in self.objects:
            obj = bpy.data.objects[obj_name]
            for meta in obj.coherence.components:
                if meta.name == component_name:
                    self.add_component(obj, component)
                    break
            else:
                # Not already registered, but autobinds
                if autobind and component.poll(obj):
                    self.add_component(obj, component)

        print('Currently registered {} components'.format(len(self.registered)))

    def unregister_component(self, component):
        """
        Args:
            component (Type[Component]):
        """
        print('UNREGISTER COMPONENT {}'.format(component))
        component.unregister_property_group()

        # Callback on final cleanup
        try:
            component.on_unregistered()
        except Exception as err:
            # Report errors but don't fail to unregister
            self.on_component_error(component.name(), err)

        self.destroy_all_component_instances(component)

        # Untrack component
        del self.registered[component]
        try:
            self.autobinds.remove(component)
        except KeyError:
            pass

    def unregister_all_components(self):
        for component in self.registered.keys():
            try:
                component.on_unregistered()
            except Exception as err:
                self.on_component_error(component.name(), err)

            self.destroy_all_component_instances(component)

        self.registered.clear()
        self.autobinds.clear()

    def get_component(self, obj, component):
        """Get the instance of the component class on the object if it exists

        Args:
            component (Type[Component]):

        Returns:
            Union[:class:`.Component`, None]
        """
        return self.get_component_by_name(obj, component.name())

    def get_component_by_name(self, obj, component_name):
        return next((x for x in self.objects[obj.name] if x.name() == component_name), None)

    def all_components(self):
        """Generator of every component instance

        Yields:
            :class:`.Component`: The next component instance
        """
        for instances in self.registered.values():
            for instance in instances:
                yield instance

    def get_registered_component(self, component_name: str):
        return next((x for x in self.registered if x.name() == component_name), None)

    def get_available_components(self, obj):
        """Return a set of component classes that could be registered to the target.

        Returns:
            set[Component]
        """
        results = set()

        for component in self.registered:
            for meta in obj.coherence.components:
                if meta.name == component.name(): break
            else:
                results.add(component)

        return results

    def add_component_by_name(self, obj, component_name):
        """

        Raises:
            KeyError:   If the object already has an instance registered
                        or the component does not exist

        Returns:
            Component: Instance that was added
        """
        for component in self.registered.keys():
            if component.name() == component_name:
                return self.add_component(obj, component)

        raise KeyError('Component [{}] is not registered'.format(component_name))

    def add_component(self, obj, component):
        """Add a component to a tracked object.

        Object must already have been added via :meth:`add_object`.

        Args:
            obj (bpy.types.Object):
            component (Type[Component]):

        Returns:
            Component: Instance that was added
        """
        obj_name = obj.name
        component_name = component.name()

        print('ADD COMPONENT component_name={}, obj_name={}'.format(
            component_name,
            obj_name
        ))

        if obj_name not in self.objects:
            self.objects[obj_name] = set()

        # If it's already added, skip
        if self.get_component(obj, component) is not None:
            return

        instance = component(obj_name)
        self.objects[obj_name].add(instance)
        self.registered[component].add(instance)

        meta = self._find_or_create_meta(obj, component_name)

        self.on_add_component(obj, instance)

        try:
            instance.enabled = meta.enabled
            instance.on_create()
        except Exception as err:
            self.on_component_error(component_name, err, obj_name)
            # TODO: Should this have prevented creation altogether?

        # If the component has a mesh assigned to it, add to the
        # list of geometry to update next depsgraph update.
        mesh_uid = instance.mesh_uid
        if mesh_uid:
            self.dirty_geometry[mesh_uid] = instance

        return instance

    def destroy_all_component_instances(self, component):
        """Destroy all instances of the given component class

        Args:
            component (Type[Component]):
        """
        instances = self.registered[component].copy()
        for instance in instances:
            self.destroy_component(instance.bpy_obj, instance)

    def destroy_component_by_name(self, obj, component_name):
        """

        Raises:
            KeyError: If the object is not tracked or is missing the given component
        """
        for instance in self.objects[obj.name]:
            if instance.name() == component_name:
                return self.destroy_component(obj, instance)

        raise KeyError('Object does not have a component [{}]'.format(component_name))

    def destroy_component(self, obj, instance):
        """
        Args:
            obj (bpy.types.Object):
            instance (Component):
        """
        obj_name = obj.name
        component_name = instance.name()

        self.on_destroy_component(obj_name, component_name)

        try:
            instance.enabled = False
            instance.on_destroy()
        except Exception as err:
            self.on_component_error(component_name, err, obj_name)

        # Untrack the destroyed component
        self.objects[obj_name].remove(instance)
        self.registered[instance.__class__].remove(instance)

        # Delete the underlying PropertyGroup data associated
        # with this component instance as well. This'll prevent
        # lingering properties for components we no longer use.
        self._remove_meta(obj, component_name)

        if hasattr(obj, instance.get_property_group_name()):
            del obj[instance.get_property_group_name()]

        # If this was the last component on the object, remove from the synced scene.
        if len(self.objects[obj_name]) < 1:
            self.on_destroy_object(obj_name)
            del self.objects[obj_name]

    def _find_meta(self, obj, name: str):
        return next((x for x in obj.coherence.components if x.name == name), None)

    def _find_or_create_meta(self, obj, name: str):
        meta = self._find_meta(obj, name)
        if not meta:
            meta = obj.coherence.components.add()
            meta.name = name
            meta.enabled = True

        return meta

    def _remove_meta(self, obj, component_name: str):
        """Remove meta state for a named component"""
        prop = obj.coherence.components

        for i in range(len(prop)):
            if prop[i].name == component_name:
                prop.remove(i)
                return

    def sync_objects_with_scene(self, scene):
        """Add and remove objects based on whether they exist in the current scene

        Args:
            scene (bpy.types.Scene)
        """
        active = set()

        # Check for added objects
        for obj in scene.objects:
            active.add(obj.name)
            if obj.name not in self.current_names:
                self.on_new_object(obj)

        # Check for removed objects
        removed = self.current_names - active
        for obj_name in removed:
            self.destroy_object(obj_name)

        # TODO: Also include anything referenced by collections
        # into other scenes
        self.current_names = active

    def dispatch_component_message(self, component_name: str, message):
        """
        Args:
            component_name (str)
            message (InteropComponentMessage)
        """
        for instance in self.objects[message.target]:
            if instance.name() == component_name:
                instance._dispatch(message)

    def on_add_object(self, obj):
        """
        Args:
            obj (bpy.types.Object):
        """
        lib.AddObjectToScene(
            get_string_buffer(obj.name),
            get_string_buffer('UNUSED'), # Type
            to_interop_transform(obj)
        )

        # Blender treats renamed objects as remove + add.
        # Unfortunately this doesn't propagate any change events to children,
        # so we need to manually trigger a transform update for everything parented
        # to this object so they can all update their parent name to match.
        for child in obj.children:
            if child.name in self.objects:
                lib.SetObjectTransform(
                    get_string_buffer(child.name),
                    to_interop_transform(child)
                )

        # If any of the components have a mesh - send that as well.
        # ... no, I need to batch for a depsgraph update all in one...
        print('Called on_add_object on {}'.format(obj))

    def on_destroy_object(self, obj_name: str):
        """
        Args:
            obj_name (str):
        """
        lib.RemoveObjectFromScene(
            get_string_buffer(obj_name)
        )

    def on_destroy_component(self, obj_name: str, component_name: str):
        """
        Args:
            obj_name (str):
            component_name (str):
        """
        # If this was the last component - stop syncing the object altogether
        if len(self.objects[obj_name]) < 1:
            lib.RemoveObjectFromScene(
                get_string_buffer(obj_name)
            )
        else: # Just destroy the component
            lib.DestroyComponent(
                get_string_buffer(obj_name),
                get_string_buffer(component_name)
            )
            pass

    def on_add_component(self, obj: str, component):
        """
        Args:
            obj (bpy.types.Object):
            component (Component):
        """
        # If this was the first component added - start syncing the object
        if len(self.objects[obj.name]) < 2:
            self.on_add_object(obj)

        lib.AddComponent(
            get_string_buffer(obj.name),
            get_string_buffer(component.name()),
            component.enabled
        )

    def on_component_error(self, component_name: str, err, obj_name = None):
        """Log an error raised from within a component callback

        Args:
            component_name (str):   The component that threw the error
            err (Exception):        The error that was thrown
            obj_name (str|None):    Optional object context for the error
        """
        error('Exception through in component={}, context={}: {}'.format(
            component_name,
            obj_name,
            err
        ))
        # TODO: Log somewhere more visible

    def on_update_material(self, mat):
        """
        Args:
            mat (bpy.types.Material)
        """
        debug('on_update_material - name={}'.format(mat.name))

        # TODO: How do I refactor this? Each object needs to update
        # themselves to reference the new material somehow...

        # # Fire off an update for all objects that are using this material
        # for bpy_obj in bpy.context.scene.objects:
        #     if bpy_obj.active_material == mat:
        #         obj = self.objects.find_by_bpy_name(bpy_obj.name)
        #         if obj: obj.update_properties()

    def on_depsgraph_update(self, scene, depsgraph):
        """Sync the objects with the scene's dependency graph on each update

        Args:
            scene (bpy.types.Scene)
            depsgraph (bpy.types.Depsgraph)
        """
        debug('DEPSGRAPH UPDATE')

        self.sync_objects_with_scene(scene)

        for update in depsgraph.updates:
            if type(update.id) == bpy.types.Material:
                self.on_update_material(update.id)

            elif type(update.id) == bpy.types.Object:
                components = self.objects.get(update.id.name)
                if not components: continue

                if update.is_updated_transform:
                    update_transform(update.id)

                if update.is_updated_geometry:
                    for instance in components:
                        mesh_uid = instance.mesh_uid
                        if mesh_uid:
                            # Add one component handler per mesh UID.
                            # For instanced meshes, this allows us to only have
                            # *one* component push the mesh update to Coherence.
                            # This also allows multiple components to manage
                            # independent meshes on the same scene object.
                            self.dirty_geometry[mesh_uid] = instance

        # Handle all unique geometry updates
        for component in self.dirty_geometry.values():
            try:
                component.on_update_mesh(depsgraph)
            except Exception as err:
                self.on_component_error(component.name(), err, component.object_name)
                pass

        self.dirty_geometry.clear()

    def on_registered(self):
        #self.sync_objects_with_scene(bpy.data.scenes[0])
        # TODO: No scene context - error:
        # AttributeError: '_RestrictContext' object has no attribute 'scene'
        # Same for trying to do in bpy.data.scenes
        # (probably because this is happening when coherence itself registers
        # so there's no good context to pull the scene from yet)

        # Need a better solution here - some kind of late call or something.
        pass

    def on_unregistered(self):
        lib.Clear()
        self.unregister_all_components()

    def on_start(self):
        # Sync the current scene state
        self.sync_objects_with_scene(bpy.context.scene)

        for component in self.all_components():
            component.on_coherence_start()

    def on_stop(self):
        for component in self.all_components():
            component.on_coherence_stop()

        # TODO: Necessary? We still want to persist state between stop/start.
        # lib.Clear()

    def on_connected(self):
        for component in self.all_components():
            component.on_coherence_connected()

    def on_disconnected(self):
        for component in self.all_components():
            component.on_coherence_disconnected()

    def on_message(self, message):
        """Forward inbound messages to components

        Args:
            message (InteropMessage)
        """
        component_msg = message.as_component_message()
        if not component_msg:
            return

        try:
            self.dispatch_component_message(message.target, component_msg)
        except KeyError as e:
            error('Error while routing message to component [{}]'.format(message.target, e))
