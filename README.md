# Agenty - Lightweight C# Framework for LLM-Powered Agents

[![NuGet Version](https://img.shields.io/nuget/v/Agenty.Core)](https://www.nuget.org/packages/Agenty.Core) [![Build Status](https://img.shields.io/badge/build-passing-brightgreen)](https://github.com/your-org/Agenty/actions) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Agenty is a minimal, extensible .NET framework for building single-agent LLM applications. Inspired by the need for simplicity in .NET ecosystems, it focuses on clean tool orchestration, structured reasoning, and low-overhead pipelines—without the bloat of full kernels. Perfect for internal tools, prototypes, or production micro-agents where you want full control without ceremony.

Built for .NET 8+, it integrates seamlessly with OpenAI-compatible LLMs (e.g., Azure OpenAI, LM Studio) and supports easy provider swaps. No multi-agent complexity—just pure, reliable single-agent flows.

## 🚀 Features
- **Tool-Centric Design**: Register tools via attributes or delegates; auto-generate JSON schemas for LLM calls.
- **Reasoning Pipeline**: Composable steps (e.g., plan → tool call → finalize) with middleware for logging/metrics.
- **Streaming & Retries**: Native token streaming, exponential backoff retries, and context trimming to stay under limits.
- **Structured Outputs**: Typed responses with schema validation—parse JSON safely or fallback gracefully.
- **Memory via Chat History**: Episodic memory out-of-the-box; persist to files (no DB deps). Hook external RAG (e.g., RAGSharp) as tools.
- **LLM Agnostic**: Abstracted providers (OpenAI default; swap via 2 methods for Anthropic/Mistral).
- **Sequential Tool Calls**: Hallucination-resistant—avoids unreliable parallels; validates args inline.
- **Enterprise-Ready Plumbing**: Token tracking, error handling, and DI-friendly (ActivatorUtilities).

Under 5k LOC, zero runtime deps beyond Newtonsoft.Json and OpenAI SDK.

## 📦 Installation
Via NuGet:
```bash
dotnet add package Agenty.Core
```
(For providers: `dotnet add package Agenty.Providers.OpenAI`)

## ⚡ Quick Start
Build an agent in ~20 lines:

```csharp
using Agenty;
using Agenty.AgentCore.Steps;
using Agenty.LLMCore.BuiltInTools; // Or your custom tools

var builder = Agent.CreateBuilder()
    .AddOpenAI(opts => {
        opts.BaseUrl = "https://your-openai-endpoint/v1";
        opts.ApiKey = "your-api-key";
        opts.Model = "gpt-4o-mini";
    })
    .WithSystemPrompt("You are a helpful assistant.")
    .WithTools<MathTools>() // Register tools
    .Use<ToolCallingStep>(maxIterations: 5) // Default loop
    .Use<PlanningStep>(); // Optional: Auto-plan steps

var agent = builder.Build();

var response = await agent.ExecuteAsync("What's 15% of 250? Explain step-by-step.");
Console.WriteLine(response.Message); // "37.5 - First, convert 15% to decimal..."
```

Output streams live if wired (e.g., to console or SignalR).

### Console Runner Example
For interactive testing:
```csharp
// See TestApp/ConsoleRunner.cs in repo for full REPL with history persistence.
await ConsoleRunner.RunAsync(); // Loads/saves chat from file
```

## 🛠️ Core Concepts
### 1. **Builder Pattern**
Start with `Agent.CreateBuilder()`:
- `.AddOpenAI()` (or custom provider) for LLM.
- `.WithTools<T>()` registers methods as tools (e.g., `public double Add(double a, double b)` becomes callable).
- `.WithSystemPrompt("...")` sets base instructions.
- `.WithMaxContextWindow(8000)` caps tokens.

### 2. **Pipeline Steps**
Compose via `.Use<TStep>()` (middleware-style):
- `ToolCallingStep`: Core loop—LLM reasons, calls tools sequentially, appends results. Caps iterations to avoid spins.
- `PlanningStep`: Generates structured plan (e.g., `List<string> Steps`), injects into prompt.
- Custom: Implement `IAgentStep` for domain logic (e.g., `ValidateHardwareStep`).

Default: If no steps, falls back to `ToolCallingStep` for instant agents.

### 3. **Tools**
- Define: `[Description("Adds two numbers")]` on methods; auto-schema gen.
- Register: `WithTools<YourClass>()` (instance/static) or `Register(Delegate)`.
- Call: LLM decides; framework invokes, validates params, handles errors.
- Advanced: Tags for filtering (`GetByTags("hardware")`).

### 4. **Conversation & Memory**
- `Conversation`: List<Chat> with roles (System/User/Assistant/Tool).
- Persist: `await agent.LoadHistoryAsync("session-id")` / `SaveHistoryAsync`.
- RAG: Inject via prompt (`convo.AddUser(ragResults)`) or as a tool (e.g., `SearchDocsTool` using RAGSharp).

### 5. **Streaming & Observability**
- `onStream: chunk => Console.Write(chunk.AsText())` for live output.
- Tokens: Auto-tracked via `ITokenManager`; logs deltas per step.
- Retries: Configurable (`MaxRetries=3`, backoff) for flaky LLMs.

## 🔧 Architecture
- **ILLMClient**: Abstracts LLM (streaming/text/tools/structured).
- **IToolRuntime**: Executes calls with param parsing/validation.
- **Pipeline**: `AgentStepDelegate` chains steps; `IAgentContext` carries chat/response.
- Providers: Extend `LLMClientBase` for new LLMs (override 2 methods).
- Why Sequential? LLMs hallucinate on parallels (2025 reality); focus on reliability.

High-level flow:
```
User Goal → Pipeline (Steps) → LLM Loop (Reason + Tools) → Response
```

## 📚 Examples
- **Math Solver**: Tools + PlanningStep for step-by-step.
- **File Analyzer**: Custom tool to read/parse CSVs; stream results.
- **Hardware Tester** (your use case): Tools for "probe device" + "run SQC script" + "assert output".

Full samples in `/samples/` (add to repo).

## 🤝 Contributing
1. Fork & clone.
2. Add features (e.g., new provider) via PR.
3. Run `dotnet test` / `dotnet build`.
4. Docs: Update this README.

Issues? Open one—focus on single-agent wins.

## 📄 License
MIT—use freely, attribute if you like.

## 🙏 Acknowledgments
Built by a solo dev for real-world .NET needs. Inspired by Semantic Kernel's patterns but trimmed for speed.

---

*Star us on GitHub! Questions? Open an issue or ping @your-dev-handle.*

*(Publish tip: Use GitHub's README editor; add badges via shields.io. If NuGet-ready, link it here.)*
