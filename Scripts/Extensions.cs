
using UnityEngine;

namespace Coherence
{
    public static class TransformExtensions
    {
        public static void FromInteropMatrix4x4(this Transform transform, InteropMatrix4x4 matrix)
        {
            transform.localScale = matrix.Scale();
            transform.rotation = matrix.Rotation();
            transform.position = matrix.Position();
        }
    }

    public static class InteropVector3Extensions
    {
        public static Vector3 ToVector3(this InteropVector3 vec)
        {
            return new Vector3(vec.x, vec.y, vec.z);
        }
    }

    public static class InteropQuaternionExtensions
    {
        public static Quaternion ToQuaternion(this InteropQuaternion q)
        {
            return new Quaternion(q.x, q.y, q.z, q.w);
        }
    }

    /// <summary>
    /// Extension methods that are Unity-only
    /// </summary>
    public static class InteropMatrix4x4Extensions
    {
        public static Quaternion Rotation(this InteropMatrix4x4 matrix)
        {
            Vector3 forward;
            forward.x = matrix.m02;
            forward.y = matrix.m12;
            forward.z = matrix.m22;

            Vector3 upwards;
            upwards.x = matrix.m01;
            upwards.y = matrix.m11;
            upwards.z = matrix.m21;

            return Quaternion.LookRotation(forward, upwards);
        }

        public static Vector3 Position(this InteropMatrix4x4 matrix)
        {
            Vector3 position;
            position.x = matrix.m03;
            position.y = matrix.m13;
            position.z = matrix.m23;
            return position;
        }

        public static Vector3 Scale(this InteropMatrix4x4 matrix)
        {
            Vector3 scale;
            scale.x = new Vector4(matrix.m00, matrix.m10, matrix.m20, matrix.m30).magnitude;
            scale.y = new Vector4(matrix.m01, matrix.m11, matrix.m21, matrix.m31).magnitude;
            scale.z = new Vector4(matrix.m02, matrix.m12, matrix.m22, matrix.m32).magnitude;
            return scale;
        }
    }
}
