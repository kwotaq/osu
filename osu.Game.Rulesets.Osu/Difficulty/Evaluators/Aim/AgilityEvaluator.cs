// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim
{
    public static class AgilityEvaluator
    {
        /// <summary>
        /// Evaluates the difficulty of fast aiming
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current)
        {
            const double previous_delta_influence = 1;

            if (current.BaseObject is Spinner)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuPrevObj = (OsuDifficultyHitObject?)current.Previous();

            double baseDifficulty = 1;

            if (osuCurrObj.Angle != null && osuPrevObj?.Angle != null)
            {
                double addition = 0;

                double distanceFactor = DiffUtils.ReverseLerp(osuPrevObj.LazyJumpDistance, OsuDifficultyHitObject.NORMALISED_RADIUS, OsuDifficultyHitObject.NORMALISED_DIAMETER);
                double rhythmFactor = DiffUtils.Pow(Math.Min(osuCurrObj.AdjustedDeltaTime, osuPrevObj.AdjustedDeltaTime) / Math.Max(osuCurrObj.AdjustedDeltaTime, osuPrevObj.AdjustedDeltaTime), 3);

                double angleSwitchingBonus = (1 - Math.Min(AngleUtils.CalculateAcuteness(osuCurrObj.Angle.Value), DiffUtils.Pow(AngleUtils.CalculateAcuteness(osuPrevObj.Angle.Value), 3))) *
                                             distanceFactor * rhythmFactor;

                double wideAngleBonus = AngleUtils.CalculateWideness(osuCurrObj.Angle.Value);

                wideAngleBonus *= (0.25 + 0.75 * Math.Min(wideAngleBonus, DiffUtils.Pow(AngleUtils.CalculateWideness(osuPrevObj.Angle.Value), 3))) * distanceFactor * rhythmFactor;

                addition += DiffUtils.Norm(2, angleSwitchingBonus, wideAngleBonus);

                baseDifficulty += addition;
            }

            // For objects that are stacked we want to reduce the agility difficulty slightly by combining delta times of both objects together
            // Because we can assume that they likely would be done in one movement.
            double previousDelta = 0;

            if (osuPrevObj != null)
            {
                previousDelta = osuPrevObj.AdjustedDeltaTime *
                                DiffUtils.ReverseLerp(osuPrevObj.LazyJumpDistance, OsuDifficultyHitObject.NORMALISED_RADIUS, 0);
            }

            double combinedDelta = osuCurrObj.AdjustedDeltaTime + previousDelta * previous_delta_influence;

            double agilityDifficulty = baseDifficulty * 1_000_000 / DiffUtils.Pow(combinedDelta, 3.0);

            agilityDifficulty *= osuCurrObj.SmallCircleBonus;

            return agilityDifficulty;
        }
    }
}
