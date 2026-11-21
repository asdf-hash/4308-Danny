using UnityEngine;
using UnityEngine.InputSystem;

namespace SlopeSmart
{
    /// <summary>
    /// Steering in [-1,1] (left..right).
    /// Priority: screen-edge taps / keyboard, then device tilt (gravity sensor, gyro-style).
    /// Tilt works on phones and tablets in any orientation (Input System compensates for screen rotation).
    /// </summary>
    public class SteeringInput
    {
        // Set by the UI (edge zones + brake button).
        public bool LeftHeld, RightHeld, BrakeHeld;
        float leftPulse, rightPulse;

        public bool TiltAvailable { get; private set; }
        public bool TiltEnabled = true;
        public bool UsingTilt { get; private set; }

        const float TiltRange = 0.42f;   // ~25 degrees = full lock
        const float TiltDeadZone = 0.035f;
        const float TapPulse = 0.18f;    // a quick tap still steers for a moment

        Sensor sensor;
        Vector3 smoothed;
        bool haveSample;
        float neutral;
        float sign = 1f; // sensor convention differs between platforms; detected from the up axis
        float value;

        public void Init()
        {
            TiltAvailable = false;
            if (GravitySensor.current != null) sensor = GravitySensor.current;
            else if (Accelerometer.current != null) sensor = Accelerometer.current;
            if (sensor != null)
            {
                InputSystem.EnableDevice(sensor);
                TiltAvailable = true;
            }
        }

        public void Shutdown()
        {
            if (sensor != null && sensor.enabled) InputSystem.DisableDevice(sensor);
        }

        public void EdgeDown(int dir)
        {
            if (dir < 0) { LeftHeld = true; leftPulse = TapPulse; }
            else { RightHeld = true; rightPulse = TapPulse; }
        }

        public void EdgeUp(int dir)
        {
            if (dir < 0) LeftHeld = false; else RightHeld = false;
        }

        public void ResetAll()
        {
            LeftHeld = RightHeld = BrakeHeld = false;
            leftPulse = rightPulse = 0;
            value = 0;
        }

        Vector3 ReadSensor()
        {
            if (sensor == null) return Vector3.zero;
            Vector3 v = sensor is GravitySensor g ? g.gravity.ReadValue() : ((Accelerometer)sensor).acceleration.ReadValue();
            return v;
        }

        float RawTilt()
        {
            Vector3 v = ReadSensor();
            if (v.sqrMagnitude < 0.0001f) return 0f;
            Vector3 n = v.normalized;
            smoothed = haveSample ? Vector3.Lerp(smoothed, n, 0.2f) : n;
            haveSample = true;
            // Detect convention: held upright, the "up/down" axis is clearly non-zero.
            if (Mathf.Abs(smoothed.y) > 0.4f) sign = smoothed.y > 0 ? -1f : 1f;
            return smoothed.x * sign;
        }

        /// <summary>Call when a run starts: the current hold becomes "straight".</summary>
        public void Calibrate()
        {
            haveSample = false;
            if (!TiltAvailable) { neutral = 0; return; }
            for (int i = 0; i < 6; i++) RawTilt();
            neutral = Mathf.Clamp(RawTilt(), -0.3f, 0.3f);
        }

        public bool BrakePressed()
        {
            if (BrakeHeld) return true;
            var kb = Keyboard.current;
            return kb != null && (kb.spaceKey.isPressed || kb.sKey.isPressed || kb.downArrowKey.isPressed);
        }

        public float Tick(float dt)
        {
            leftPulse = Mathf.Max(0, leftPulse - dt);
            rightPulse = Mathf.Max(0, rightPulse - dt);

            float target = 0f;
            bool manual = false;

            if (LeftHeld || leftPulse > 0) { target -= 1f; manual = true; }
            if (RightHeld || rightPulse > 0) { target += 1f; manual = true; }

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) { target -= 1f; manual = true; }
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) { target += 1f; manual = true; }
            }

            UsingTilt = false;
            if (!manual && TiltAvailable && TiltEnabled)
            {
                float t = RawTilt() - neutral;
                float a = Mathf.Abs(t);
                if (a > TiltDeadZone)
                    target = Mathf.Sign(t) * Mathf.Clamp01((a - TiltDeadZone) / (TiltRange - TiltDeadZone));
                UsingTilt = true;
            }

            target = Mathf.Clamp(target, -1f, 1f);
            value = Mathf.MoveTowards(value, target, dt * (manual ? 5f : 7f));
            return value;
        }
    }
}
