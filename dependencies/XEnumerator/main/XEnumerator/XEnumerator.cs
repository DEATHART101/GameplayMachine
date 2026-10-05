using System;
using System.Collections;
using System.Collections.Generic;

namespace XEnumerator
{
    [System.Serializable]
    public class XEnumerator
    {
        private object m_lastValue = null;
        private bool m_paused = false;
        private LinkedList<IEnumerator> m_enumators = new LinkedList<IEnumerator>();

        public object Current
        {
            get
            {
                return m_lastValue;
            }
        }

        public bool Paused
        {
            get
            {
                return m_paused;
            }
        }

        public void Start(IEnumerator enumerator)
        {
            if (m_enumators.Count != 0)
            {
                throw new Exception("Enumeration already started. Call Clear first if you want to start a new one.");
            }

            Put(enumerator);
        }

        public void Clear()
        {
            m_enumators.Clear();
            m_lastValue = null;
            m_paused = false;
        }

        public bool Step(bool pauseOnEnd = false)
        {
            var lastNode = m_enumators.Last;
            m_lastValue = null;
            m_paused = false;
            while (lastNode != null)
            {
                IEnumerator enumerator = lastNode.Value;
                if (enumerator.MoveNext())
                {
                    object value = enumerator.Current;
                    if (value != null)
                    {
                        if (CanIterator(value))
                        {
                            PutIterator(value);
                            return Step(pauseOnEnd);
                        }
                        else
                        {
                            m_lastValue = value;
                        }
                    }
                    return true;
                }
                else
                {
                    var theNode = lastNode;
                    lastNode = lastNode.Previous;
                    m_enumators.Remove(theNode);

                    if (pauseOnEnd)
                    {
                        m_paused = true;
                        return true;
                    }
                }
            }

            return false;
        }

        private void Put(IEnumerable enumerable)
        {
            Put(enumerable.GetEnumerator());
        }

        private void Put(IEnumerator enumerator)
        {
            m_enumators.AddLast(enumerator);
            m_lastValue = null;
        }

        private void PutIterator(object iterator)
        {
            if (iterator is IEnumerable)
            {
                Put(iterator as IEnumerable);
            }
            else
            {
                Put(iterator as IEnumerator);
            }
        }

        private bool CanIterator(object obj)
        {
            return obj is IEnumerable || obj is IEnumerator;
        }
    }
}