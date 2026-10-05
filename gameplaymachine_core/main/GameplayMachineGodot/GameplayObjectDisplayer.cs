#nullable enable
using System.Reflection;
using GMCore;
using Godot;
using XLifetimeObject;

namespace ODStudio.Godot.Runtime;

public partial class GameplayObjectDisplayer : Node, IReturnableHandle
{
    private readonly List<IReturnableHandle> bindings = new();
    public CommonGameplayObject GameplayObject { get; private set; }
    public GameplayMachine Machine => GameplayObject.Machine;
    public bool IsBound { get; private set; }

    public void BindTo(CommonGameplayObject target)
    {
        if (IsBound) throw new InvalidOperationException("Displayer is already bound");
        if (!target.Machine.ContainsGameplayObject(target.ObjectID)) throw new InvalidOperationException("Cannot bind an unavailable object");
        GameplayObject = target;
        IsBound = true;
        try { PrepareTarget(); OnGameplayObjectBound(); BindFields(); }
        catch { Return(); throw; }
    }

    protected virtual void PrepareTarget() { }
    protected virtual void BindFields() { }
    protected virtual void OnGameplayObjectBound() { }
    protected virtual void OnGameplayObjectUnbinding() { }
    protected virtual void OnFieldChanged(ODFieldName field) { }
    protected void NotifyField(ODFieldName field) => OnFieldChanged(field);

    protected void TrackBinding(IReturnableHandle handle) => bindings.Add(handle);
    protected void Bind(FieldChangeEventBinder field, Action callback, bool trigger = true)
    {
        TrackBinding(field.Bind(_ => callback()));
        if (trigger) callback();
    }
    protected void BindItems<T>(CollectionBinder<T> field, Action<CollectionItemChangeType, T> callback) =>
        TrackBinding(field.BindItemChanged(change => callback(change.ChangeType, change.Item)));
    protected void BindItems<K, V>(CollectionBinder<K, V> field, Action<CollectionItemChangeType, K> callback) =>
        TrackBinding(field.BindItemChanged(change => callback(change.ChangeType, change.Item)));

    public void Return()
    {
        if (!IsBound) return;
        try { OnGameplayObjectUnbinding(); }
        finally
        {
            foreach (var handle in bindings) handle.Return();
            bindings.Clear();
            IsBound = false;
            GameplayObject = default;
        }
    }

    public override void _ExitTree() => Return();
}

public abstract partial class TypedGameplayObjectDisplayer<T> : GameplayObjectDisplayer
    where T : struct, IGameplayObjectOperator, IODClass
{
    public T Target { get; private set; }

    protected override void PrepareTarget() => Target = GameplayObject.Cast<T>() ??
        throw new InvalidOperationException($"{GameplayObject.ObjectClass} cannot bind to {typeof(T).Name}");

    protected override void BindFields()
    {
        foreach (var property in typeof(T).GetProperties())
        {
            if (property.PropertyType == typeof(FieldChangeEventBinder) && property.Name.StartsWith("After") && property.Name.EndsWith("Changed"))
            {
                string fieldName = property.Name.Substring(5, property.Name.Length - 12);
                PropertyInfo? collectionProperty = typeof(T).GetProperty(fieldName + "Binder");
                if (collectionProperty != null && IsCollectionBinder(collectionProperty.PropertyType)) continue;
                var binder = (FieldChangeEventBinder)property.GetValue(Target)!;
                var method = FindCallback("On" + property.Name.Substring(5), Type.EmptyTypes);
                Bind(binder, () => { method?.Invoke(this, null); NotifyField(binder.FieldName); });
            }
            else if (property.PropertyType.IsGenericType &&
                (property.PropertyType.GetGenericTypeDefinition() == typeof(CollectionBinder<>) ||
                 property.PropertyType.GetGenericTypeDefinition() == typeof(CollectionBinder<,>)))
            {
                string name = property.Name.EndsWith("Binder") ? property.Name.Substring(0, property.Name.Length - 6) : property.Name;
                object binder = property.GetValue(Target)!;
                var field = (ODFieldName)property.PropertyType.GetField("FieldName")!.GetValue(binder)!;
                var callback = FindCallback("On" + name + "Changed", Type.EmptyTypes);
                Bind(new FieldChangeEventBinder { Machine = Machine, ObjectID = Target.ObjectID, FieldName = field },
                    () => { callback?.Invoke(this, null); NotifyField(field); });
                var keyType = property.PropertyType.GetGenericArguments()[0];
                var itemCallback = FindCallback("On" + name + "ItemChanged", new[] { typeof(CollectionItemChangeType), keyType });
                if (itemCallback != null)
                    typeof(TypedGameplayObjectDisplayer<T>).GetMethod(nameof(BindItemCallback), BindingFlags.Instance | BindingFlags.NonPublic)!
                        .MakeGenericMethod(keyType).Invoke(this, new object[] { field, itemCallback });
            }
        }
    }

    private static bool IsCollectionBinder(Type type) =>
        type.IsGenericType &&
        (type.GetGenericTypeDefinition() == typeof(CollectionBinder<>) ||
         type.GetGenericTypeDefinition() == typeof(CollectionBinder<,>));

    private MethodInfo? FindCallback(string name, Type[] parameters)
    {
        for (Type? type = GetType(); type != null; type = type.BaseType)
        {
            var result = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, parameters, null);
            if (result != null) return result;
        }
        return null;
    }

    private void BindItemCallback<TKey>(ODFieldName field, MethodInfo callback) =>
        BindItems(new CollectionBinder<TKey> { Machine = Machine, ObjectID = Target.ObjectID, FieldName = field },
            (kind, key) => callback.Invoke(this, new object?[] { kind, key }));
}
