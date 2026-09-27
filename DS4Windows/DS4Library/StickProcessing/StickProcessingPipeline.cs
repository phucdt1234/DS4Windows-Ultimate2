/*
DS4Windows
Copyright (C) 2023  Travis Nickles

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;

namespace DS4Windows.StickProcessing
{
    /// <summary>
    /// Stick input processing pipeline. Processing order:
    ///
    ///   Raw Stick Input (normalized -1..1) -> Radial Deadzone -> Response
    ///   Curve -> RC Low-Pass Filter -> Rotational Jitter / Recoil
    ///   Compensation -> Output Clamp.
    ///
    /// All stages are individually bypassable through the settings object.
    /// Values are normalized: -1..1 on both axes with +1 = right / up, so the
    /// pipeline can be unit tested without hardware and reused across input
    /// device types.
    /// </summary>
    public static class StickProcessingPipeline
    {
        /// <summary>
        /// Processes one stick sample.
        /// </summary>
        /// <param name="x">Horizontal input in -1..1 (+1 = right). Updated in place with the processed output.</param>
        /// <param name="y">Vertical input in -1..1 (+1 = up). Updated in place with the processed output.</param>
        /// <param name="settings">Stage configuration for this stick.</param>
        /// <param name="state">Persistent per-stick state.</param>
        /// <param name="dtSeconds">Time since the previous sample, in seconds.</param>
        /// <param name="triggerValue">Analog trigger value in 0..1 used by recoil compensation.</param>
        public static void Process(ref double x, ref double y,
            StickProcessingSettings settings, StickProcessingState state,
            double dtSeconds, double triggerValue)
        {
            if (settings == null || !settings.Enabled)
            {
                return;
            }

            ApplyRadialDeadzone(ref x, ref y, settings.Deadzone);
            ApplyResponseCurve(ref x, ref y, settings.Curve);
            ApplyLowPassFilter(ref x, ref y, settings.Filter, state);
            ApplyRotationalJitter(ref x, ref y, settings.Jitter, state, dtSeconds);
            ApplyRecoilCompensation(ref x, ref y, settings.Recoil, state, dtSeconds, triggerValue);
            ClampOutput(ref x, ref y);
        }

        /// <summary>Converts a raw axis byte (0..255, 0x80 center) to -1..1 with +1 = right/up.</summary>
        public static double NormalizeAxis(byte raw)
        {
            return (raw - 127.5) / 127.5;
        }

        /// <summary>Converts a processed -1..1 pair back into DS4State axis bytes (0..255, 0x80 center).</summary>
        public static byte DenormalizeAxis(double value)
        {
            double clamped = Math.Max(-1.0, Math.Min(1.0, value));
            return (byte)Math.Round(clamped * 127.5 + 127.5);
        }

        /// <summary>
        /// Radial deadzone: works on the vector magnitude so the direction is
        /// preserved (no diagonal distortion that independent X/Y deadzones
        /// introduce). Supports an inner cut and an outer saturation radius.
        /// </summary>
        public static void ApplyRadialDeadzone(ref double x, ref double y, RadialDeadzoneSettings s)
        {
            if (s == null || !s.Enabled)
            {
                return;
            }

            double magnitude = Math.Sqrt(x * x + y * y);
            if (magnitude <= 0.0)
            {
                return;
            }

            double inner = Clamp01(s.InnerRadius);
            double outer = Clamp01(s.OuterRadius);

            if (outer > 0.0 && outer <= inner)
            {
                // Degenerate range: treat outer as disabled to avoid a divide by zero.
                outer = 0.0;
            }

            if (magnitude <= inner)
            {
                x = 0.0;
                y = 0.0;
                return;
            }

            double scaled;
            if (outer > 0.0 && magnitude >= outer)
            {
                scaled = 1.0; // saturate
            }
            else
            {
                double span = outer > 0.0 ? (outer - inner) : (1.0 - inner);
                scaled = span > 0.0 ? (magnitude - inner) / span : magnitude;
                scaled = Math.Min(scaled, 1.0);
            }

            double factor = scaled / magnitude;
            x *= factor;
            y *= factor;
        }

        /// <summary>
        /// Response curve applied radially: the magnitude is remapped through
        /// the curve while the direction is preserved.
        /// </summary>
        public static void ApplyResponseCurve(ref double x, ref double y, ResponseCurveSettings s)
        {
            if (s == null || !s.Enabled || s.CurveType == ResponseCurveType.Linear)
            {
                return;
            }

            double magnitude = Math.Sqrt(x * x + y * y);
            if (magnitude <= 0.0)
            {
                return;
            }

            double mapped = s.CurveType switch
            {
                ResponseCurveType.Power => EvalPowerCurve(magnitude, s.PowerExponent),
                ResponseCurveType.Bezier => EvalBezierCurve(magnitude, s.BezierP1X, s.BezierP1Y, s.BezierP2X, s.BezierP2Y),
                ResponseCurveType.SCurve => EvalSCurve(magnitude),
                _ => magnitude,
            };

            mapped = Clamp01(mapped);
            double factor = mapped / magnitude;
            x *= factor;
            y *= factor;
        }

        /// <summary>y = x^n. n = 1 is linear, n &gt; 1 reduces sensitivity near the center.</summary>
        public static double EvalPowerCurve(double x, double exponent)
        {
            if (x < 0.0) x = 0.0;
            if (exponent <= 0.0)
            {
                return x >= 0.0 ? 1.0 : 0.0;
            }

            return Math.Pow(x, exponent);
        }

        /// <summary>
        /// Cubic Bezier y(x) with P0=(0,0), P3=(1,1) and configurable P1/P2.
        /// Solved parametrically: find t where x(t) = x, then return y(t).
        /// </summary>
        public static double EvalBezierCurve(double x, double p1x, double p1y, double p2x, double p2y)
        {
            x = Clamp01(x);
            p1x = Clamp01(p1x);
            p2x = Clamp01(p2x);

            // Newton-Raphson first, bisect as a fallback for robustness.
            double t = x;
            for (int i = 0; i < 8; i++)
            {
                double cx = BezierX(t, p1x, p2x) - x;
                double dx = BezierXDerivative(t, p1x, p2x);
                if (Math.Abs(dx) < 1e-9)
                {
                    break;
                }

                t -= cx / dx;
                if (t < 0.0) t = 0.0;
                if (t > 1.0) t = 1.0;
            }

            if (Math.Abs(BezierX(t, p1x, p2x) - x) > 1e-6)
            {
                double lo = 0.0, hi = 1.0;
                for (int i = 0; i < 40; i++)
                {
                    t = 0.5 * (lo + hi);
                    if (BezierX(t, p1x, p2x) < x)
                    {
                        lo = t;
                    }
                    else
                    {
                        hi = t;
                    }
                }

                t = 0.5 * (lo + hi);
            }

            return BezierY(t, p1y, p2y);
        }

        private static double BezierX(double t, double p1x, double p2x)
        {
            double om = 1.0 - t;
            return 3.0 * om * om * t * p1x + 3.0 * om * t * t * p2x + t * t * t;
        }

        private static double BezierY(double t, double p1y, double p2y)
        {
            double om = 1.0 - t;
            return 3.0 * om * om * t * p1y + 3.0 * om * t * t * p2y + t * t * t;
        }

        private static double BezierXDerivative(double t, double p1x, double p2x)
        {
            double om = 1.0 - t;
            return 3.0 * om * om * p1x + 6.0 * om * t * (p2x - p1x) + 3.0 * t * t * (1.0 - p2x);
        }

        /// <summary>
        /// S-curve (smoothstep): slow response near the center and the edges,
        /// with the steepest sensitivity in the mid range.
        /// </summary>
        public static double EvalSCurve(double x)
        {
            x = Clamp01(x);
            return x * x * (3.0 - 2.0 * x);
        }

        /// <summary>
        /// RC low-pass filter: y[n] = y[n-1] + a * (x[n] - y[n-1]). Reduces
        /// high-frequency stick jitter at the cost of a small, configurable
        /// amount of input latency (smaller a = smoother, slower).
        /// </summary>
        public static void ApplyLowPassFilter(ref double x, ref double y, RcFilterSettings s, StickProcessingState state)
        {
            if (s == null || !s.Enabled)
            {
                return;
            }

            double a = Math.Max(1e-4, Math.Min(1.0, s.Alpha));
            if (!state.FilterInitialized)
            {
                state.FilteredX = x;
                state.FilteredY = y;
                state.FilterInitialized = true;
                return;
            }

            state.FilteredX += a * (x - state.FilteredX);
            state.FilteredY += a * (y - state.FilteredY);
            x = state.FilteredX;
            y = state.FilteredY;
        }

        /// <summary>
        /// Rotational jitter: injects a high-frequency micro-circle while the
        /// stick sits inside the activation threshold region near neutral.
        /// Near the rim (full deflection) the offset would be clipped by the
        /// output clamp, so the result is rescaled back onto the unit circle:
        /// the stick tip slides along the rim instead of losing the jitter.
        /// </summary>
        public static void ApplyRotationalJitter(ref double x, ref double y, RotationalJitterSettings s,
            StickProcessingState state, double dtSeconds)
        {
            if (s == null || !s.Enabled)
            {
                return;
            }

            double dt = dtSeconds > 0.0 ? dtSeconds : 0.0;
            double magnitude = Math.Sqrt(x * x + y * y);
            double threshold = Math.Max(0.0, s.ActivationThreshold);

            // Cap the magnitude at 1.0 for the threshold check: corner pushes
            // (diagonal full deflection) reach sqrt(2) raw, and must still
            // count as "at the rim", not beyond the activation region.
            double effectiveMagnitude = Math.Min(1.0, magnitude);

            if (effectiveMagnitude <= threshold)
            {
                double radius = Clamp01(s.Radius);
                double frequency = Math.Max(0.0, s.FrequencyHz);
                double angle = state.JitterPhase + 2.0 * Math.PI * frequency * dt;
                state.JitterPhase = angle % (2.0 * Math.PI);

                x += radius * Math.Cos(angle);
                y += radius * Math.Sin(angle);

                double newMagnitude = Math.Sqrt(x * x + y * y);
                if (newMagnitude > 1.0)
                {
                    double scale = 1.0 / newMagnitude;
                    x *= scale;
                    y *= scale;
                }
            }
        }

        /// <summary>
        /// Recoil compensation: while the trigger exceeds its threshold the
        /// offset ramps toward the configured target at the pull rate; when
        /// released it ramps back to zero at the same rate.
        /// </summary>
        public static void ApplyRecoilCompensation(ref double x, ref double y, RecoilCompensationSettings s,
            StickProcessingState state, double dtSeconds, double triggerValue)
        {
            if (s == null || !s.Enabled)
            {
                return;
            }

            double dt = dtSeconds > 0.0 ? dtSeconds : 0.0;
            bool active = triggerValue >= Math.Max(0.0, s.TriggerThreshold);

            double targetX = Clamp01(Math.Abs(s.CompensationX)) * Math.Sign(s.CompensationX) * Clamp01(s.PullStrength);
            double targetY = Clamp01(Math.Abs(s.CompensationY)) * Math.Sign(s.CompensationY) * Clamp01(s.PullStrength);

            double rate = Math.Max(0.0, s.PullRate);
            state.RecoilOffsetX = MoveToward(state.RecoilOffsetX, active ? targetX : 0.0, rate * dt);
            state.RecoilOffsetY = MoveToward(state.RecoilOffsetY, active ? targetY : 0.0, rate * dt);

            x += state.RecoilOffsetX;
            y += state.RecoilOffsetY;
        }

        public static void ClampOutput(ref double x, ref double y)
        {
            if (x > 1.0) x = 1.0;
            else if (x < -1.0) x = -1.0;
            if (y > 1.0) y = 1.0;
            else if (y < -1.0) y = -1.0;
        }

        private static double MoveToward(double current, double target, double maxDelta)
        {
            if (current < target)
            {
                return Math.Min(current + maxDelta, target);
            }

            return Math.Max(current - maxDelta, target);
        }

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
    }
}
