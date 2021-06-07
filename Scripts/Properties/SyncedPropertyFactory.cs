using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Coherence
{
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

            return null;
        }
    }
}
