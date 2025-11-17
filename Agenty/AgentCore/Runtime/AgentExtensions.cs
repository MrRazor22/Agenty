using Agenty.AgentCore.TokenHandling;
using Agenty.LLMCore.Providers.OpenAI;
using Agenty.LLMCore.RuntIme;
using Agenty.LLMCore.ToolHandling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace Agenty.AgentCore.Runtime
{
    public static class AgentExtensions
    {
        //Open AI
        public static AgentBuilder AddOpenAI(this AgentBuilder builder, Action<LLMInitOptions> configure)
        {
            var opts = new LLMInitOptions();
            configure(opts);

            builder.Services.AddSingleton<IToolRuntime>(sp =>
            {
                var registry = sp.GetRequiredService<IToolCatalog>();
                var logger = sp.GetService<ILogger<ToolRuntime>>();
                return new ToolRuntime(registry, logger);
            });

            builder.Services.AddSingleton<ILLMClient>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ILLMClient>>();
                var registry = sp.GetRequiredService<IToolCatalog>();
                var runtime = sp.GetRequiredService<IToolRuntime>();
                var tokenizer = sp.GetRequiredService<ITokenizer>();
                var trimmer = sp.GetRequiredService<IContextTrimmer>();
                var tokenManager = sp.GetRequiredService<ITokenManager>();
                var parser = new ToolCallParser();
                var retry = sp.GetRequiredService<IRetryPolicy>();

                return new OpenAILLMClient(
                    opts,
                    registry,
                    runtime,
                    parser,
                    tokenizer,
                    trimmer,
                    tokenManager,
                    retry,
                    logger);
            });

            return builder;
        }

        //Retry policy 
        public static AgentBuilder AddRetryPolicy(this AgentBuilder builder, Action<RetryPolicyOptions>? configure = null)
        {
            if (configure != null)
                builder.Services.Configure(configure);
            else
                builder.Services.Configure<RetryPolicyOptions>(_ => { }); // defaults

            builder.Services.AddSingleton<IRetryPolicy, DefaultRetryPolicy>();
            return builder;
        }

        //context trimmer
        public static AgentBuilder AddContextTrimming(
           this AgentBuilder builder,
           Action<ContextTrimOptions> configure)
        {
            var options = new ContextTrimOptions();
            configure(options);

            builder.Services.AddSingleton<IContextTrimmer>(sp =>
            {
                var tokenizer = sp.GetRequiredService<ITokenizer>();
                return new SlidingWindowTrimmer(tokenizer, options);
            });

            return builder;
        }
    }
}
