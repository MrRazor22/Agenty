using Agenty.LLMCore.JsonSchema;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Agenty.LLMCore.ToolHandling
{
    public interface IToolRegistry
    {
        IReadOnlyList<Tool> RegisteredTools { get; }
        void Register(params Delegate[] funcs);
        void Register(Delegate func, params string[] tags);
        void RegisterAll<T>(params string[] tags);
        void RegisterAll<T>(T? instance = default, params string[] tags);
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
        public void RegisterAll<T>(T? instance = default, params string[] tags)
        {
            var methods = typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);

            foreach (var method in methods)
            {
                try
                {
                    // If instance is null, only allow static methods
                    if (!method.IsStatic && instance == null)
                        continue;

                    if (method.DeclaringType == typeof(object))
                        continue;

                    var paramTypes = method.GetParameters()
                        .Select(p => p.ParameterType)
                        .Concat(new[] { method.ReturnType })
                        .ToArray();

                    var del = Delegate.CreateDelegate(Expression.GetDelegateType(paramTypes),
                                                      instance, method, throwOnBindFailure: false);

                    if (del != null)
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

        public override string ToString() => RegisteredTools.ToJoinedString();
    }
}