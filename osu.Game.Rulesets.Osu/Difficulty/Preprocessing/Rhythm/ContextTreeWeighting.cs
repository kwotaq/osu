// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing.Rhythm
{
    public class CtwNode
    {
        private readonly int alphabetSize;
        private readonly int[] counts;
        private CtwNode?[]? children;

        private int totalCount;

        public CtwNode(int alphabetSize)
        {
            this.alphabetSize = alphabetSize;
            counts = new int[alphabetSize];
        }

        /// <summary>
        /// Returns the KT-estimated log-probability of the symbol before updating counts.
        /// KT estimator: P(s) = (n_s + 0.5) / (n + K/2)
        /// </summary>
        public void UpdateKt(int symbol)
        {
            double prob = (counts[symbol] + 0.5) / (totalCount + alphabetSize / 2.0);
            double logProb = Math.Log(prob);

            LogProbKt += logProb;
            counts[symbol]++;
            totalCount++;
        }

        /// <summary>
        /// Returns what UpdateKt's log-probability would be for the symbol, without mutating counts.
        /// </summary>
        public double PeekKt(int symbol)
        {
            double prob = (counts[symbol] + 0.5) / (totalCount + alphabetSize / 2.0);
            return Math.Log(prob);
        }

        public double LogProbKt { get; private set; }

        public CtwNode GetOrCreateChild(int symbol)
        {
            children ??= new CtwNode[alphabetSize];
            return children[symbol] ??= new CtwNode(alphabetSize);
        }

        /// <summary>
        /// Returns the existing child for the symbol, or null if it hasn't been created yet.
        /// Never allocates.
        /// </summary>
        public CtwNode? TryGetChild(int symbol) => children?[symbol];

        /// <summary>
        /// Sums LogProbWeighted over all existing children except excludeSymbol. Used by Peek to
        /// combine the hypothetical on-path child with the unchanged off-path siblings.
        /// </summary>
        public double SumChildrenWeighted(int excludeSymbol)
        {
            if (children == null)
                return 0;

            double sum = 0;

            for (int i = 0; i < alphabetSize; i++)
            {
                if (i == excludeSymbol)
                    continue;

                if (children[i] != null)
                    sum += children[i]!.LogProbWeighted;
            }

            return sum;
        }

        // Recomputes weighted probability mixing KT estimate with children's predictions.
        // At leaf depth the KT estimate is used directly; at internal nodes we average
        // the KT estimate with the product of children's weighted probabilities.
        public void RecomputeWeighted(bool isLeaf)
        {
            if (isLeaf)
            {
                LogProbWeighted = LogProbKt;
                return;
            }

            double logProbChildren = 0;

            if (children != null)
            {
                foreach (var child in children)
                {
                    if (child != null)
                        logProbChildren += child.LogProbWeighted;
                }
            }

            LogProbWeighted = Math.Log(0.5) + LogSumExp(LogProbKt, logProbChildren);
        }

        public double LogProbWeighted { get; private set; }

        internal static double LogSumExp(double a, double b)
        {
            double max = Math.Max(a, b);

            if (double.IsNegativeInfinity(max))
                return double.NegativeInfinity;

            return max + Math.Log(Math.Exp(a - max) + Math.Exp(b - max));
        }
    }

    public class ContextTreeWeighting
    {
        private readonly int maxDepth;
        private readonly int alphabetSize;
        private readonly CtwNode root;
        private readonly int[] contextBuffer;
        private int bufferCount;

        // Preallocated scratch buffers reused across Update/Peek calls to avoid a per-call allocation.
        private readonly CtwNode[] updatePathBuffer;
        private readonly CtwNode?[] peekPathBuffer;

        public ContextTreeWeighting(int maxDepth, int alphabetSize)
        {
            this.maxDepth = maxDepth;
            this.alphabetSize = alphabetSize;
            root = new CtwNode(alphabetSize);
            contextBuffer = new int[maxDepth];
            updatePathBuffer = new CtwNode[maxDepth + 1];
            peekPathBuffer = new CtwNode?[maxDepth + 1];
        }

        // Returns surprise (-log P_ctw) for the symbol before updating the model.
        // Walks the context tree from root to leaf using the context buffer,
        // updates KT estimates bottom-up, then recomputes weighted probabilities.
        public double Update(int symbol)
        {
            double previousLogProb = root.LogProbWeighted;

            int depth = Math.Min(bufferCount, maxDepth);

            updatePathBuffer[0] = root;

            for (int d = 0; d < depth; d++)
            {
                int contextSymbol = contextBuffer[(bufferCount - 1 - d) % maxDepth];
                updatePathBuffer[d + 1] = updatePathBuffer[d].GetOrCreateChild(contextSymbol);
            }

            // Update KT estimates at every node along the path
            for (int d = depth; d >= 0; d--)
                updatePathBuffer[d].UpdateKt(symbol);

            // Recompute weighted probabilities bottom-up
            for (int d = depth; d >= 0; d--)
                updatePathBuffer[d].RecomputeWeighted(d == depth);

            // Store symbol in circular context buffer
            contextBuffer[bufferCount % maxDepth] = symbol;
            bufferCount++;

            // Surprise = negative log-probability of this symbol under the CTW model
            return -(root.LogProbWeighted - previousLogProb);
        }

        /// <summary>
        /// Returns the surprise (-log P_ctw) the symbol would have if applied now, without
        /// mutating any tree state. Only walks nodes that already exist along the context path
        /// (bounded by maxDepth); missing nodes are treated as fresh/uncreated.
        /// </summary>
        public double Peek(int symbol)
        {
            double previousLogProb = root.LogProbWeighted;

            int depth = Math.Min(bufferCount, maxDepth);

            peekPathBuffer[0] = root;

            for (int d = 0; d < depth; d++)
            {
                int contextSymbol = contextBuffer[(bufferCount - 1 - d) % maxDepth];
                var parent = peekPathBuffer[d];
                peekPathBuffer[d + 1] = parent?.TryGetChild(contextSymbol);
            }

            double weighted = 0;

            for (int d = depth; d >= 0; d--)
            {
                var node = peekPathBuffer[d];

                double newLogProbKt = node != null
                    ? node.LogProbKt + node.PeekKt(symbol)
                    : Math.Log(0.5 / (alphabetSize / 2.0));

                if (d == depth)
                {
                    weighted = newLogProbKt;
                }
                else
                {
                    int childSymbol = contextBuffer[(bufferCount - 1 - d) % maxDepth];
                    double siblingSum = node?.SumChildrenWeighted(childSymbol) ?? 0;
                    weighted = Math.Log(0.5) + CtwNode.LogSumExp(newLogProbKt, weighted + siblingSum);
                }
            }

            return -(weighted - previousLogProb);
        }
    }
}
