// The two Unity types the game's spring structs are declared with, and the one CySpring type they mention.
//
// The structs and the managed solver are compiled from the viewer's own copies (see the .csproj), so this
// file supplies the engine maths those sources use: Vector3/Quaternion arithmetic. It follows Unity's
// documented behaviour rather than any particular implementation detail - a zero-length normalize gives a
// zero vector, not NaN - because that is what the solver's branches assume.

using System;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 down => new Vector3(0f, -1f, 0f);

        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);

        public float sqrMagnitude => x * x + y * y + z * z;

        public Vector3 normalized
        {
            get
            {
                float length = magnitude;
                return length > 1e-9f ? new Vector3(x / length, y / length, z / length) : zero;
            }
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);

        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);

        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);

        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);

        public static Vector3 operator *(float s, Vector3 a) => a * s;

        public static Vector3 operator /(Vector3 a, float s) => new Vector3(a.x / s, a.y / s, a.z / s);

        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);

        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        public static Vector3 Cross(Vector3 a, Vector3 b) =>
            new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;

        public override string ToString() => $"({x:F3}, {y:F3}, {z:F3})";
    }

    public struct Quaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static Quaternion identity => new Quaternion(0f, 0f, 0f, 1f);

        /// <summary>q1 * q2: applying q2 first, then q1.</summary>
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            float x2 = q.x * 2f, y2 = q.y * 2f, z2 = q.z * 2f;
            float xx = q.x * x2, yy = q.y * y2, zz = q.z * z2;
            float xy = q.x * y2, xz = q.x * z2, yz = q.y * z2;
            float wx = q.w * x2, wy = q.w * y2, wz = q.w * z2;

            return new Vector3(
                (1f - (yy + zz)) * v.x + (xy - wz) * v.y + (xz + wy) * v.z,
                (xy + wz) * v.x + (1f - (xx + zz)) * v.y + (yz - wx) * v.z,
                (xz - wy) * v.x + (yz + wx) * v.y + (1f - (xx + yy)) * v.z);
        }

        /// <summary>The rotation that turns <paramref name="from"/> onto <paramref name="to"/>.</summary>
        public static Quaternion FromToRotation(Vector3 from, Vector3 to)
        {
            Vector3 a = from.normalized;
            Vector3 b = to.normalized;
            float d = Vector3.Dot(a, b);

            if (d >= 1f - 1e-6f) return identity;

            if (d <= -1f + 1e-6f)
            {
                // opposite: any axis perpendicular to `a` will do
                Vector3 axis = Vector3.Cross(a, new Vector3(1f, 0f, 0f));
                if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(a, new Vector3(0f, 1f, 0f));
                axis = axis.normalized;
                return new Quaternion(axis.x, axis.y, axis.z, 0f);
            }

            Vector3 c = Vector3.Cross(a, b);
            var q = new Quaternion(c.x, c.y, c.z, 1f + d);
            return q.Normalized();
        }

        public Quaternion Normalized()
        {
            float length = (float)Math.Sqrt(x * x + y * y + z * z + w * w);
            return length > 1e-9f
                ? new Quaternion(x / length, y / length, z / length, w / length)
                : identity;
        }

        public override string ToString() => $"({x:F3}, {y:F3}, {z:F3}, {w:F3})";
    }
}

/// <summary>The capability flags a spring bone carries; the runtime class itself is not needed here.</summary>
public class CySpring
{
    public enum eCapability
    {
        None,
        GravityControl,
        WindForce
    }
}
