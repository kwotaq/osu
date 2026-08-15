// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    public static class ReadingEvaluator
    {
        private const double reading_window_size = 3000; // 3 seconds
        private const double distance_influence_threshold = OsuDifficultyHitObject.NORMALISED_DIAMETER * 1.5; // 1.5 circles distance between centers

        public static double EvaluateDifficultyOf(DifficultyHitObject current, bool hidden)
        {
            if (current.BaseObject is Spinner || current.Index == 0)
                return 0;

            var currObj = (OsuDifficultyHitObject)current;
            var nextObj = (OsuDifficultyHitObject)current.Next(0);

            double velocity = Math.Max(1, currObj.LazyJumpDistance / currObj.AdjustedDeltaTime); // Only allow velocity to buff

            double currentVisibleObjectDensity = retrieveCurrentVisibleObjectDensity(currObj);
            double pastObjectDifficultyInfluence = getPastObjectDifficultyInfluence(currObj);

            double constantAngleNerfFactor = getConstantAngleNerfFactor(currObj);

            double overlapDifficulty = calculateOverlapDifficulty(currObj, hidden);

            double noteDensityDifficulty = calculateDensityDifficulty(nextObj, velocity, constantAngleNerfFactor, pastObjectDifficultyInfluence, currentVisibleObjectDensity);
            noteDensityDifficulty *= highBpmBonus(currObj.AdjustedDeltaTime);

            double hiddenDifficulty = hidden
                ? calculateHiddenDifficulty(currObj, pastObjectDifficultyInfluence, currentVisibleObjectDensity, velocity, constantAngleNerfFactor)
                : 0;
            hiddenDifficulty *= highBpmBonus(currObj.AdjustedDeltaTime);

            double preemptDifficulty = calculatePreemptDifficulty(velocity, constantAngleNerfFactor, currObj.Preempt);
            preemptDifficulty *= highBpmBonus(currObj.AdjustedDeltaTime);

            double readingDifficulty = DiffUtils.Norm(1.5, preemptDifficulty, hiddenDifficulty, noteDensityDifficulty, overlapDifficulty);

            return readingDifficulty;
        }

        /// <summary>
        /// Calculates the density difficulty of the current object and how hard it is to aim it because of it based on:
        /// <list type="bullet">
        /// <item><description>cursor velocity to the current object,</description></item>
        /// <item><description>how many times the current object's angle was repeated,</description></item>
        /// <item><description>density of objects visible when the current object appears,</description></item>
        /// <item><description>density of objects visible when the current object needs to be clicked,</description></item>
        /// /// </list>
        /// </summary>
        private static double calculateDensityDifficulty(OsuDifficultyHitObject? nextObj, double velocity, double constantAngleNerfFactor,
                                                         double pastObjectDifficultyInfluence, double currentVisibleObjectDensity)
        {
            const double density_multiplier = 2.4;
            const double density_difficulty_base = 2.5;

            // Consider future densities too because it can make the path the cursor takes less clear
            double futureObjectDifficultyInfluence = Math.Sqrt(currentVisibleObjectDensity);

            if (nextObj != null)
            {
                // Reduce difficulty if movement to next object is small
                futureObjectDifficultyInfluence *= DiffUtils.Smootherstep(nextObj.LazyJumpDistance, 15, distance_influence_threshold);
            }

            // Value higher note densities exponentially
            double noteDensityDifficulty = DiffUtils.Pow(pastObjectDifficultyInfluence + futureObjectDifficultyInfluence, 1.7) * 0.4 * constantAngleNerfFactor * velocity;

            // Award only denser than average maps.
            noteDensityDifficulty = Math.Max(0, noteDensityDifficulty - density_difficulty_base);

            // Apply a soft cap to general density reading to account for partial memorization
            noteDensityDifficulty = DiffUtils.Pow(noteDensityDifficulty, 0.45) * density_multiplier;

            return noteDensityDifficulty;
        }

        /// <summary>
        /// Calculates the difficulty of aiming the current object when the approach rate is very high based on:
        /// <list type="bullet">
        /// <item><description>cursor velocity to the current object,</description></item>
        /// <item><description>how many times the current object's angle was repeated,</description></item>
        /// <item><description>how many milliseconds elapse between the approach circle appearing and touching the inner circle</description></item>
        /// </list>
        /// </summary>
        private static double calculatePreemptDifficulty(double velocity, double constantAngleNerfFactor, double preempt)
        {
            const double preempt_balancing_factor = 140000;
            const double preempt_starting_point = 500; // AR 9.66 in milliseconds

            // Arbitrary curve for the base value preempt difficulty should have as approach rate increases.
            // https://www.desmos.com/calculator/c175335a71
            double preemptDifficulty = DiffUtils.Pow((preempt_starting_point - preempt + Math.Abs(preempt - preempt_starting_point)) / 2, 2.5) / preempt_balancing_factor;

            preemptDifficulty *= constantAngleNerfFactor * velocity;

            return preemptDifficulty;
        }

        /// <summary>
        /// Calculates the difficulty of aiming the current object when the hidden mod is active based on:
        /// <list type="bullet">
        /// <item><description>cursor velocity to the current object,</description></item>
        /// <item><description>time the current object spends invisible,</description></item>
        /// <item><description>density of objects visible when the current object appears,</description></item>
        /// <item><description>density of objects visible when the current object needs to be clicked,</description></item>
        /// <item><description>how many times the current object's angle was repeated,</description></item>
        /// <item><description>if the current object is perfectly stacked to the previous one</description></item>
        /// </list>
        /// </summary>
        private static double calculateHiddenDifficulty(OsuDifficultyHitObject currObj, double pastObjectDifficultyInfluence, double currentVisibleObjectDensity, double velocity,
                                                        double constantAngleNerfFactor)
        {
            const double hidden_multiplier = 0.28;

            // Higher preempt means that time spent invisible is higher too, we want to reward that
            double preemptFactor = DiffUtils.Pow(currObj.Preempt, 2.2) * 0.01;

            // Account for both past and current densities
            double densityFactor = DiffUtils.Pow(currentVisibleObjectDensity + pastObjectDifficultyInfluence, 3.3) * 3;

            double hiddenDifficulty = (preemptFactor + densityFactor) * constantAngleNerfFactor * velocity * 0.01;

            // Apply a soft cap to general HD reading to account for partial memorization
            hiddenDifficulty = DiffUtils.Pow(hiddenDifficulty, 0.4) * hidden_multiplier;

            var previousObj = (OsuDifficultyHitObject)currObj.Previous(0);

            // Buff perfect stacks only if current note is completely invisible at the time you click the previous note.
            if (currObj.LazyJumpDistance == 0 && currObj.OpacityAt(previousObj.BaseObject.StartTime, true) == 0 && previousObj.StartTime > currObj.StartTime - currObj.Preempt)
                hiddenDifficulty += hidden_multiplier * 2500 / DiffUtils.Pow(currObj.AdjustedDeltaTime, 1.5); // Perfect stacks are harder the less time between notes

            return hiddenDifficulty;
        }

        private static double calculateOverlapDifficulty(OsuDifficultyHitObject currObj, bool hidden)
        {
            double totalOverlapDifficulty = 0;
            var currBaseObj = (OsuHitObject)currObj.BaseObject;
            Vector2 currPosition = currBaseObj.StackedPosition;

            // Consider the limit at which notes overlapping becomes irrelevant reading-wise at over a radius but less than a diameter
            double overlapLimit = ((OsuHitObject)currObj.BaseObject).Radius * 1.7;

            var currSliderPathPositions = new List<Vector2>();

            if (currBaseObj is Slider slider)
            {
                slider.Path.GetPathToProgress(currSliderPathPositions, 0, 1);
            }

            double nonOverlappedDistanceSum = 0;
            double prevDistanceChange = 0;
            double prevDistanceChangeDelta = 0;

            foreach (var loopObj in retrievePastVisibleObjects(currObj))
            {
                var loopBaseObj = (OsuHitObject)loopObj.BaseObject;
                Vector2 loopPosition = loopBaseObj.StackedPosition;

                var loopSliderPathPositions = new List<Vector2>();

                if (loopBaseObj is Slider loopSlider)
                {
                    loopSlider.Path.GetPathToProgress(loopSliderPathPositions, 0, 1);
                }

                double distanceFromCurrent = (loopPosition - currPosition).Length;

                double loopDifficulty = Math.Max(0, overlapLimit - distanceFromCurrent);

                // circle over slider body
                loopDifficulty += getBodyOverlapness(currPosition, currSliderPathPositions, loopPosition, overlapLimit, loopDifficulty) * 0.2;

                // slider body over circle
                loopDifficulty += getBodyOverlapness(loopPosition, loopSliderPathPositions, currPosition, overlapLimit, loopDifficulty) * 0.15;

                // slider body over slider body
                double bodyDifficulty = 0;

                if (currBaseObj is Slider && loopBaseObj is Slider)
                {
                    foreach (var pos in currSliderPathPositions)
                    {
                        var currSliderBodyPosition = currPosition + pos;
                        double overlapToCurrent = 0;

                        foreach (var loopPos in loopSliderPathPositions)
                        {
                            var loopSliderBodyPosition = loopPosition + loopPos;
                            double bodyDistanceFromCurrent = (loopSliderBodyPosition - currSliderBodyPosition).Length;
                            overlapToCurrent += Math.Max(0, overlapLimit - bodyDistanceFromCurrent) / loopSliderPathPositions.Count;
                        }

                        bodyDifficulty = Math.Sqrt(Math.Max(loopDifficulty, overlapToCurrent));
                    }
                }

                loopDifficulty += bodyDifficulty * 0.7;

                // Buff notes the more they overlap
                loopDifficulty = Math.Pow(loopDifficulty, 2) * 0.001;

                double nonOverlappedDistance = Math.Max(0, distanceFromCurrent - overlapLimit);
                double distanceChangeDelta = Math.Abs(nonOverlappedDistance - prevDistanceChange);

                double repetitionFactor = Math.Max(0.1, DiffUtils.Smootherstep(Math.Abs(distanceChangeDelta - prevDistanceChangeDelta), 0, 100));
                prevDistanceChangeDelta = distanceChangeDelta;

                nonOverlappedDistanceSum += nonOverlappedDistance;

                double distanceChangeFactor = DiffUtils.Smootherstep(nonOverlappedDistanceSum, 0, 50);

                bool notStacked = loopObj.LazyJumpDistance > overlapLimit;
                prevDistanceChange = notStacked ? nonOverlappedDistance : prevDistanceChange;

                if (distanceChangeFactor > 0)
                {
                    loopDifficulty *= repetitionFactor * 5;
                }

                // Account less for objects close to the max reading window
                double timeBetweenCurrAndLoopObj = currObj.StartTime - loopObj.StartTime;
                double timeNerfFactor = getTimeNerfFactor(timeBetweenCurrAndLoopObj);

                loopDifficulty *= timeNerfFactor;

                loopDifficulty *= distanceChangeFactor;

                // Greatly reduce difficulty depending on the visibility of the overlapping object
                loopDifficulty *= DiffUtils.Pow(currObj.OpacityAt(loopObj.BaseObject.StartTime, hidden), 2);

                totalOverlapDifficulty += loopDifficulty;
            }

            double overlapDifficulty = DiffUtils.Pow(Math.Max(0, totalOverlapDifficulty), 0.3) * 90000;

            // The longer a note is overlapped the more time you have time to process it
            overlapDifficulty /= currObj.Preempt;

            return overlapDifficulty;
        }

        private static double getBodyOverlapness(Vector2 headPosition, List<Vector2> path, Vector2 targetPosition, double overlapLimit, double loopDifficulty)
        {
            double bodyDifficulty = 0;

            foreach (var pos in path)
            {
                var bodyPosition = headPosition + pos;
                double bodyDistanceFromCurrent = (targetPosition - bodyPosition).Length;
                double overlapToCurrent = Math.Max(0, overlapLimit - bodyDistanceFromCurrent);

                bodyDifficulty = Math.Max(loopDifficulty, overlapToCurrent);

                if (bodyDistanceFromCurrent == 0)
                    break;
            }

            return bodyDifficulty;
        }

        private static double getPastObjectDifficultyInfluence(OsuDifficultyHitObject currObj)
        {
            double pastObjectDifficultyInfluence = 0;

            foreach (var loopObj in retrievePastVisibleObjects(currObj))
            {
                double loopDifficulty = currObj.OpacityAt(loopObj.BaseObject.StartTime, false);

                // When aiming an object small distances mean previous objects may be cheesed, so it doesn't matter whether they were arranged confusingly.
                loopDifficulty *= DiffUtils.Smootherstep(loopObj.LazyJumpDistance, 15, distance_influence_threshold);

                // Account less for objects close to the max reading window
                double timeBetweenCurrAndLoopObj = currObj.StartTime - loopObj.StartTime;
                double timeNerfFactor = getTimeNerfFactor(timeBetweenCurrAndLoopObj);

                loopDifficulty *= timeNerfFactor;
                pastObjectDifficultyInfluence += loopDifficulty;
            }

            return pastObjectDifficultyInfluence;
        }

        // Returns a list of objects that are visible on screen at the point in time the current object becomes visible.
        private static IEnumerable<OsuDifficultyHitObject> retrievePastVisibleObjects(OsuDifficultyHitObject current)
        {
            for (int i = 0; i < current.Index; i++)
            {
                OsuDifficultyHitObject hitObject = (OsuDifficultyHitObject)current.Previous(i);

                if (hitObject == null ||
                    current.StartTime - hitObject.StartTime > reading_window_size ||
                    hitObject.StartTime < current.StartTime - current.Preempt) // Current object not visible at the time object needs to be clicked
                    break;

                yield return hitObject;
            }
        }

        // Returns the density of objects visible at the point in time the current object needs to be clicked capped by the reading window.
        private static double retrieveCurrentVisibleObjectDensity(OsuDifficultyHitObject current)
        {
            double visibleObjectCount = 0;

            OsuDifficultyHitObject? hitObject = (OsuDifficultyHitObject)current.Next(0);

            while (hitObject != null)
            {
                if (hitObject.StartTime - current.StartTime > reading_window_size ||
                    current.StartTime < hitObject.StartTime - hitObject.Preempt) // Object not visible at the time current object needs to be clicked.
                    break;

                double timeBetweenCurrAndLoopObj = hitObject.StartTime - current.StartTime;
                double timeNerfFactor = getTimeNerfFactor(timeBetweenCurrAndLoopObj);

                visibleObjectCount += hitObject.OpacityAt(current.BaseObject.StartTime, false) * timeNerfFactor;

                hitObject = (OsuDifficultyHitObject?)hitObject.Next(0);
            }

            return visibleObjectCount;
        }

        // Returns a factor of how often the current object's angle has been repeated in a certain time frame.
        // It does this by checking the difference in angle between current and past objects and sums them based on a range of similarity.
        // https://www.desmos.com/calculator/eb057a4822
        private static double getConstantAngleNerfFactor(OsuDifficultyHitObject current)
        {
            const double minimum_angle_relevancy_time = 2000; // 2 seconds
            const double maximum_angle_relevancy_time = 200;

            double constantAngleCount = 0;
            int index = 0;
            double currentTimeGap = 0;

            OsuDifficultyHitObject loopObjPrev0 = current;
            OsuDifficultyHitObject? loopObjPrev1 = null;
            OsuDifficultyHitObject? loopObjPrev2 = null;

            while (currentTimeGap < minimum_angle_relevancy_time)
            {
                var loopObj = (OsuDifficultyHitObject)current.Previous(index);

                if (loopObj == null)
                    break;

                // Account less for objects that are close to the time limit.
                double longIntervalFactor = 1 - DiffUtils.ReverseLerp(loopObj.AdjustedDeltaTime, maximum_angle_relevancy_time, minimum_angle_relevancy_time);

                if (loopObj.Angle != null && current.Angle != null)
                {
                    double angleDifference = Math.Abs(current.Angle.Value - loopObj.Angle.Value);
                    double angleDifferenceAlternating = Math.PI;

                    if (loopObjPrev0.Angle != null && loopObjPrev1?.Angle != null && loopObjPrev2?.Angle != null)
                    {
                        angleDifferenceAlternating = Math.Abs(loopObjPrev1.Angle.Value - loopObj.Angle.Value);
                        angleDifferenceAlternating += Math.Abs(loopObjPrev2.Angle.Value - loopObjPrev0.Angle.Value);

                        double weight = 1.0;

                        // Be sure that one of the angles is very sharp, when other is wide
                        weight *= DiffUtils.ReverseLerp(Math.Min(loopObj.Angle.Value, loopObjPrev0.Angle.Value) * 180 / Math.PI, 20, 5);
                        weight *= DiffUtils.ReverseLerp(Math.Max(loopObj.Angle.Value, loopObjPrev0.Angle.Value) * 180 / Math.PI, 60, 120);

                        // Lerp between max angle difference and rescaled alternating difference, with more harsh scaling compared to normal difference
                        angleDifferenceAlternating = double.Lerp(Math.PI, 0.1 * angleDifferenceAlternating, weight);
                    }

                    double stackFactor = DiffUtils.Smootherstep(loopObj.LazyJumpDistance, 0, OsuDifficultyHitObject.NORMALISED_RADIUS);

                    constantAngleCount += Math.Cos(3 * Math.Min(double.DegreesToRadians(30), Math.Min(angleDifference, angleDifferenceAlternating) * stackFactor)) * longIntervalFactor;
                }

                currentTimeGap = current.StartTime - loopObj.StartTime;
                index++;

                loopObjPrev2 = loopObjPrev1;
                loopObjPrev1 = loopObjPrev0;
                loopObjPrev0 = loopObj;
            }

            return Math.Clamp(2 / constantAngleCount, 0.2, 1);
        }

        // Returns a nerfing factor for when objects are very distant in time, affecting reading less.
        private static double getTimeNerfFactor(double deltaTime)
        {
            return Math.Clamp(2 - deltaTime / (reading_window_size / 2), 0, 1);
        }

        private static double highBpmBonus(double ms) => 1 / (1 - DiffUtils.Pow(0.8, ms / 1000));
    }
}
