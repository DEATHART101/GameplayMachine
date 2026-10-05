#nullable enable
using GMCore;
using Godot;

namespace ODStudio.Godot.Runtime;

[Tool, GlobalClass]
public partial class GameplayObjectBindingRule : Resource
{
    [Export] public string GameplayClass { get; set; } = "";
    [Export] public PackedScene? ViewScene { get; set; }
    [Export] public bool IncludeDerivedClasses { get; set; } = true;
    public Func<Node>? ViewFactory { get; set; }
    public ODClassName? ClassName { get; set; }

    public override void _ValidateProperty(global::Godot.Collections.Dictionary property)
    {
        if (property["name"].AsString() != nameof(GameplayClass)) return;
        var names = AppDomain.CurrentDomain.GetAssemblies().SelectMany(LoadTypes)
            .Where(type => type.IsValueType && !type.ContainsGenericParameters && typeof(IODClass).IsAssignableFrom(type) && typeof(IGameplayObjectOperator).IsAssignableFrom(type))
            .Select(type => type.FullName).Where(name => name != null).Distinct().OrderBy(name => name);
        property["hint"] = (int)PropertyHint.Enum;
        property["hint_string"] = string.Join(",", names);
    }

    private static IEnumerable<Type> LoadTypes(System.Reflection.Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (System.Reflection.ReflectionTypeLoadException error) { return error.Types.OfType<Type>(); }
    }

    internal bool Matches(CommonGameplayObject target)
    {
        var name = ClassName;
        if (!name.HasValue)
        {
            foreach (var type in target.Machine.Modules.SelectMany(module => module.GetAllODClassMetas()))
                if (type.ODType?.FullName == GameplayClass || type.Name.ToString() == GameplayClass)
                { name = type.Name; break; }
        }
        return name.HasValue && (target.ObjectClass.Equals(name.Value) ||
            IncludeDerivedClasses && GameplayMachineBase.CanCastTo(target.ObjectClass, name.Value));
    }
}
