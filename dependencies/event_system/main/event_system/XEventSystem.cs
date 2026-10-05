using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using XLifetimeObject;

namespace XEventSystem
{
    public struct BindCommonReturnHandle : IReturnableHandle
    {
        private EventSystem m_eventSystem;
        private object m_bindKey;
        private object m_callBack;

        public BindCommonReturnHandle(EventSystem eventSystem, object bindType, object inCallBack)
        {
            m_eventSystem = eventSystem;
            m_bindKey = bindType;
            m_callBack = inCallBack;
        }

        public void Return()
        {
            m_eventSystem.Internal_UnBindEvtDelegate(m_bindKey, m_callBack);
        }
    }

    public delegate void EvtCommonHandler(object context);
    public delegate void TemplateEvtDelegateHandler<T>(T context);

    public class HandlerWrapper
    {
        public object Handler;

        public HandlerWrapper(object handler)
        {
            Handler = handler;
        }

        public void Invoke(object context)
        {
            OnInvoked(context);
        }

        public virtual object GetTarget()
        {
            return ((EvtCommonHandler)Handler).Target;
        }

        protected virtual void OnInvoked(object context)
        {
            ((EvtCommonHandler)Handler)(context);
        }

        public bool HandlerEquals(object other)
        {
            return Handler.Equals(other);
        }
    }

    public class HandlerWrapper<T> : HandlerWrapper
    {
        public HandlerWrapper(object handler) : base(handler)
        {
        }

        public override object GetTarget()
        {
            return ((TemplateEvtDelegateHandler<T>)Handler).Target;
        }

        protected override void OnInvoked(object context)
        {
            ((TemplateEvtDelegateHandler<T>)Handler)((T)context);
        }
    }

    public class TypedEventSystem
    {
        private EventSystem m_eventSystem = new EventSystem();

        #region Interface

        public IReturnableHandle BindEvtDelegate<T>(TemplateEvtDelegateHandler<T> callBack)
        {
            return m_eventSystem.BindEvtDelegate(typeof(T), GenCommonDelegate<T>(callBack));
        }

        public void UnBindEvtDelegate<T>(TemplateEvtDelegateHandler<T> callBack)
        {
            m_eventSystem.Internal_UnBindEvtDelegate(typeof(T), callBack);
        }

        public IReturnableHandle BindEvtDelegate(Type evtContextType, EvtCommonHandler callBack)
        {
            return m_eventSystem.BindEvtDelegate(evtContextType, callBack);
        }

        public void UnBindEvtDelegate(Type evtContextType, EvtCommonHandler callBack)
        {
            m_eventSystem.UnBindEvtDelegate(evtContextType, callBack);
        }

        public void BroadCastEvtDelegate(object evtDelegateContext)
        {
            if (evtDelegateContext == null)
            {
                return;
            }
            m_eventSystem.BroadCastEvtDelegate(evtDelegateContext.GetType(), evtDelegateContext);
        }

        public void ClearEvent<T>()
        {
            ClearEvent(typeof(T));
        }

        public void ClearEvent(Type bindKey)
        {
            m_eventSystem.ClearEvent(bindKey);
        }

        #endregion

        public static HandlerWrapper<T> GenCommonDelegate<T>(TemplateEvtDelegateHandler<T> handler)
        {
            return new HandlerWrapper<T>(handler);
        }
    }

    public class EventSystem
    {
        #region Define

        private enum DelayedBindActionTypes { Add, Remove };

        private struct DelayedBindAction
        {
            public DelayedBindActionTypes DelayedBindActionType;
            public object BindKey;
            public object Handler;

            public DelayedBindAction(DelayedBindActionTypes delayedBindActionType, object bindKey, object handler)
            {
                DelayedBindActionType = delayedBindActionType;
                BindKey = bindKey;
                Handler = handler;
            }
        }

        #endregion

        #region Interface

        public IReturnableHandle BindEvtDelegate(object bindKey, EvtCommonHandler callBack)
        {
            return InternalBindEvtDelegate(bindKey, callBack);
        }

        public void UnBindEvtDelegate(object bindKey, EvtCommonHandler callBack)
        {
            InternalUnBindEvtDelegate(bindKey, callBack);
        }

        public IReturnableHandle BindEvtDelegate(object bindKey, HandlerWrapper callBack)
        {
            return InternalBindEvtDelegate(bindKey, callBack);
        }

        public void UnBindEvtDelegate(object bindKey, HandlerWrapper callBack)
        {
            InternalUnBindEvtDelegate(bindKey, callBack.Handler);
        }

        public void BroadCastEvtDelegate(object bindKey, object context)
        {
            InternalBroadCastEvtDelegate(bindKey, context);
        }

        public void ClearEvent(object bindKey)
        {
            InternalClearEvent(bindKey);
        }

        public Func<object, bool> TargetValidator
        {
            set
            {
                m_targetValidator = value;
            }
        }

        #endregion

        #region Template Version

        // Dictionary<Key, List<Handler>>
        private Dictionary<object, List<object>> m_type2Event = new Dictionary<object, List<object>>();
        private Func<object, bool> m_targetValidator;

        private int m_broadCastingLayer = 0;

        private List<DelayedBindAction> m_delayedBindActions = new List<DelayedBindAction>();

        private void InternalBroadCastEvtDelegate(object bindKey, object context)
        {
            m_broadCastingLayer++;

            List<object> outHandlers;
            if (m_type2Event.TryGetValue(bindKey, out outHandlers))
            {
                foreach (var item in outHandlers)
                {
                    Func<object, bool> targetValidator = m_targetValidator ?? EventHelper.IsTargetValid;
                    if (item is HandlerWrapper wrapper)
                    {
                        if (!targetValidator(wrapper.GetTarget()))
                        {
                            InternalUnBindEvtDelegate(bindKey, wrapper.Handler);
                            continue;
                        }
                        else
                        {
                            wrapper.Invoke(context);
                        }
                    }
                    else
                    {
                        EvtCommonHandler evtCommonHandler = (EvtCommonHandler)item;
                        if (!targetValidator(evtCommonHandler.Target))
                        {
                            InternalUnBindEvtDelegate(bindKey, item);
                            continue;
                        }
                        else
                        {
                            evtCommonHandler.Invoke(context);
                        }
                    }
                }
            }

            m_broadCastingLayer--;

            InternalProcessDelayedActions();
        }

        private IReturnableHandle InternalBindEvtDelegate(object bindKey, object handler)
        {
            IReturnableHandle result = new BindCommonReturnHandle(this, bindKey, handler);
            if (m_broadCastingLayer != 0)
            {
                m_delayedBindActions.Add(new DelayedBindAction(DelayedBindActionTypes.Add, bindKey, handler));
                return result;
            }

            InternalAddCallBack(bindKey, handler);
            return result;
        }

        private void InternalAddCallBack(object bindKey, object callBack)
        {
            List<object> callBackList;
            if (!m_type2Event.TryGetValue(bindKey, out callBackList))
            {
                callBackList = new List<object>();
                m_type2Event.Add(bindKey, callBackList);
            }

            callBackList.Add(callBack);
        }

        public void Internal_UnBindEvtDelegate(object bindKey, object handler)
        {
            if (handler is HandlerWrapper wrapper)
            {
                InternalUnBindEvtDelegate(bindKey, wrapper.Handler);
            }
            else
            {
                InternalUnBindEvtDelegate(bindKey, handler);
            }
        }

        private void InternalUnBindEvtDelegate(object bindKey, object handler)
        {
            if (m_broadCastingLayer != 0)
            {
                m_delayedBindActions.Add(new DelayedBindAction(DelayedBindActionTypes.Remove, bindKey, handler));
                return;
            }

            InternalRemoveCallBack(bindKey, handler);
        }

        private void InternalClearEvent(object bindKey)
        {
            m_type2Event.Remove(bindKey);
        }

        private void InternalRemoveCallBack(object bindKey, object handler)
        {
            List<object> callBackList;
            if (!m_type2Event.TryGetValue(bindKey, out callBackList))
            {
                return;
            }

            int count = callBackList.Count;
            for (int i = 0; i < count; i++)
            {
                object item = callBackList[i];
                if (item is HandlerWrapper wrapper)
                {
                    if (wrapper.HandlerEquals(handler))
                    {
                        callBackList.RemoveAt(i);
                        break;
                    }
                }
                else 
                {
                    if (item.Equals(handler))
                    {
                        callBackList.RemoveAt(i);
                        break;
                    }
                }
            }
            if (callBackList.Count == 0)
            {
                m_type2Event.Remove(bindKey);
            }
        }

        private void InternalProcessDelayedActions()
        {
            if (m_broadCastingLayer != 0)
            {
                return;
            }

            foreach (var delayedAction in m_delayedBindActions)
            {
                if (delayedAction.DelayedBindActionType == DelayedBindActionTypes.Add)
                {
                    InternalAddCallBack(delayedAction.BindKey, delayedAction.Handler);
                }
                else
                {
                    InternalRemoveCallBack(delayedAction.BindKey, delayedAction.Handler);
                }
            }
            m_delayedBindActions.Clear();
        }

        #endregion
    }

    public static class EventHelper
    {
        public static bool IsTargetValid(object obj)
        {
            return true;
        }
    }
}