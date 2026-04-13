// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
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
            if (current.BaseObject is Spinner)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;

            double strain = 1000 / osuCurrObj.AdjustedDeltaTime;

            strain *= Math.Pow(osuCurrObj.SmallCircleBonus, 1.5);

            strain *= highBpmBonus(osuCurrObj.AdjustedDeltaTime);

            return strain;
        }

        private static double highBpmBonus(double ms) => 1 / (1 - Math.Pow(0.2, ms / 1000));
    }
}
