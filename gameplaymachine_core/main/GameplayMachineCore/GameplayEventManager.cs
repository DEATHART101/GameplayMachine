using ODCore;
using System;
using System.Collections;
using System.Collections.Generic;

namespace GMCore
{
    public class GameplayEventSystemManager
    {
        #region Define

        private const string EVENT_LOG_CAT = "Event";

        #endregion

        private GameplayMachine m_machine;
        private Dictionary<GObjectID, XEventSystem.EventSystem> m_eventSystems = new Dictionary<GObjectID, XEventSystem.EventSystem>();
        private Dictionary<GObjectID, XEventSystem.EventSystem> m_itemEventSystems = new Dictionary<GObjectID, XEventSystem.EventSystem>();

        public GameplayEventSystemManager(GameplayMachine machine)
        {
            m_machine = machine;
        }

        #region Interface

        public void ClearEvent(GObjectID gObject)
        {
            m_eventSystems.Remove(gObject);
            m_itemEventSystems.Remove(gObject);
        }

        public XLifetimeObject.IReturnableHandle BindEvtDelegate<T>(GObjectID objectID, ODFieldName bindName, XEventSystem.TemplateEvtDelegateHandler<T> callBack, ObjectEventTypes eventTypes)
        {
            var eventSystem = GetOrCreateEventSystem(objectID, bindName, eventTypes);

            return eventSystem.BindEvtDelegate(bindName, XEventSystem.TypedEventSystem.GenCommonDelegate<T>(callBack));
        }

        public void UnBindEvtDelegate<T>(GObjectID objectID, ODFieldName bindName, XEventSystem.TemplateEvtDelegateHandler<T> callBack, ObjectEventTypes eventTypes)
        {
            XEventSystem.EventSystem evtSystem = TryGetEventSystem(objectID, bindName, eventTypes);

            evtSystem?.Internal_UnBindEvtDelegate(bindName, callBack);
        }

        public XLifetimeObject.IReturnableHandle BindEvtDelegate(GObjectID objectID, ODFieldName bindName, XEventSystem.EvtCommonHandler callBack, ObjectEventTypes eventTypes)
        {
            var eventSystem = GetOrCreateEventSystem(objectID, bindName, eventTypes);

            return eventSystem.BindEvtDelegate(bindName, callBack);
        }

        public void UnBindEvtDelegate(GObjectID objectID, ODFieldName bindName, XEventSystem.EvtCommonHandler callBack, ObjectEventTypes eventTypes)
        {
            XEventSystem.EventSystem evtSystem = TryGetEventSystem(objectID, bindName, eventTypes);

            evtSystem?.UnBindEvtDelegate(bindName, callBack);
        }

        public void BroadCastEvtDelegate(GObjectID objectID, ODFieldName bindName, object context, ObjectEventTypes eventTypes)
        {
            XEventSystem.EventSystem evtSystem = TryGetEventSystem(objectID, bindName, eventTypes);
            evtSystem?.BroadCastEvtDelegate(bindName, context);
        }

        #endregion

        private XEventSystem.EventSystem TryGetEventSystem(GObjectID objectID, ODFieldName bindName, ObjectEventTypes eventType)
        {
            var eventSystems = GetEventSystems(eventType);
            if (eventSystems == null)
                return null;

            XEventSystem.EventSystem evtSystem;
            if (!eventSystems.TryGetValue(objectID, out evtSystem))
            {
                return null;
            }

            return evtSystem;
        }

        private XEventSystem.EventSystem GetOrCreateEventSystem(GObjectID objectID, ODFieldName bindName, ObjectEventTypes eventType)
        {
            var eventSystems = GetEventSystems(eventType);
            if (eventSystems == null)
                throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unsupported event type");

            XEventSystem.EventSystem evtSystem;
            if (!eventSystems.TryGetValue(objectID, out evtSystem))
            {
                evtSystem = new XEventSystem.EventSystem();
                eventSystems[objectID] = evtSystem;
            }

            return evtSystem;
        }

        private Dictionary<GObjectID, XEventSystem.EventSystem> GetEventSystems(ObjectEventTypes eventType)
        {
            switch (eventType)
            {
                case ObjectEventTypes.Changed:
                    return m_eventSystems;
                case ObjectEventTypes.ItemChanged:
                    return m_itemEventSystems;
                default:
                    return null;
            }
        }
    }

    public enum CollectionItemChangeType
    {
        Add,
        Remove,
        Changed,
    }

    public readonly struct CollectionItemChangedParam<T>
    {
        public CollectionItemChangeType ChangeType { get; }
        public T Item { get; }

        internal CollectionItemChangedParam(CollectionItemChangeType changeType, T item)
        {
            ChangeType = changeType;
            Item = item;
        }
    }

    internal readonly struct CollectionItemChangedContext
    {
        public CollectionItemChangeType ChangeType { get; }
        public object Item { get; }

        public CollectionItemChangedContext(CollectionItemChangeType changeType, object item)
        {
            ChangeType = changeType;
            Item = item;
        }
    }

    public struct FieldChangeEventBinder
    {
        public GameplayMachine Machine { get; set; }
        public GObjectID ObjectID { get; set; }
        public ODFieldName FieldName;

        public XLifetimeObject.IReturnableHandle Bind(XEventSystem.EvtCommonHandler rhs)
        {
            return Machine.EventManager.BindEvtDelegate(ObjectID, FieldName, rhs, ObjectEventTypes.Changed);
        }

        public XLifetimeObject.IReturnableHandle BindAndTrigger(XEventSystem.EvtCommonHandler rhs)
        {
            var result = Bind(rhs);
            rhs.Invoke(null);
            return result;
        }

        public void UnBind(XEventSystem.EvtCommonHandler rhs)
        {
            Machine.EventManager.UnBindEvtDelegate(ObjectID, FieldName, rhs, ObjectEventTypes.Changed);
        }

        public void Invoke()
        {
            Machine.EventManager.BroadCastEvtDelegate(ObjectID, FieldName, null, ObjectEventTypes.Changed);
        }
    }

    public struct CollectionBinder<T>
    {
        public GameplayMachine Machine;
        public GObjectID ObjectID;
        public ODFieldName FieldName;

        public XLifetimeObject.IReturnableHandle BindItemChanged(
            XEventSystem.TemplateEvtDelegateHandler<CollectionItemChangedParam<T>> callBack)
        {
            return Machine.EventManager.BindEvtDelegate(ObjectID, FieldName, context =>
            {
                CollectionItemChangedContext itemChanged = (CollectionItemChangedContext)context;
                callBack(new CollectionItemChangedParam<T>(itemChanged.ChangeType, (T)itemChanged.Item));
            }, ObjectEventTypes.ItemChanged);
        }

        public XLifetimeObject.IReturnableHandle BindChanged(XEventSystem.EvtCommonHandler callBack)
        {
            return Machine.EventManager.BindEvtDelegate(ObjectID, FieldName, callBack, ObjectEventTypes.Changed);
        }

        public XLifetimeObject.IReturnableHandle BindAndTrigger(XEventSystem.EvtCommonHandler callBack)
        {
            var result = BindChanged(callBack);
            callBack.Invoke(null);
            return result;
        }

        public void UnBindChanged(XEventSystem.EvtCommonHandler callBack)
        {
            Machine.EventManager.UnBindEvtDelegate(ObjectID, FieldName, callBack, ObjectEventTypes.Changed);
        }

        public void Invoke()
        {
            Machine.EventManager.BroadCastEvtDelegate(ObjectID, FieldName, null, ObjectEventTypes.Changed);
        }
    }

    public struct CollectionBinder<K, V>
    {
        public GameplayMachine Machine;
        public GObjectID ObjectID;
        public ODFieldName FieldName;

        public XLifetimeObject.IReturnableHandle BindItemChanged(
            XEventSystem.TemplateEvtDelegateHandler<CollectionItemChangedParam<K>> callBack)
        {
            return Machine.EventManager.BindEvtDelegate(ObjectID, FieldName, context =>
            {
                CollectionItemChangedContext itemChanged = (CollectionItemChangedContext)context;
                callBack(new CollectionItemChangedParam<K>(itemChanged.ChangeType, (K)itemChanged.Item));
            }, ObjectEventTypes.ItemChanged);
        }

        public XLifetimeObject.IReturnableHandle BindChanged(XEventSystem.EvtCommonHandler callBack)
        {
            return Machine.EventManager.BindEvtDelegate(ObjectID, FieldName, callBack, ObjectEventTypes.Changed);
        }

        public XLifetimeObject.IReturnableHandle BindAndTrigger(XEventSystem.EvtCommonHandler callBack)
        {
            var result = BindChanged(callBack);
            callBack.Invoke(null);
            return result;
        }

        public void UnBindChanged(XEventSystem.EvtCommonHandler callBack)
        {
            Machine.EventManager.UnBindEvtDelegate(ObjectID, FieldName, callBack, ObjectEventTypes.Changed);
        }

        public void Invoke()
        {
            Machine.EventManager.BroadCastEvtDelegate(ObjectID, FieldName, null, ObjectEventTypes.Changed);
        }
    }
}

