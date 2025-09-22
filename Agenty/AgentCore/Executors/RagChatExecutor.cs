using Agenty.AgentCore.Steps;
using Agenty.AgentCore.Steps.RAG;
using Agenty.RAG;
using Agenty.RAG.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Agenty.AgentCore.Executors
{
    /// <summary>
    /// RAG-enabled chatbot: KB search first, then reflective QA loop.
    /// </summary>
    public sealed class RagChatExecutor : IExecutor
    {
        private readonly IExecutor _pipeline;

        public RagChatExecutor(IRagRetriever retriever, int maxRounds = 5, string finalPrompt = "Summarize clearly with sources if possible.")
        {
            _pipeline = new StepExecutor.Builder()
                        .Add(new KbSearchStep(retriever))   // search KB
                        .Branch<IReadOnlyList<SearchResult>>(
                            results => results!.Any() || results!.Max(r => r.Score) < 0.6,
                            onWeak => onWeak.Add(new WebFallbackStep(retriever))
                        )
                        .Add(new ContextBuildStep())        // add context into chat
                        .Add(new LoopStep(
                            new StepExecutor.Builder()
                                .Add(new ResponseStep())         // model answers
                                .Add(new ReflectiveQAStep())   // summarize, evaluate, finalize/replan
                                .Build()
                        ))
                        .Build();
        }

        public Task<object?> Execute(IAgentContext ctx) => _pipeline.Execute(ctx);
    }
}
