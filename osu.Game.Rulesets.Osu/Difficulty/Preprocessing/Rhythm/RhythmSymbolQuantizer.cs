// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing.Rhythm
{
    public static class RhythmSymbolQuantizer
    {
        // 15 ratio bins evenly spaced in log-space from ln(1/16) to ln(16/1).
        // Covers all beat snap divisor ratios {1..9, 12, 16}.
        public const int RATIO_BIN_COUNT = 15;
        private static readonly double log_ratio_min = Math.Log(1.0 / 16.0);
        private static readonly double log_ratio_max = Math.Log(16.0);
        private static readonly double bin_width = (log_ratio_max - log_ratio_min) / RATIO_BIN_COUNT;

        // Center bin index (ratio ≈ 1.0)
        private const int center_bin = RATIO_BIN_COUNT / 2;

        public static int QuantizeRatio(double currDelta, double prevDelta, double epsilon)
        {
            // Deltas within the OD hit window are indistinguishable — snap to center
            if (Math.Abs(currDelta - prevDelta) < epsilon)
                return center_bin;

            double ratio = currDelta / prevDelta;
            double logRatio = Math.Log(ratio);

            int bin = (int)Math.Floor((logRatio - log_ratio_min) / bin_width);
            return Math.Clamp(bin, 0, RATIO_BIN_COUNT - 1);
        }
    }
}
