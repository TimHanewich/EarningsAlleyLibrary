using System;
using TimHanewichToolkit.TextAnalysis;
using TheMotleyFool.Transcripts;
using System.Collections.Generic;
using TheMotleyFool.Transcripts.Helper;

namespace EarningsAlley
{
    public static class EarningsAlleyExtensions
    {
        public static TextValuePair[] RankSentences(this Transcript trans)
        {
            List<TextValuePairArg> Args = new List<TextValuePairArg>();
            Args.Add(TextValuePairArg.Create("revenue", 3, true));
            Args.Add(TextValuePairArg.Create("net income", 3, true));
            Args.Add(TextValuePairArg.Create("earnings per share", 4, true));
            Args.Add(TextValuePairArg.Create("record", 2, true));
            Args.Add(TextValuePairArg.Create("growth", 3, true));
            Args.Add(TextValuePairArg.Create("$", 4, true));
            Args.Add(TextValuePairArg.Create("%", 5, true));
            Args.Add(TextValuePairArg.Create("revenue grew", 8, true));
            Args.Add(TextValuePairArg.Create("revenue fell", 8, true));
            Args.Add(TextValuePairArg.Create("income grew", 8, true));
            Args.Add(TextValuePairArg.Create("income fell", 8, true));
            Args.Add(TextValuePairArg.Create("increase in volume", 2, true));
            Args.Add(TextValuePairArg.Create("decrease in volume", 2, true));
            Args.Add(TextValuePairArg.Create("brought down our cost", 4, true));
            Args.Add(TextValuePairArg.Create("revenue of", 3, true));
            Args.Add(TextValuePairArg.Create("income of", 3, true));
            Args.Add(TextValuePairArg.Create("cash flow", 5, true));
            Args.Add(TextValuePairArg.Create("earnings per share", 5, true));
            Args.Add(TextValuePairArg.Create("net profit was", 5, true));
            Args.Add(TextValuePairArg.Create("net loss was", 5, true));
            Args.Add(TextValuePairArg.Create("net profit for the period was", 5, true));
            Args.Add(TextValuePairArg.Create("net loss for the period was", 5, true));
            Args.Add(TextValuePairArg.Create("impact", 2, true));
            Args.Add(TextValuePairArg.Create("cash flow", 3, true));
            Args.Add(TextValuePairArg.Create("we opened", 1, true));
            Args.Add(TextValuePairArg.Create("per share", 3, true));
            Args.Add(TextValuePairArg.Create("we think", 1, false));
            Args.Add(TextValuePairArg.Create("surprised", 2, false));
            Args.Add(TextValuePairArg.Create("surprising", 2, false));
            Args.Add(TextValuePairArg.Create("we generated revenue", 7, false));
            Args.Add(TextValuePairArg.Create("guidance", 6, true));
            Args.Add(TextValuePairArg.Create("surpassed", 3, true));
            Args.Add(TextValuePairArg.Create("top selling", 2, true));
            Args.Add(TextValuePairArg.Create("record performance", 1, true));
            Args.Add(TextValuePairArg.Create("revenue reached", 7, true));
            Args.Add(TextValuePairArg.Create("historic", 2, true));
            Args.Add(TextValuePairArg.Create("extraordinary", 1, true));

            string[] sentences = trans.GetSentences(true);

            TextValuePair[] ranked = TextAnalysisToolkit.RankStrings(sentences, Args.ToArray());

            return ranked;

        }
    }
}