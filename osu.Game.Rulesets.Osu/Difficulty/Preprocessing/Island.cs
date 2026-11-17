// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing
{
    /// <summary>
    /// An island is a group of consecutive objects with the same delta time.
    /// </summary>
    public class Island
    {
        /// <summary>
        /// Delta time of every object in this island
        /// </summary>
        public int Delta { get; private set; }

        /// <summary>
        /// How long the island is
        /// </summary>
        public int DeltaCount { get; private set; } = 1;

        /// <summary>
        /// How many times island already occured
        /// </summary>
        public int Occurrences { get; set; } = 1;

        public Island? PreviousIsland { get; set; }

        public Island(int delta)
        {
            Delta = Math.Max(delta, OsuDifficultyHitObject.MIN_DELTA_TIME);
        }

        public void AddDelta(int delta)
        {
            if (Delta == int.MaxValue)
                Delta = Math.Max(delta, OsuDifficultyHitObject.MIN_DELTA_TIME);

            DeltaCount++;
        }

        public bool IsSimilarPolarity(Island other, double epsilon)
        {
            // single delta islands shouldn't be compared
            if (DeltaCount <= 1 || other.DeltaCount <= 1)
                return false;

            return Math.Abs(Delta - other.Delta) < epsilon &&
                   DeltaCount % 2 == other.DeltaCount % 2;
        }

        public bool AlmostEquals(Island other, double epsilon)
        {
            return Math.Abs(Delta - other.Delta) < epsilon &&
                   DeltaCount == other.DeltaCount;
        }

        public override string ToString()
        {
            return $"{Delta}x{DeltaCount}";
        }
    }
}
