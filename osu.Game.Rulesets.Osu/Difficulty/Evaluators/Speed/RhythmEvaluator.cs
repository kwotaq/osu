// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Runtime.CompilerServices;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Speed
{
    public static class RhythmEvaluator
    {
        /// <summary>
        /// Calculates a rhythm multiplier for the difficulty of the tap associated with historic data of the current <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current)
        {
            if (current.BaseObject is Spinner)
                return 0;

            const int history_time_max = 5 * 1000; // 5 seconds
            const int history_objects_max = 32;
            const double rhythm_overall_multiplier = 0.8;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            double rhythmComplexitySum = 0;

            double deltaDifferenceEpsilon = osuCurrObj.HitWindowGreat * 0.3;

            double startDifficulty = 0; // store the difficulty of the current start of an island to buff for tighter rhythms

            int historicalNoteCount = Math.Min(current.Index, history_objects_max);

            int rhythmStart = 0;

            while (rhythmStart < historicalNoteCount - 2 && current.StartTime - current.Previous(rhythmStart).StartTime < history_time_max)
                rhythmStart++;

            OsuDifficultyHitObject prevObj = (OsuDifficultyHitObject)current.Previous(rhythmStart);
            OsuDifficultyHitObject prevPrevObj = (OsuDifficultyHitObject)current.Previous(rhythmStart + 1);

            // we go from the furthest object back to the current one
            for (int i = rhythmStart; i > 0; i--)
            {
                OsuDifficultyHitObject currObj = (OsuDifficultyHitObject)current.Previous(i - 1);

                if (currObj.BaseObject is Spinner)
                    continue;

                // scales note 0 to 1 from history to now
                double timeDecay = (history_time_max - (current.StartTime - currObj.StartTime)) / history_time_max;
                double noteDecay = (double)(historicalNoteCount - i) / historicalNoteCount;

                double currHistoricalDecay = Math.Min(noteDecay, timeDecay); // either we're limited by time or limited by object count.

                // Use custom cap value to ensure that at this point delta time is actually zero
                const double delta_min_value = 1e-7;

                double currDelta = Math.Max(currObj.DeltaTime, delta_min_value);
                double prevDelta = Math.Max(prevObj.DeltaTime, delta_min_value);

                // calculate how much current delta difference deserves a rhythm bonus
                // this function is meant to reduce rhythm bonus for deltas that are multiples of each other (i.e 100 and 200)
                double effectiveDifficulty = getEffectiveDifficulty(Math.Max(prevDelta, currDelta) / Math.Min(prevDelta, currDelta));

                // if previous object is a slider it might be easier to tap since you don't have to do a whole tapping motion
                // while a full deltatime might end up some weird ratio the "unpress->tap" motion might be simple
                // for example a slider-circle-circle pattern should be evaluated as a regular triple and not as a single->double
                if (prevObj.BaseObject is Slider)
                {
                    double sliderLazyEndDelta = currObj.MinimumJumpTime;
                    double sliderLazyDeltaDifferenceRatio = Math.Max(sliderLazyEndDelta, currDelta) / Math.Min(sliderLazyEndDelta, currDelta);
                    double sliderLazyEffectiveDifficulty = getEffectiveDifficulty(sliderLazyDeltaDifferenceRatio);

                    double sliderRealEndDelta = currObj.LastObjectEndDeltaTime;
                    double sliderRealDeltaDifferenceRatio = Math.Max(sliderRealEndDelta, currDelta) / Math.Min(sliderRealEndDelta, currDelta);
                    double sliderRealEffectiveDifficulty = getEffectiveDifficulty(sliderRealDeltaDifferenceRatio);

                    double sliderEndDelta;
                    double sliderEffectiveDifficulty;

                    if (sliderLazyEffectiveDifficulty < sliderRealEffectiveDifficulty)
                    {
                        sliderEndDelta = sliderLazyEndDelta;
                        sliderEffectiveDifficulty = sliderLazyEffectiveDifficulty;
                    }
                    else
                    {
                        sliderEndDelta = sliderRealEndDelta;
                        sliderEffectiveDifficulty = sliderRealEffectiveDifficulty;
                    }

                    if (sliderEffectiveDifficulty < effectiveDifficulty)
                    {
                        // if slider effective difficulty is easier switch currDelta to slider delta
                        currDelta = sliderEndDelta;
                        effectiveDifficulty = sliderEffectiveDifficulty;
                    }
                }

                // do the same thing to the prev-prevprev object too
                if (prevPrevObj.BaseObject is Slider)
                {
                    double sliderLazyEndDelta = prevObj.MinimumJumpTime;
                    double sliderLazyDeltaDifferenceRatio = Math.Max(sliderLazyEndDelta, prevDelta) / Math.Min(sliderLazyEndDelta, prevDelta);
                    double sliderLazyEffectiveDifficulty = getEffectiveDifficulty(sliderLazyDeltaDifferenceRatio);

                    double sliderRealEndDelta = prevObj.LastObjectEndDeltaTime;
                    double sliderRealDeltaDifferenceRatio = Math.Max(sliderRealEndDelta, prevDelta) / Math.Min(sliderRealEndDelta, prevDelta);
                    double sliderRealEffectiveDifficulty = getEffectiveDifficulty(sliderRealDeltaDifferenceRatio);

                    double sliderEndDelta;
                    double sliderEffectiveDifficulty;

                    if (sliderLazyEffectiveDifficulty < sliderRealEffectiveDifficulty)
                    {
                        sliderEndDelta = sliderLazyEndDelta;
                        sliderEffectiveDifficulty = sliderLazyEffectiveDifficulty;
                    }
                    else
                    {
                        sliderEndDelta = sliderRealEndDelta;
                        sliderEffectiveDifficulty = sliderRealEffectiveDifficulty;
                    }

                    double prevPrevDelta = Math.Max(prevPrevObj.DeltaTime, delta_min_value);
                    double prevEffectiveDifficulty = getEffectiveDifficulty(Math.Max(prevDelta, prevPrevDelta) / Math.Min(prevDelta, prevPrevDelta));

                    if (sliderEffectiveDifficulty < prevEffectiveDifficulty)
                    {
                        // if slider effective difficulty is easier switch prevDelta to slider delta
                        prevDelta = sliderEndDelta;
                    }
                }

                effectiveDifficulty *= windowPenalty * differenceMultiplier;

                var currIsland = currObj.Island;
                var prevIsland = currIsland?.PreviousIsland;

                if (deltaDifference > deltaDifferenceEpsilon)
                {
                    // bpm change is into slider, this is easy acc window
                    if (currObj.BaseObject is Slider)
                        effectiveDifficulty *= 0.5;

                    // previous increase happened a note ago, 1/1->1/2-1/4, dont want to buff this.
                    if (Math.Max(prevPrevObj.DeltaTime, delta_min_value) > prevDelta + deltaDifferenceEpsilon && prevDelta > currDelta + deltaDifferenceEpsilon)
                        effectiveDifficulty *= 0.125;

                    if (currIsland != null && prevIsland != null)
                    {
                        // repeated island polarity (2 -> 4, 3 -> 5)
                        if (currIsland.IsSimilarPolarity(prevIsland, deltaDifferenceEpsilon))
                            effectiveDifficulty *= 0.5;

                        var iterationIsland = prevIsland;
                        int occurrences = 0;

                        for (int j = 0; j < 5; j++)
                        {
                            iterationIsland = iterationIsland.PreviousIsland;

                            if (iterationIsland == null)
                                break;

                            if (iterationIsland.AlmostEquals(currIsland, deltaDifferenceEpsilon))
                            {
                                occurrences++;
                            }
                        }

                        if (occurrences > 0)
                        {
                            // repeated island (ex: triplet -> triplet)
                            double power = DiffUtils.Logistic(currIsland.Delta, maxValue: 2.75, multiplier: 0.24, midpointOffset: 58.33);
                            effectiveDifficulty *= Math.Min(3.0 / occurrences, DiffUtils.Pow(1.0 / occurrences, power));
                        }
                    }

                    // scale down the difficulty if the object is double-tappable
                    effectiveDifficulty *= 1 - prevObj.CalculateDoubleTapFeasibility(currObj) * 0.75;

                    rhythmComplexitySum += Math.Sqrt(effectiveDifficulty * startDifficulty) * currHistoricalDecay;

                    startDifficulty = effectiveDifficulty;
                }

                prevPrevObj = prevObj;
                prevObj = currObj;
            }

            // If the current island is long we don't want the sum to have as big of an effect
            rhythmComplexitySum *= DiffUtils.ReverseLerp(osuCurrObj.Island?.DeltaCount ?? 1, 22, 3);

            return Math.Sqrt(4 + rhythmComplexitySum * rhythm_overall_multiplier) / 2.0; // produces multiplier that can be applied to strain. range [1, infinity) (not really though)
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double getEffectiveDifficulty(double deltaDifferenceRatio)
        {
            const double rhythm_ratio_difficulty_multiplier = 15.0;

            // Take only the fractional part of the value since we're only interested in punishing multiples
            double deltaDifferenceFraction = deltaDifferenceRatio - Math.Truncate(deltaDifferenceRatio);

            return 1.0 + rhythm_ratio_difficulty_multiplier * Math.Min(0.5, DiffUtils.SmoothstepBellCurve(deltaDifferenceFraction));
        }
    }
}
