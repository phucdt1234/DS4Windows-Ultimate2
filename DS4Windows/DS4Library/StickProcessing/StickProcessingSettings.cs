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
    /// <summary>Response curve types supported by the stick processing pipeline.</summary>
    public enum ResponseCurveType : ushort
    {
        Linear,
        Power,
        Bezier,
        SCurve,
    }

    /// <summary>Radial deadzone configuration. Radii are normalized (0..1).</summary>
    public class RadialDeadzoneSettings
    {
        public bool Enabled;
        /// <summary>Inner radius. Magnitudes below this snap to zero.</summary>
        public double InnerRadius;
        /// <summary>Outer radius. Magnitudes above this saturate to 1. 0 disables the outer cap.</summary>
        public double OuterRadius;

        public static RadialDeadzoneSettings Disabled() => new RadialDeadzoneSettings();
    }

    /// <summary>Response curve configuration applied radially to the stick magnitude.</summary>
    public class ResponseCurveSettings
    {
        public bool Enabled;
        public ResponseCurveType CurveType = ResponseCurveType.Linear;
        /// <summary>Exponent n for y = x^n (Power curve). 1 = linear.</summary>
        public double PowerExponent = 1.0;
        /// <summary>First control point X (0..1) for the Bezier curve.</summary>
        public double BezierP1X = 0.33;
        /// <summary>First control point Y (0..1) for the Bezier curve.</summary>
        public double BezierP1Y = 0.33;
        /// <summary>Second control point X (0..1) for the Bezier curve.</summary>
        public double BezierP2X = 0.67;
        /// <summary>Second control point Y (0..1) for the Bezier curve.</summary>
        public double BezierP2Y = 0.67;

        public static ResponseCurveSettings Disabled() => new ResponseCurveSettings();
    }

    /// <summary>RC low-pass filter configuration (y += a * (x - y)).</summary>
    public class RcFilterSettings
    {
        public bool Enabled;
        /// <summary>Filter factor a in (0, 1]. 1 = no smoothing (passthrough), smaller = smoother/slower.</summary>
        public double Alpha = 0.5;

        public static RcFilterSettings Disabled() => new RcFilterSettings();
    }

    /// <summary>
    /// Rotational stick jitter configuration: injects a high-frequency
    /// micro-circle around the stick's neutral region.
    /// </summary>
    public class RotationalJitterSettings
    {
        public bool Enabled;
        /// <summary>Radius of the injected micro-circle (normalized 0..1).</summary>
        public double Radius = 0.01;
        /// <summary>Rotation frequency in Hz.</summary>
        public double FrequencyHz = 20.0;
        /// <summary>Jitter is injected only while the stick magnitude is below this threshold.</summary>
        public double ActivationThreshold = 0.05;

        public static RotationalJitterSettings Disabled() => new RotationalJitterSettings();
    }

    /// <summary>
    /// Recoil compensation configuration: applies an X/Y input offset while
    /// the configured trigger exceeds its activation threshold.
    /// </summary>
    public class RecoilCompensationSettings
    {
        public bool Enabled;
        /// <summary>Horizontal compensation target (-1..1, negative pulls left).</summary>
        public double CompensationX;
        /// <summary>Vertical compensation target (-1..1, negative pulls down).</summary>
        public double CompensationY;
        /// <summary>Overall strength multiplier applied to both targets (0..1).</summary>
        public double PullStrength = 1.0;
        /// <summary>Rate the compensation ramps toward its target, in normalized units per second.</summary>
        public double PullRate = 2.0;
        /// <summary>Trigger value (0..1) above which compensation activates.</summary>
        public double TriggerThreshold = 0.3;
        /// <summary>Which analog trigger activates compensation.</summary>
        public TriggerSource Trigger = TriggerSource.RightTrigger;

        public static RecoilCompensationSettings Disabled() => new RecoilCompensationSettings();
    }

    public enum TriggerSource : ushort
    {
        LeftTrigger,
        RightTrigger,
        AnyTrigger,
    }

    /// <summary>
    /// Aggregate configuration for one analog stick. Every stage is
    /// independently configurable and bypassable.
    /// </summary>
    public class StickProcessingSettings
    {
        public bool Enabled;
        public RadialDeadzoneSettings Deadzone = RadialDeadzoneSettings.Disabled();
        public ResponseCurveSettings Curve = ResponseCurveSettings.Disabled();
        public RcFilterSettings Filter = RcFilterSettings.Disabled();
        public RotationalJitterSettings Jitter = RotationalJitterSettings.Disabled();
        public RecoilCompensationSettings Recoil = RecoilCompensationSettings.Disabled();

        public static StickProcessingSettings Disabled() => new StickProcessingSettings { Enabled = false };
    }

    /// <summary>
    /// Per-stick persistent state for the processing pipeline (filter memory,
    /// jitter phase, recoil ramp).
    /// </summary>
    public class StickProcessingState
    {
        public double FilteredX;
        public double FilteredY;
        public bool FilterInitialized;
        public double JitterPhase;
        public double RecoilOffsetX;
        public double RecoilOffsetY;

        public void Reset()
        {
            FilteredX = FilteredY = 0.0;
            FilterInitialized = false;
            JitterPhase = 0.0;
            RecoilOffsetX = RecoilOffsetY = 0.0;
        }
    }
}
