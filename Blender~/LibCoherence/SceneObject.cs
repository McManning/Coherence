using System;
using System.Collections.Generic;

namespace Coherence
{
    /// <summary>
    /// A distinct object in a blender scene
    /// </summary>
    class SceneObject : IInteropSerializable<InteropSceneObject>
    {
        /// <summary>
        /// Data that will be shared with Unity
        /// </summary>
        internal InteropSceneObject data;

        public string Name { get; set; }

        /// <summary>
        /// Name of the active material for this object
        /// </summary>
        internal string Material
        {
            get { return data.material; }
            set { data.material = value; }
        }

        internal InteropMatrix4x4 Transform
        {
            get { return data.transform; }
            set { data.transform = value; }
        }

        internal uint[] Triangles
        {
            get { return triangles; }
        }

        private uint[] triangles;

        internal InteropVector3[] Vertices
        {
            get { return vertices; }
        }

        private InteropVector3[] vertices;

        internal InteropVector3[] Normals
        {
            get { return normals; }
        }

        private InteropVector3[] normals;

        internal InteropBoneWeight[] BoneWeights { get; set; }

        private Dictionary<int, InteropVector2[]> uvLayers;
        private MLoop[] cachedLoops;

        internal SceneObject(string name, SceneObjectType type)
        {
            Name = name;

            data = new InteropSceneObject
            {
                name = name,
                type = type,
                material = "Default"
            };

            uvLayers = new Dictionary<int, InteropVector2[]>();
        }

        /// <summary>
        /// Retrieve a specific UV layer
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        internal InteropVector2[] GetUV(int layer)
        {
            if (uvLayers.ContainsKey(layer))
            {
                return uvLayers[layer];
            }

            return null;
        }

        /// <summary>
        /// Copy an <see cref="MLoop"/> array into managed memory for vertex lookups
        /// aligned with other MLoop* structures.
        /// </summary>
        /// <param name="loops"></param>
        internal void CopyFromMLoops(MLoop[] loops)
        {
            if (cachedLoops == null)
            {
                cachedLoops = new MLoop[loops.Length];
            }
            else
            {
                Array.Resize(ref cachedLoops, loops.Length);
            }

            Array.Copy(loops, cachedLoops, loops.Length);

            InteropLogger.Debug($"Copy {loops.Length} loops");
        }

        /// <summary>
        /// Copy an <see cref="MVert"/> array into <see cref="Vertices"/> and <see cref="Normals"/>.
        /// </summary>
        /// <param name="verts"></param>
        internal void CopyFromMVerts(MVert[] verts)
        {
            var count = verts.Length;
            if (vertices == null)
            {
                vertices = new InteropVector3[count];
            }

            if (normals == null)
            {
                normals = new InteropVector3[count];
            }

            Array.Resize(ref vertices, count);
            Array.Resize(ref normals, count);

            // We're copying the data instead of sending directly into shared memory
            // because we cannot guarantee that:
            //  1.  shared memory will be available for write when this is called
            //  2.  the source MVert array hasn't been reallocated once shared
            //      memory *is* available for write

            // TODO: "Smart" dirtying.
            // If verts don't change, don't flag for updates.
            // Or if verts change, only update a region of the verts.
            // Could do a BeginChangeCheck() ... EndChangeCheck() with
            // a bool retval if changes happened.

            // The idea is to do as much work as possible locally within
            // Blender to transmit the smallest deltas to Unity.

            // On a resize - this is just everything.
            int rangeStart = count;
            int rangeEnd = 0;
            int changedVertexCount = 0;

            for (int i = 0; i < count; i++)
            {
                var co = verts[i].co;
                var no = verts[i].no;

                var newVert = new InteropVector3(co[0], co[1], co[2]);

                // Normals need to be cast from short -> float
                var newNorm = new InteropVector3(no[0] / 32767f, no[1] / 32767f, no[2] / 32767f);

                if (!newVert.Approx(vertices[i]) || !newNorm.Approx(normals[i]))
                {
                    rangeStart = Math.Min(rangeStart, i);
                    rangeEnd = Math.Max(rangeEnd, i);
                    changedVertexCount++;
                }

                vertices[i] = newVert;
                normals[i] = newNorm;

                // TECHNICALLY I can collect an index list of what changed and send just
                // that to Unity - but is it really worth the extra effort? We'll see!

                // Console.WriteLine($" - v={vertices[i]}, n={normals[i]}");

                // Other issue is that changes may affect index 0, 1, and 1000, 1001 for a quad.
                // Or a change has shifted vertices around in the array due to some sort of
                // esoteric Blender operator. So to play it safe, we just update the whole array
                // until it can be guaranteed that we can identify an accurate slice of updates.
            }

            InteropLogger.Debug(
                $"** Changed {changedVertexCount} vertices within index range " +
                $"[{rangeStart}, {rangeEnd}] covering {rangeEnd - rangeStart} vertices"
            );
        }

        /// <summary>
        /// Copy an <see cref="MLoopTri"/> array into <see cref="Triangles"/>
        ///
        /// <para>
        ///     You <b>must</b> ensure <see cref="cachedLoops"/> is up to date via
        ///     <see cref="CopyFromMVerts(MVert[])"/> before performing this
        ///     operation to ensure that vertices can be mapped to.
        /// </para>
        /// </summary>
        /// <param name="loopTris"></param>
        internal void CopyFromMLoopTris(MLoopTri[] loopTris)
        {
            var count = loopTris.Length * 3;
            if (triangles == null)
            {
                triangles = new uint[count];
            }

            Array.Resize(ref triangles, count);

            // Triangle indices are re-mapped from MLoop vertex index
            // to the source vertex index using cachedLoops.
            for (int i = 0; i < loopTris.Length; i++)
            {
                var loop = loopTris[i];
                var j = i * 3;

                // TODO: Can this be any faster? This indirect lookup sucks,
                // but might be the best we can do with the way Blender
                // organizes data.
                triangles[j] = cachedLoops[loop.tri[0]].v;
                triangles[j + 1] = cachedLoops[loop.tri[1]].v;
                triangles[j + 2] = cachedLoops[loop.tri[2]].v;

                // Console.WriteLine($" - {triangles[j]}, {triangles[j + 1]},  {triangles[j + 2]}");
            }

            InteropLogger.Debug($"Copied {triangles.Length} triangle indices");
        }

        /// <summary>
        /// Copy an <see cref="MLoopUV"/> array for <see cref="GetUV(int)"/>.
        ///
        /// <para>
        ///     You <b>must</b> ensure <see cref="cachedLoops"/> is up to date via
        ///     <see cref="CopyFromMVerts(MVert[])"/> before performing this
        ///     operation to ensure that vertices can be mapped to.
        /// </para>
        /// </summary>
        /// <param name="layer"></param>
        /// <param name="loopUVs"></param>
        internal void CopyFromMLoopUV(int layer, MLoopUV[] loopUVs)
        {
            var count = vertices.Length;

            if (!uvLayers.TryGetValue(layer, out InteropVector2[] uv))
            {
                uv = new InteropVector2[count];
            }

            Array.Resize(ref uv, count);
            uvLayers[layer] = uv;

            for (int i = 0; i < loopUVs.Length; i++)
            {
                // Note that this currently does not support
                // different UVs in the same layer for the same vertex
                // since we crush them down and don't try to split verts.
                // TODO: Improve on this.
                var v = cachedLoops[i].v; // Vertex index
                uv[v] = new InteropVector2(loopUVs[i].uv);
            }
        }

        public InteropSceneObject Serialize()
        {
            return data;
        }

        public void Deserialize(InteropSceneObject interopData)
        {
            throw new InvalidOperationException();
        }
    }
}
