// Enhancing Tools class to support Enums, Async, Recursive, Overloads, Metadata, etc.
using Agenty.LLMCore.JsonSchema;
using Microsoft.Win32;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Agenty.LLMCore
{
    public interface IToolRegistry
    {
        IReadOnlyList<Tool> RegisteredTools { get; }
        void Register(params Delegate[] funcs);
        void RegisterAll<T>();
        Tool? Get(Delegate func);
        Tool? Get(string toolName);
        IEnumerable<Tool> GetTools<TTuple>();
        bool Contains(string toolName);
    }
    public class ToolRegistry(IEnumerable<Tool>? tools = null) : IToolRegistry
    {
        private List<Tool> _registeredTools = tools?.ToList() ?? new();
        public IReadOnlyList<Tool> RegisteredTools => _registeredTools;

        public static implicit operator ToolRegistry(List<Tool> tools) => new ToolRegistry(tools);
        public void Register(params Delegate[] funcs)
        {
            foreach (var f in funcs)
            {
                var tool = CreateToolFromDelegate(f);
                _registeredTools.Add(tool);
            }
        }
        public void RegisterAll<T>()
        {
            var methods = typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<DescriptionAttribute>() != null);

            foreach (var method in methods)
            {
                try
                {
                    var del = Delegate.CreateDelegate(
                        Expression.GetDelegateType(
                            method.GetParameters().Select(p => p.ParameterType)
                            .Concat(new[] { method.ReturnType })
                            .ToArray()), method);

                    Register(del);
                }
                catch
                {
                    // Skip overloads or mismatches
                }
            }
        }
        public Tool? Get(Delegate func)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            var method = func.Method;
            return _registeredTools.FirstOrDefault(t => t.Function?.Method == method);
        }
        public IEnumerable<Tool> GetTools<TTuple>()
        {
            var tupleType = typeof(TTuple);//If TTuple = (WeatherTool, ConversionTools), then tupleType = typeof(ValueTuple<WeatherTool, ConversionTools>)

            //This is the branch for "single type" (non-tuple case).
            if (!tupleType.IsGenericType || tupleType.GetGenericTypeDefinition() != typeof(ValueTuple<>)
                && !tupleType.FullName!.StartsWith("System.ValueTuple"))
            {
                return GetTools(tupleType);
            }

            //Example: ValueTuple<WeatherTool, ConversionTools> → [WeatherTool, ConversionTools]
            var types = tupleType.GetGenericArguments();
            return GetTools(types);
        }

        private IEnumerable<Tool> GetTools(params Type[] types)
        {
            return _registeredTools.Where(t =>
                t.Function?.Method.DeclaringType != null &&
                types.Contains(t.Function.Method.DeclaringType));
        }


        public bool Contains(string toolName) => _registeredTools.Any(t =>
                t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
        public Tool? Get(string toolName)
            => _registeredTools.FirstOrDefault(t => t.Name.Equals(toolName, StringComparison.InvariantCultureIgnoreCase));
        private Tool CreateToolFromDelegate(Delegate func)
        {
            var method = func.Method;
            var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? method.Name;
            var parameters = method.GetParameters();

            var properties = new JsonObject();
            var required = new JsonArray();

            foreach (var param in parameters)
            {
                var name = param.Name!;
                var desc = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? name;
                var typeSchema = param.ParameterType.GetSchemaForType();
                typeSchema[JsonSchemaConstants.DescriptionKey] ??= desc;

                properties[name] = typeSchema;
                if (!param.IsOptional) required.Add(name);
            }

            var schema = new JsonSchemaBuilder()
                .Type<object>()
                .Properties(properties)
                .Required(required)
                .Build();

            return new Tool
            {
                Name = method.Name,
                Description = description,
                SchemaDefinition = schema,
                Function = func
            };
        }

        public override string ToString() =>
            string.Join(", ", RegisteredTools.Select(t => t.ToString()));
    }
}