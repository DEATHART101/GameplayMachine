using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace CommonSerialize
{
    public interface IListDataProvider
    {
        public IEnumerable<IEnumerable<KeyValuePair<string, string>>> Provide();
    }

    public interface IMapDataProvider
    {
        public IEnumerable<KeyValuePair<string, IEnumerable<KeyValuePair<string, string>>>> Provide();
    }
}
