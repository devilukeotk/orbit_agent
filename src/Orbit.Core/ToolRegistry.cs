using Orbit.Contracts;

namespace Orbit.Core;

public interface IToolRegistry
{
    IReadOnlyCollection<ToolDescriptor> GetAll();
    ToolDescriptor GetRequired(string name);
}

public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyDictionary<string, ToolDescriptor> _tools;

    public ToolRegistry(IEnumerable<ToolDescriptor> tools)
    {
        _tools = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<ToolDescriptor> GetAll() => _tools.Values.ToArray();

    public ToolDescriptor GetRequired(string name) => _tools.TryGetValue(name, out var tool)
        ? tool
        : throw new ToolNotFoundException(name);
}

public sealed class ToolNotFoundException(string name)
    : Exception($"Tool '{name}' is not registered.");
