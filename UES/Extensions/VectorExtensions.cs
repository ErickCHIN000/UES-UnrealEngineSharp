using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace UES.Extensions
{
    /// <summary>
    /// Vector3 with double precision for high-precision calculations
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Vector3Double
    {
        public double X;
        public double Y;
        public double Z;

        public Vector3Double(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Converts double precision vector to single precision Vector3
        /// </summary>
        public Vector3 ToFloats()
        {
            return new Vector3((float)X, (float)Y, (float)Z);
        }

        public static implicit operator Vector3Double(Vector3 v)
        {
            return new Vector3Double(v.X, v.Y, v.Z);
        }

        public static implicit operator Vector3(Vector3Double v)
        {
            return v.ToFloats();
        }
    }

    /// <summary>
    /// Transform structure representing position, rotation, and scale
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Transform
    {
        public Vector3Double Rotation;
        public double RotationW;
        public Vector3Double Translation;
        public double TranslationW;
        public Vector3Double Scale;
        public double ScaleW;

        /// <summary>
        /// Converts transform to a 4x4 matrix with scale applied
        /// </summary>
        public Matrix4x4 ToMatrixWithScale()
        {
            var x2 = Rotation.X + Rotation.X;
            var y2 = Rotation.Y + Rotation.Y;
            var z2 = Rotation.Z + Rotation.Z;

            var xx2 = Rotation.X * x2;
            var yy2 = Rotation.Y * y2;
            var zz2 = Rotation.Z * z2;

            var yz2 = Rotation.Y * z2;
            var wx2 = RotationW * x2;

            var xy2 = Rotation.X * y2;
            var wz2 = RotationW * z2;

            var xz2 = Rotation.X * z2;
            var wy2 = RotationW * y2;

            var m = new Matrix4x4
            {
                M41 = (float)Translation.X,
                M42 = (float)Translation.Y,
                M43 = (float)Translation.Z,
                M11 = (float)((1.0f - (yy2 + zz2)) * Scale.X),
                M22 = (float)((1.0f - (xx2 + zz2)) * Scale.Y),
                M33 = (float)((1.0f - (xx2 + yy2)) * Scale.Z),
                M32 = (float)((yz2 - wx2) * Scale.Z),
                M23 = (float)((yz2 + wx2) * Scale.Y),
                M21 = (float)((xy2 - wz2) * Scale.Y),
                M12 = (float)((xy2 + wz2) * Scale.X),
                M31 = (float)((xz2 + wy2) * Scale.Z),
                M13 = (float)((xz2 - wy2) * Scale.X),
                M14 = 0.0f,
                M24 = 0.0f,
                M34 = 0.0f,
                M44 = 1.0f
            };
            return m;
        }

        /// <summary>
        /// Alternative matrix conversion returning a 2D array
        /// </summary>
        public float[,] ToMatrixWithScale2()
        {
            var m = new float[4, 4];

            m[3, 0] = (float)Translation.X;
            m[3, 1] = (float)Translation.Y;
            m[3, 2] = (float)Translation.Z;

            var x2 = Rotation.X * 2;
            var y2 = Rotation.Y * 2;
            var z2 = Rotation.Z * 2;

            var xx2 = Rotation.X * x2;
            var yy2 = Rotation.Y * y2;
            var zz2 = Rotation.Z * z2;
            m[0, 0] = (float)((1.0f - (yy2 + zz2)) * Scale.X);
            m[1, 1] = (float)((1.0f - (xx2 + zz2)) * Scale.Y);
            m[2, 2] = (float)((1.0f - (xx2 + yy2)) * Scale.Z);

            var yz2 = Rotation.Y * z2;
            var wx2 = RotationW * x2;
            m[2, 1] = (float)((yz2 - wx2) * Scale.Z);
            m[1, 2] = (float)((yz2 + wx2) * Scale.Y);

            var xy2 = Rotation.X * y2;
            var wz2 = RotationW * z2;
            m[1, 0] = (float)((xy2 - wz2) * Scale.Y);
            m[0, 1] = (float)((xy2 + wz2) * Scale.X);

            var xz2 = Rotation.X * z2;
            var wy2 = RotationW * y2;
            m[2, 0] = (float)((xz2 + wy2) * Scale.Z);
            m[0, 2] = (float)((xz2 - wy2) * Scale.X);

            m[0, 3] = 0.0f;
            m[1, 3] = 0.0f;
            m[2, 3] = 0.0f;
            m[3, 3] = 1.0f;

            return m;
        }
    }

    /// <summary>
    /// Extension methods for Vector3 operations commonly used in game development
    /// </summary>
    public static class VectorExtensions
    {
        /// <summary>
        /// Calculates dot product of two vectors
        /// </summary>
        public static float Mult(this Vector3 v, Vector3 s) => v.X * s.X + v.Y * s.Y + v.Z * s.Z;

        /// <summary>
        /// Extracts the axis vectors from a rotation matrix
        /// </summary>
        public static void GetAxes(this Vector3 v, out Vector3 x, out Vector3 y, out Vector3 z)
        {
            var m = v.ToMatrix();

            x = new Vector3(m[0, 0], m[0, 1], m[0, 2]);
            y = new Vector3(m[1, 0], m[1, 1], m[1, 2]);
            z = new Vector3(m[2, 0], m[2, 1], m[2, 2]);
        }

        /// <summary>
        /// Converts rotation (pitch, yaw, roll) to forward direction vector
        /// </summary>
        public static Vector3 FromRotator(this Vector3 v)
        {
            float radPitch = (float)(v.X * Math.PI / 180f);
            float radYaw = (float)(v.Y * Math.PI / 180f);
            float SP = (float)Math.Sin(radPitch);
            float CP = (float)Math.Cos(radPitch);
            float SY = (float)Math.Sin(radYaw);
            float CY = (float)Math.Cos(radYaw);
            return new Vector3(CP * CY, CP * SY, SP);
        }

        /// <summary>
        /// Converts rotation vector to transformation matrix
        /// </summary>
        public static float[,] ToMatrix(this Vector3 v, Vector3 origin = default)
        {
            if (origin == default)
                origin = Vector3.Zero;
            
            var radPitch = (float)(v.X * Math.PI / 180f);
            var radYaw = (float)(v.Y * Math.PI / 180f);
            var radRoll = (float)(v.Z * Math.PI / 180f);

            var SP = (float)Math.Sin(radPitch);
            var CP = (float)Math.Cos(radPitch);
            var SY = (float)Math.Sin(radYaw);
            var CY = (float)Math.Cos(radYaw);
            var SR = (float)Math.Sin(radRoll);
            var CR = (float)Math.Cos(radRoll);

            var m = new float[4, 4];
            m[0, 0] = CP * CY;
            m[0, 1] = CP * SY;
            m[0, 2] = SP;
            m[0, 3] = 0f;

            m[1, 0] = SR * SP * CY - CR * SY;
            m[1, 1] = SR * SP * SY + CR * CY;
            m[1, 2] = -SR * CP;
            m[1, 3] = 0f;

            m[2, 0] = -(CR * SP * CY + SR * SY);
            m[2, 1] = CY * SR - CR * SP * SY;
            m[2, 2] = CR * CP;
            m[2, 3] = 0f;

            m[3, 0] = origin.X;
            m[3, 1] = origin.Y;
            m[3, 2] = origin.Z;
            m[3, 3] = 1f;
            
            return m;
        }

        /// <summary>
        /// Calculates rotation needed to look from source to destination
        /// </summary>
        public static Vector3 CalcRotation(this Vector3 source, Vector3 destination, Vector3 origAngles, float smooth)
        {
            var angles = new Vector3();
            var diff = source - destination;
            var hyp = Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y);
            
            angles.Y = (float)Math.Atan(diff.Y / diff.X) * 57.295779513082f;
            angles.X = -(float)Math.Atan(diff.Z / hyp) * 57.295779513082f;
            angles.Z = 0.0f;
            
            if (diff.X >= 0.0)
            {
                if (angles.Y > 0)
                    angles.Y -= 180.0f;
                else
                    angles.Y += 180.0f;
            }
            
            if (smooth > 0 && Math.Abs(angles.Y - origAngles.Y) < 180.0f)
                angles -= ((angles - origAngles) * smooth);
            
            return angles;
        }
    }
}