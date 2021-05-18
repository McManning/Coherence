using System;

namespace Coherence
{
    /// <summary>
    /// Required attribute for any MonoBehaviours implementing <see cref="IComponent"/>.
    ///
    /// <para>
    ///     This defines metadata for associating the MonoBehaviour with a specific
    ///     Blender component and SceneObject.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class ComponentAttribute : Attribute
    {
        /// <summary>
        /// The name of the Blender component (required).
        ///
        /// This allows messaging to target the correct component and GameObjects.
        /// </summary>
        public string Name { get; set; }

        public ComponentAttribute(string name)
        {
            Name = name;
        }
    }
}
