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
        void Register(Delegate func, params string[] tags);
        void RegisterAll<T>(params string[] tags);
        Tool? Get(Delegate func);
        Tool? Get(string toolName);
        IEnumerable<Tool> GetTools(params Type[] toolTypes);
        IEnumerable<Tool> GetByTags(bool include = true, params string[] tags);
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
        public void Register(Delegate func, params string[] tags)
        {
            var tool = CreateToolFromDelegate(func);
            if (tags?.Length > 0)
                tool.Tags.AddRange(tags.Distinct(StringComparer.OrdinalIgnoreCase));

            _registeredTools.Add(tool);
        }
        public void RegisterAll<T>(params string[] tags)
        {
            var methods = typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Static);

            foreach (var method in methods)
            {
                try
                {
                    var del = Delegate.CreateDelegate(
                        Expression.GetDelegateType(
                            method.GetParameters().Select(p => p.ParameterType)
                            .Concat(new[] { method.ReturnType })
                            .ToArray()), method);

                    Register(del, tags);
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
        public IEnumerable<Tool> GetTools(params Type[] types)
        {
            return _registeredTools.Where(t =>
                t.Function?.Method.DeclaringType != null &&
                types.Contains(t.Function.Method.DeclaringType));
        }
        public IEnumerable<Tool> GetByTags(bool include = true, params string[] tags)
        {
            if (tags == null || tags.Length == 0) return _registeredTools;

            return include
                ? _registeredTools.Where(t => t.Tags.Any(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                : _registeredTools.Where(t => !t.Tags.Any(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase)));
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
                ParametersSchema = schema,
                Function = func,
                Tags = new List<string>()
            };
        }

        public override string ToString() =>
            string.Join(", ", RegisteredTools.Select(t => t.ToString()));
    }
}