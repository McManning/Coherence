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
}
