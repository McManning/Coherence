using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Coherence
{
    /// <summary>
    /// Interface for properties that can be synced between applications.
    /// </summary>
    internal interface ISyncedProperty
    {
        string Name { get; }

        void CreateDelegates(IComponent obj, PropertyInfo prop);

        void FromInterop(InteropProperty prop);

        InteropProperty ToInterop();
    }

    internal abstract class SyncedProperty<T>
    {
        public string Name { get; private set; }

        internal Action<T> setter;
        internal Func<T> getter;

        public void CreateDelegates(IComponent component, PropertyInfo prop)
        {
            // Property names are standardized to lowercase alphanumeric only.
            Name = Regex.Replace(prop.Name, "[^A-Za-z0-9]", "").ToLower();

            var getterMethod = prop.GetGetMethod(true);
            var setterMethod = prop.GetSetMethod(true);

            if (getterMethod == null || setterMethod == null)
            {
                throw new NotSupportedException();
            }

            getter = (Func<T>)Delegate.CreateDelegate(
                typeof(Func<T>),
                component,
                getterMethod
            );

            setter = (Action<T>)Delegate.CreateDelegate(
                typeof(Action<T>),
                component,
                setterMethod
            );
        }
    }

    internal class SyncedBoolProperty : SyncedProperty<bool>, ISyncedProperty
    {
        public void FromInterop(InteropProperty prop) => setter(prop.intValue == 1);

        public InteropProperty ToInterop()
        {
            return new InteropProperty
            {
                name = new InteropString64(Name),
                type = InteropPropertyType.Boolean,
                intValue = getter() ? 1 : 0
            };
        }
    }

    internal class SyncedIntProperty : SyncedProperty<int>, ISyncedProperty
    {
        public void FromInterop(InteropProperty prop) => setter(prop.intValue);

        public InteropProperty ToInterop()
        {
            return new InteropProperty
            {
                name = new InteropString64(Name),
                type = InteropPropertyType.Integer,
                intValue = getter()
            };
        }
    }

    internal class SyncedFloatProperty : SyncedProperty<float>, ISyncedProperty
    {
        public void FromInterop(InteropProperty prop) => setter(prop.vectorValue.x);

        public InteropProperty ToInterop()
        {
            return new InteropProperty
            {
                name = new InteropString64(Name),
                type = InteropPropertyType.Float,
                vectorValue = new InteropVector4(getter(), 0, 0, 0)
            };
        }
    }

    internal class SyncedStringProperty : SyncedProperty<string>, ISyncedProperty
    {
        public void FromInterop(InteropProperty prop) => setter(prop.stringValue);

        public InteropProperty ToInterop()
        {
            return new InteropProperty
            {
                name = new InteropString64(Name),
                type = InteropPropertyType.String,
                stringValue = new InteropString64(getter())
            };
        }
    }

    internal class SyncedVector2Property : SyncedProperty<Vector2>, ISyncedProperty
    {
        public void FromInterop(InteropProperty prop)
        {
            var v = prop.vectorValue;
            setter(new Vector2(v.x, v.y));
        }

        public InteropProperty ToInterop()
        {
            var v = getter();
            return new InteropProperty
            {
                name = new InteropString64(Name),
                type = InteropPropertyType.FloatVector2,
                vectorValue = new InteropVector4(v.x, v.y, 0, 0)
            };
        }
    }

    internal class SyncedVector3Property : SyncedProperty<Vector3>, ISyncedProperty
    {
        public void FromInterop(InteropProperty prop)
        {
            var v = prop.vectorValue;
            setter(new Vector3(v.x, v.y, v.z));
        }

        public InteropProperty ToInterop()
        {
            var v = getter();
            return new InteropProperty
            {
                name = new InteropString64(Name),
                type = InteropPropertyType.FloatVector3,
                vectorValue = new InteropVector4(v.x, v.y, v.z, 0)
            };
        }
    }

    internal class SyncedVector4Property : SyncedProperty<Vector4>, ISyncedProperty
    {
        public void FromInterop(InteropProperty prop)
        {
            var v = prop.vectorValue;
            setter(new Vector4(v.x, v.y, v.z, v.w));
        }

        public InteropProperty ToInterop()
        {
            var v = getter();
            return new InteropProperty
            {
                name = new InteropString64(Name),
                type = InteropPropertyType.FloatVector4,
                vectorValue = new InteropVector4(v.x, v.y, v.z, v.w)
            };
        }
    }

    internal class SyncedColorProperty : SyncedProperty<Color>, ISyncedProperty
    {
        public void FromInterop(InteropProperty prop)
        {
            var v = prop.vectorValue;
            setter(new Color(v.x, v.y, v.z));
        }

        public InteropProperty ToInterop()
        {
            var v = getter();
            return new InteropProperty
            {
                name = new InteropString64(Name),
                type = InteropPropertyType.Color,
                vectorValue = new InteropVector4(v.r, v.g, v.b, 1)
            };
        }
    }

    internal class SyncedEnumProperty<T> : SyncedProperty<T>, ISyncedProperty
    {
        public void FromInterop(InteropProperty prop)
        {
            setter((T)Enum.Parse(typeof(T), prop.stringValue, true));
        }

        public InteropProperty ToInterop()
        {
            var value = getter();
            return new InteropProperty
            {
                name = new InteropString64(Name),
                type = InteropPropertyType.String, // Technically passed as a string.
                stringValue = value.ToString()
            };
        }
    }

    /// <summary>
    /// Factory for instantiating new concrete <see cref="ISyncedProperty"/>
    /// implementations already bound to the given component instance and property.
    /// </summary>
    internal class SyncedPropertyFactory
    {
        public static ISyncedProperty Create(IComponent component, PropertyInfo prop)
        {
            var instance = Instantiate(prop.PropertyType);
            if (instance == null)
            {
                throw new NotSupportedException(
                    $"Property [{prop.Name}] of type [{prop.PropertyType}] is not supported"
                );
            }

            instance.CreateDelegates(component, prop);
            return instance;
        }

        private static ISyncedProperty Instantiate(Type type)
        {
            if (type.IsEnum)
            {
                var enumType = typeof(SyncedEnumProperty<>).MakeGenericType(type);
                return Activator.CreateInstance(enumType) as ISyncedProperty;
            }

            if (type == typeof(bool))
                return new SyncedBoolProperty();

            if (type == typeof(int))
                return new SyncedIntProperty();

            if (type == typeof(float))
                return new SyncedFloatProperty();

            if (type == typeof(string))
                return new SyncedStringProperty();

            if (type == typeof(Vector2))
                return new SyncedVector2Property();

            if (type == typeof(Vector3))
                return new SyncedVector3Property();

            if (type == typeof(Vector4))
                return new SyncedVector4Property();

            if (type == typeof(Color))
                return new SyncedColorProperty();

            // TODO: enums pass as strings through Blender. Need to resolve.
            // Ideally blender should convert it to an int and we can
            // convert it as an enum by checking the prop type.

            return null;
        }
    }

    /// <summary>
    /// Tracking and metadata for Coherence components
    /// </summary>
    internal class ComponentInfo
    {
        /// <summary>
        /// Get a list of all components, regardless of registration state
        /// </summary>
        internal static Dictionary<string, ComponentInfo> Infos {
            get {
                // If we came out of an assembly reload, try to restore.
                if (infos == null)
                {
                    LoadComponentsFromAssemblies();
                }
                return infos;
            }
        }

        private static Dictionary<string, ComponentInfo> infos;

        internal static ComponentInfo Find(string name)
        {
            // TODO: Throw or something
            return Infos[name];
        }

        internal string Name { get; set; }

        internal Type Type { get; set; }

        /// <summary>
        /// Methods that can handle Coherence events (e.g. OnConnected, OnDisconnected)
        /// </summary>
        internal Dictionary<string, MethodInfo> EventLookupTable { get; } = new Dictionary<string, MethodInfo>();

        internal HashSet<PropertyInfo> Properties { get; } = new HashSet<PropertyInfo>();

        internal HashSet<FieldInfo> Fields { get; } = new HashSet<FieldInfo>();

        private static void LoadComponentsFromAssemblies()
        {
            infos = new Dictionary<string, ComponentInfo>();
            var componentType = typeof(IComponent);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (componentType.IsAssignableFrom(type) && !type.IsInterface)
                    {
                        Add(type);
                    }
                }
            }
        }

        internal static void Add(Type type)
        {
            var attr = type.GetCustomAttribute<ComponentAttribute>();
            if (attr == null)
            {
                Debug.LogError("missing attr"); // TODO: message
                return;
            }

            if (infos.ContainsKey(attr.Name))
            {
                Debug.Log("Already registered: " + attr.Name); // TODO: Error?
                return;
            }

            var info = new ComponentInfo
            {
                Name = attr.Name,
                Type = type
            };

            // Load all On* event handlers into a lookup table
            foreach (var method in type.GetMethods())
            {
                if (method.Name.StartsWith("On"))
                {
                    info.EventLookupTable.Add(method.Name, method);
                }
            }

            // TODO: Is there any performance boost in doing this
            // then caching? Or is it fast enough to do on-demand
            // per instance?
            foreach (var prop in type.GetProperties())
            {
                info.Properties.Add(prop);
            }

            foreach (var field in type.GetFields())
            {
                info.Fields.Add(field);
            }

            Infos.Add(attr.Name, info);
        }
    }
}
