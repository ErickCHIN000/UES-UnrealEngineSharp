using System;
using System.Numerics;

namespace UES.Extensions
{
    /// <summary>
    /// Utilities for converting 3D world coordinates to 2D screen coordinates
    /// Essential for overlays, ESP, and game hacking features
    /// </summary>
    public static class WorldToScreen
    {
        // Cached rotation values to avoid recalculation
        private static Vector3 _lastRotation = Vector3.Zero;
        private static Vector3 _vAxisX = Vector3.Zero;
        private static Vector3 _vAxisY = Vector3.Zero;
        private static Vector3 _vAxisZ = Vector3.Zero;

        /// <summary>
        /// Converts a 3D world position to 2D screen coordinates
        /// </summary>
        /// <param name="worldLocation">3D position in world space</param>
        /// <param name="cameraLocation">Camera position in world space</param>
        /// <param name="cameraRotation">Camera rotation (pitch, yaw, roll)</param>
        /// <param name="fieldOfView">Camera field of view in degrees</param>
        /// <param name="screenCenterX">Screen center X coordinate</param>
        /// <param name="screenCenterY">Screen center Y coordinate</param>
        /// <returns>2D screen coordinates</returns>
        public static Vector2 Convert(Vector3 worldLocation, Vector3 cameraLocation, Vector3 cameraRotation, 
            float fieldOfView, int screenCenterX, int screenCenterY)
        {
            // Cache rotation calculations if camera hasn't moved
            if (_lastRotation != cameraRotation)
            {
                cameraRotation.GetAxes(out _vAxisX, out _vAxisY, out _vAxisZ);
                _lastRotation = cameraRotation;
            }

            var vDelta = worldLocation - cameraLocation;
            var vTransformed = new Vector3(vDelta.Mult(_vAxisY), vDelta.Mult(_vAxisZ), vDelta.Mult(_vAxisX));
            
            // Ensure we don't divide by zero or get behind camera
            if (vTransformed.Z < 1f) 
                vTransformed.Z = 1f;

            var projectionFactor = screenCenterX / (float)Math.Tan(fieldOfView * (float)Math.PI / 360);
            
            var fullScreen = new Vector2(
                screenCenterX + vTransformed.X * projectionFactor / vTransformed.Z,
                screenCenterY - vTransformed.Y * projectionFactor / vTransformed.Z
            );

            return fullScreen;
        }

        /// <summary>
        /// Converts multiple world positions to screen coordinates efficiently
        /// </summary>
        /// <param name="worldLocations">Array of 3D world positions</param>
        /// <param name="cameraLocation">Camera position in world space</param>
        /// <param name="cameraRotation">Camera rotation (pitch, yaw, roll)</param>
        /// <param name="fieldOfView">Camera field of view in degrees</param>
        /// <param name="screenCenterX">Screen center X coordinate</param>
        /// <param name="screenCenterY">Screen center Y coordinate</param>
        /// <returns>Array of 2D screen coordinates</returns>
        public static Vector2[] ConvertBatch(Vector3[] worldLocations, Vector3 cameraLocation, Vector3 cameraRotation,
            float fieldOfView, int screenCenterX, int screenCenterY)
        {
            if (worldLocations == null || worldLocations.Length == 0)
                return Array.Empty<Vector2>();

            var results = new Vector2[worldLocations.Length];

            // Calculate axes once for all points
            if (_lastRotation != cameraRotation)
            {
                cameraRotation.GetAxes(out _vAxisX, out _vAxisY, out _vAxisZ);
                _lastRotation = cameraRotation;
            }

            var projectionFactor = screenCenterX / (float)Math.Tan(fieldOfView * (float)Math.PI / 360);

            for (int i = 0; i < worldLocations.Length; i++)
            {
                var vDelta = worldLocations[i] - cameraLocation;
                var vTransformed = new Vector3(vDelta.Mult(_vAxisY), vDelta.Mult(_vAxisZ), vDelta.Mult(_vAxisX));
                
                if (vTransformed.Z < 1f) 
                    vTransformed.Z = 1f;

                results[i] = new Vector2(
                    screenCenterX + vTransformed.X * projectionFactor / vTransformed.Z,
                    screenCenterY - vTransformed.Y * projectionFactor / vTransformed.Z
                );
            }

            return results;
        }

        /// <summary>
        /// Checks if a world position would be visible on screen
        /// </summary>
        /// <param name="worldLocation">3D position in world space</param>
        /// <param name="cameraLocation">Camera position in world space</param>
        /// <param name="cameraRotation">Camera rotation (pitch, yaw, roll)</param>
        /// <param name="screenWidth">Screen width in pixels</param>
        /// <param name="screenHeight">Screen height in pixels</param>
        /// <returns>True if the position would be visible on screen</returns>
        public static bool IsVisible(Vector3 worldLocation, Vector3 cameraLocation, Vector3 cameraRotation,
            int screenWidth, int screenHeight)
        {
            if (_lastRotation != cameraRotation)
            {
                cameraRotation.GetAxes(out _vAxisX, out _vAxisY, out _vAxisZ);
                _lastRotation = cameraRotation;
            }

            var vDelta = worldLocation - cameraLocation;
            var vTransformed = new Vector3(vDelta.Mult(_vAxisY), vDelta.Mult(_vAxisZ), vDelta.Mult(_vAxisX));
            
            // Behind camera
            if (vTransformed.Z <= 0)
                return false;

            // Check if within screen bounds (with some margin)
            var screenPos = Convert(worldLocation, cameraLocation, cameraRotation, 90.0f, screenWidth / 2, screenHeight / 2);
            
            return screenPos.X >= -100 && screenPos.X <= screenWidth + 100 &&
                   screenPos.Y >= -100 && screenPos.Y <= screenHeight + 100;
        }

        /// <summary>
        /// Gets the distance from camera to world position
        /// </summary>
        /// <param name="worldLocation">3D position in world space</param>
        /// <param name="cameraLocation">Camera position in world space</param>
        /// <returns>Distance in world units</returns>
        public static float GetDistance(Vector3 worldLocation, Vector3 cameraLocation)
        {
            return Vector3.Distance(worldLocation, cameraLocation);
        }

        /// <summary>
        /// Clears the rotation cache (call when camera system changes)
        /// </summary>
        public static void ClearCache()
        {
            _lastRotation = Vector3.Zero;
            _vAxisX = Vector3.Zero;
            _vAxisY = Vector3.Zero;
            _vAxisZ = Vector3.Zero;
        }
    }
}