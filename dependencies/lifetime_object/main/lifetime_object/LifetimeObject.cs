using System;
using System.Collections.Generic;

namespace XLifetimeObject
{
    public interface IReturnableHandle
    {
        public void Return();

        public void BindTo(LifetimeObject lifetimeObject)
        {
            lifetimeObject.Bind(this);
        }
    }

    public class LifetimeObject : IReturnableHandle
    {
        private List<IReturnableHandle> m_returnabls = new List<IReturnableHandle>();

        public void Bind(IReturnableHandle newHandle)
        {
            if (newHandle == null)
            {
                return;
            }

            m_returnabls.Add(newHandle);
        }

        public void Bind<T>(IEnumerable<T> newHandle)
            where T : IReturnableHandle
        {
            foreach (var item in newHandle)
            {
                if (item == null)
                {
                    continue;
                }

                m_returnabls.Add(item);
            }
        }

        public void Bind(params IReturnableHandle[] handles)
        {
            Bind(handles);
        }

        public void Return()
        {
            foreach (var item in m_returnabls)
            {
                item.Return();
            }
            m_returnabls.Clear();
        }
    }
}

