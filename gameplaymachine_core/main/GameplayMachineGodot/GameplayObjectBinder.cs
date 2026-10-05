#nullable enable
using GMCore;
using Godot;
using XLifetimeObject;

namespace ODStudio.Godot.Runtime;

[GlobalClass]
public partial class GameplayObjectBinder : Node
{
    [Export] public Node? Runner { get; set; }
    [Export] public Node? ViewParent { get; set; }
    [Export] public global::Godot.Collections.Array<GameplayObjectBindingRule> Rules { get; set; } = new();
    private readonly Dictionary<GObjectID, List<Node>> views = new();
    private readonly List<IReturnableHandle> handles = new();
    private IGameplayMachineRunner? runtimeRunner;
    public int BoundObjectCount => views.Count;
    public GameplayMachine? Machine { get; private set; }

    public override void _Ready()
    {
        if (Runner == null) throw new InvalidOperationException("Bind an explicit Runner on GameplayObjectBinder");
        runtimeRunner = Runner as IGameplayMachineRunner ??
            throw new InvalidOperationException($"Runner '{Runner.Name}' does not implement {nameof(IGameplayMachineRunner)}");
        runtimeRunner.Started += Attach;
        runtimeRunner.Stopping += Detach;
        Attach();
    }

    private void Attach()
    {
        if (Machine != null || runtimeRunner?.Machine == null) return;
        Machine = runtimeRunner.Machine;
        handles.Add(Machine.BindMachineEvent<AfterGameplayObjectCreatedParam>(change => BindObject(change.ObjectID)));
        handles.Add(Machine.BindMachineEvent<AfterGameplayObjectDeletedParam>(change => ReleaseObject(change.ObjectID)));
        handles.Add(Machine.BindMachineEvent<GameplayObjectAvailabilityChangedParam>(change =>
        {
            if (change.IsAvailable) BindObject(change.ObjectID); else ReleaseObject(change.ObjectID);
        }));
        foreach (var obj in Machine.GetAllGameplayObjects().ToArray()) BindObject(obj.ObjectID);
    }

    private void BindObject(GObjectID id)
    {
        if (Machine == null || views.ContainsKey(id) || !Machine.ContainsGameplayObject(id)) return;
        var target = new CommonGameplayObject { Machine = Machine, ObjectID = id };
        var matches = Rules.Where(rule => rule.Matches(target)).ToArray();
        if (matches.Length == 0) return;
        var instances = new List<Node>();
        views.Add(id, instances);
        try
        {
            foreach (var rule in matches)
            {
                var instance = rule.ViewFactory?.Invoke() ?? rule.ViewScene?.Instantiate() ??
                    throw new InvalidOperationException($"No view configured for {target.ObjectClass}");
                instances.Add(instance);
                (ViewParent ?? this).AddChild(instance);
                foreach (var displayer in Displayers(instance)) displayer.BindTo(target);
                instance.TreeExiting += () =>
                {
                    foreach (var displayer in Displayers(instance)) displayer.Return();
                    instances.Remove(instance);
                    if (instances.Count == 0) views.Remove(id);
                };
            }
        }
        catch { ReleaseObject(id); throw; }
    }

    public IReadOnlyList<Node> GetViews(GObjectID id) => views.TryGetValue(id, out var instances) ? instances.ToArray() : Array.Empty<Node>();

    private static IEnumerable<GameplayObjectDisplayer> Displayers(Node node)
    {
        if (node is GameplayObjectDisplayer displayer) yield return displayer;
        foreach (Node child in node.GetChildren())
            foreach (var nested in Displayers(child)) yield return nested;
    }

    private void ReleaseObject(GObjectID id)
    {
        if (!views.Remove(id, out var instances)) return;
        foreach (var instance in instances.ToArray())
        {
            if (!GodotObject.IsInstanceValid(instance)) continue;
            foreach (var displayer in Displayers(instance)) displayer.Return();
            instance.GetParent()?.RemoveChild(instance);
            instance.QueueFree();
        }
    }

    private void Detach()
    {
        foreach (var handle in handles) handle.Return();
        handles.Clear();
        foreach (var id in views.Keys.ToArray()) ReleaseObject(id);
        Machine = null;
    }

    public override void _ExitTree()
    {
        if (runtimeRunner != null)
        {
            runtimeRunner.Started -= Attach;
            runtimeRunner.Stopping -= Detach;
        }
        runtimeRunner = null;
        Detach();
    }
}
